using Microsoft.Extensions.Options;

namespace AutomaticPaperlessUploader.Storage;

public record QuietWaitResult(bool BecameQuiet, TimeSpan Waited);

/// <summary>
/// Waits for the scanner to stop writing before the drive is taken away from it.
///
/// Pressing the key while a scan is still being written can hand the upload a
/// half written file. There is no kernel flag for "the host is writing": the mass storage
/// LUN exposes only its backing file, read only flag and eject controls. What does move is
/// the mtime of the backing file itself, because the gadget writes the host's data through
/// it, so a file that has not been touched for a few seconds is a good proxy for a scanner
/// that has finished.
/// </summary>
public class ScannerActivityMonitor {
    private ILogger<ScannerActivityMonitor> Logger { get; }
    private StorageOptions StorageOptions { get; }

    public ScannerActivityMonitor(ILogger<ScannerActivityMonitor> logger, IOptions<StorageOptions> storageOptions) {
        Logger = logger;
        StorageOptions = storageOptions.Value;
    }

    /// <summary>
    /// True if the image was written to recently enough that the scanner is probably still
    /// busy with it.
    /// </summary>
    public bool IsBusy(string imagePath) => TimeSinceLastWrite(imagePath) < QuietPeriod;

    private TimeSpan QuietPeriod => TimeSpan.FromMilliseconds(Math.Max(0, StorageOptions.QuietPeriodMs));

    private TimeSpan MaxWait => TimeSpan.FromMilliseconds(Math.Max(0, StorageOptions.MaxWaitForQuietMs));

    private TimeSpan TimeSinceLastWrite(string imagePath) {
        try {
            return DateTime.UtcNow - File.GetLastWriteTimeUtc(imagePath);
        }
        catch (Exception exception) {
            // Treat an unreadable image as quiet. Refusing to upload because we could not
            // stat a file would be a worse failure than the one we are guarding against.
            Logger.LogWarning(exception, "Could not read the write time of '{Image}'. Assuming it is idle.", imagePath);
            return TimeSpan.MaxValue;
        }
    }

    /// <summary>
    /// Blocks until the image has been untouched for the configured quiet period, or until
    /// the maximum wait elapses.
    ///
    /// Returning <see cref="QuietWaitResult.BecameQuiet"/> as false means the caller is
    /// about to read a file the scanner may still be writing.
    /// </summary>
    public async Task<QuietWaitResult> WaitForQuietAsync(
        string imagePath,
        IProgress<TimeSpan>? stillWaiting = null,
        CancellationToken cancellationToken = default) {

        if (QuietPeriod <= TimeSpan.Zero) {
            return new QuietWaitResult(true, TimeSpan.Zero);
        }

        var startedAt = DateTimeOffset.UtcNow;

        if (!IsBusy(imagePath)) {
            return new QuietWaitResult(true, TimeSpan.Zero);
        }

        Logger.LogInformation(
            "'{Image}' was written to {Age:F1}s ago. Waiting for the scanner to finish.",
            imagePath,
            TimeSinceLastWrite(imagePath).TotalSeconds);

        var reported = false;

        while (true) {
            var waited = DateTimeOffset.UtcNow - startedAt;

            if (!IsBusy(imagePath)) {
                Logger.LogInformation("Scanner has been idle for {Quiet:F1}s. Continuing after {Waited:F1}s.",
                    QuietPeriod.TotalSeconds,
                    waited.TotalSeconds);
                return new QuietWaitResult(true, waited);
            }

            if (waited >= MaxWait) {
                return new QuietWaitResult(false, waited);
            }

            if (!reported) {
                // Let the caller show "waiting" only when there is actually a wait, rather
                // than flickering it on every press.
                stillWaiting?.Report(MaxWait);
                reported = true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }
}

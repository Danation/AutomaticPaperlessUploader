using AutomaticPaperlessUploader.Paperless;
using AutomaticPaperlessUploader.Status;
using AutomaticPaperlessUploader.Storage;
using Microsoft.Extensions.Options;

namespace AutomaticPaperlessUploader;

/// <summary>
/// Runs one full cycle: swap the media the scanner sees, then mount the released image
/// and upload whatever the scanner left there.
/// </summary>
public class UploadCycle {
    private ILogger<UploadCycle> Logger { get; }
    private GadgetController GadgetController { get; }
    private ImageMounter ImageMounter { get; }
    private PaperlessClient PaperlessClient { get; }
    private PaperlessOptions PaperlessOptions { get; }
    private StatusReporter StatusReporter { get; }

    /// <summary>
    /// Guarantees only one cycle runs at a time. A second keypress during an upload is
    /// dropped rather than queued, because the drive it would target is already in play.
    /// </summary>
    private SemaphoreSlim Gate { get; } = new(1, 1);

    public UploadCycle(
        ILogger<UploadCycle> logger,
        GadgetController gadgetController,
        ImageMounter imageMounter,
        PaperlessClient paperlessClient,
        IOptions<PaperlessOptions> paperlessOptions,
        StatusReporter statusReporter) {

        Logger = logger;
        GadgetController = gadgetController;
        ImageMounter = imageMounter;
        PaperlessClient = paperlessClient;
        PaperlessOptions = paperlessOptions.Value;
        StatusReporter = statusReporter;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default) {
        if (!await Gate.WaitAsync(0, cancellationToken)) {
            Logger.LogWarning("An upload cycle is already running. Ignoring this request.");
            return;
        }

        try {
            await StatusReporter.ReportAsync(DeviceStatus.Working, "Swapping drive", cancellationToken);

            var releasedImage = await GadgetController.SwapAsync(cancellationToken);

            await ImageMounter.UseMountedImageAsync(
                releasedImage,
                mountPoint => UploadFilesAsync(mountPoint, cancellationToken),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) {
            Logger.LogError(exception, "Upload cycle failed.");

            // Report on an uncancelled token: the user still needs to see this even if
            // the service is shutting down underneath us.
            await StatusReporter.ReportAsync(DeviceStatus.Failed, exception.Message, CancellationToken.None);
        }
        finally {
            Gate.Release();
        }
    }

    private async Task UploadFilesAsync(string mountPoint, CancellationToken cancellationToken) {
        var files = Directory
            .EnumerateFiles(mountPoint, "*", SearchOption.AllDirectories)
            .Where(IsUploadable)
            .OrderBy(x => x)
            .ToList();

        if (files.Count == 0) {
            Logger.LogInformation("No files to upload on this drive.");
            await StatusReporter.ReportAsync(DeviceStatus.NothingToUpload, cancellationToken: cancellationToken);
            return;
        }

        Logger.LogInformation("Found {Count} file(s) to upload.", files.Count);
        await StatusReporter.ReportAsync(
            DeviceStatus.Working,
            $"Uploading {files.Count} file(s)",
            cancellationToken);

        var succeeded = 0;
        string? firstError = null;

        foreach (var file in files) {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await PaperlessClient.UploadAsync(file, cancellationToken);

            if (!result.Succeeded) {
                // Leave the file in place so the next cycle retries it rather than
                // silently losing a scan.
                Logger.LogError("Upload failed for '{FileName}': {Error}", result.FileName, result.Error);
                firstError ??= result.Error;
                continue;
            }

            succeeded++;

            if (PaperlessOptions.DeleteAfterUpload) {
                TryDelete(file);
            }
        }

        Logger.LogInformation("Uploaded {Succeeded} of {Total} file(s).", succeeded, files.Count);

        // A partial success still leaves scans stranded on the drive, so report it as a
        // failure. Anything short of "everything is filed" needs the user to come back.
        if (succeeded == files.Count) {
            await StatusReporter.ReportAsync(
                DeviceStatus.Succeeded,
                $"{succeeded} file(s) uploaded",
                cancellationToken);
        }
        else {
            await StatusReporter.ReportAsync(
                DeviceStatus.Failed,
                $"Uploaded {succeeded} of {files.Count}. {firstError}".Trim(),
                cancellationToken);
        }
    }

    private bool IsUploadable(string path) {
        var fileName = Path.GetFileName(path);

        // Skip the metadata sidecars Windows and macOS scatter across FAT volumes.
        if (fileName.StartsWith("._", StringComparison.Ordinal)) {
            return false;
        }

        if (path.Contains("/.Trash", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/System Volume Information", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var extension = Path.GetExtension(path);
        return PaperlessOptions.AllowedExtensions
            .Any(x => string.Equals(x, extension, StringComparison.OrdinalIgnoreCase));
    }

    private void TryDelete(string path) {
        try {
            File.Delete(path);
            Logger.LogInformation("Deleted '{FileName}' from the drive.", Path.GetFileName(path));
        }
        catch (Exception exception) {
            Logger.LogWarning(exception, "Uploaded '{FileName}' but could not delete it.", Path.GetFileName(path));
        }
    }
}

using Microsoft.Extensions.Options;

namespace AutomaticPaperlessUploader.Status;

/// <summary>
/// Owns what the device is currently saying, and what happens next.
///
/// Callers report what just happened and nothing more. The rules about how long a state
/// stays up, which states clear themselves and which have to be replaced, live here rather
/// than being spread across whoever happens to report them. That keeps the enum and the
/// behaviour that goes with it in one place, and means a second indicator inherits the
/// same behaviour for free.
///
/// Indicator failures are swallowed. A loose wire on a status LED must never stop a scan
/// from reaching Paperless, and one failing indicator must not silence the others.
/// </summary>
public class StatusReporter {
    private ILogger<StatusReporter> Logger { get; }
    private IReadOnlyList<IStatusIndicator> Indicators { get; }
    private DisplayOptions DisplayOptions { get; }

    /// <summary>
    /// Distinguishes the current state from a stale one. A scheduled follow up only acts
    /// if nothing else has been reported since, so a new cycle started while a result is
    /// still on screen is never overwritten by the old result's timer.
    /// </summary>
    private long Generation;

    public StatusReporter(
        ILogger<StatusReporter> logger,
        IEnumerable<IStatusIndicator> indicators,
        IOptions<DisplayOptions> displayOptions) {

        Logger = logger;
        Indicators = indicators.ToList();
        DisplayOptions = displayOptions.Value;
    }

    /// <summary>The most recent update, so a late attaching indicator can catch up.</summary>
    public StatusUpdate? Current { get; private set; }

    public Task ReportAsync(DeviceStatus status, string? detail = null, CancellationToken cancellationToken = default) =>
        ReportAsync(new StatusUpdate(status, detail), cancellationToken);

    public async Task ReportAsync(StatusUpdate update, CancellationToken cancellationToken = default) {
        var generation = Interlocked.Increment(ref Generation);

        Current = update;
        await ShowAsync(update, cancellationToken);

        ScheduleFollowUp(update, generation, cancellationToken);
    }

    private async Task ShowAsync(StatusUpdate update, CancellationToken cancellationToken) {
        foreach (var indicator in Indicators) {
            try {
                await indicator.ShowAsync(update, cancellationToken);
            }
            catch (Exception exception) {
                Logger.LogWarning(
                    exception,
                    "Status indicator {Indicator} failed to show {Status}. Continuing.",
                    indicator.GetType().Name,
                    update.Status);
            }
        }
    }

    /// <summary>
    /// Some states clear themselves. A result is worth reading, so it stays up for a while
    /// and then falls back to Ready; a failure does not clear, because the files it refers
    /// to are still sitting on the drive waiting for another attempt.
    /// </summary>
    private void ScheduleFollowUp(StatusUpdate update, long generation, CancellationToken cancellationToken) {
        if (!ClearsItself(update.Status)) {
            return;
        }

        var linger = TimeSpan.FromSeconds(Math.Max(0, DisplayOptions.ResultLingerSeconds));

        if (linger <= TimeSpan.Zero) {
            return;
        }

        _ = Task.Run(async () => {
            try {
                await Task.Delay(linger, cancellationToken);
            }
            catch (OperationCanceledException) {
                return;
            }

            // Anything reported since supersedes this, including a cycle the user started
            // while the result was still showing.
            if (Interlocked.Read(ref Generation) != generation) {
                return;
            }

            await ReportAsync(DeviceStatus.Ready, cancellationToken: cancellationToken);
        }, cancellationToken);
    }

    private static bool ClearsItself(DeviceStatus status) => status switch {
        DeviceStatus.Succeeded => true,
        DeviceStatus.NothingToUpload => true,

        // Failed is deliberately absent: it stays until the next cycle replaces it.
        _ => false,
    };
}

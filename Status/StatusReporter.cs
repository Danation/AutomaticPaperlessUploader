namespace AutomaticPaperlessUploader.Status;

/// <summary>
/// Fans a status update out to every attached indicator.
///
/// This is the seam that keeps <see cref="UploadCycle"/> ignorant of hardware: it reports
/// what happened, and whatever is plugged in decides how to show it. Adding an LED or a
/// display becomes a registration change rather than a change to the upload logic.
///
/// Indicator failures are swallowed. A loose wire on a status LED must never stop a scan
/// from reaching Paperless, and one failing indicator must not silence the others.
/// </summary>
public class StatusReporter {
    private ILogger<StatusReporter> Logger { get; }
    private IReadOnlyList<IStatusIndicator> Indicators { get; }

    public StatusReporter(ILogger<StatusReporter> logger, IEnumerable<IStatusIndicator> indicators) {
        Logger = logger;
        Indicators = indicators.ToList();
    }

    /// <summary>The most recent update, so a late attaching indicator can catch up.</summary>
    public StatusUpdate? Current { get; private set; }

    public Task ReportAsync(DeviceStatus status, string? detail = null, CancellationToken cancellationToken = default) =>
        ReportAsync(new StatusUpdate(status, detail), cancellationToken);

    public async Task ReportAsync(StatusUpdate update, CancellationToken cancellationToken = default) {
        Current = update;

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
}

namespace AutomaticPaperlessUploader.Status;

/// <summary>
/// Writes status to the log. Always registered, so the device is never completely mute
/// even with no hardware attached, and journalctl shows the same story the hardware does.
/// </summary>
public class LoggingStatusIndicator : IStatusIndicator {
    private ILogger<LoggingStatusIndicator> Logger { get; }

    public LoggingStatusIndicator(ILogger<LoggingStatusIndicator> logger) {
        Logger = logger;
    }

    public Task ShowAsync(StatusUpdate update, CancellationToken cancellationToken = default) {
        var detail = string.IsNullOrWhiteSpace(update.Detail) ? "" : $": {update.Detail}";

        if (update.Status == DeviceStatus.Failed) {
            Logger.LogError("Status {Status}{Detail}", update.Status, detail);
        }
        else {
            Logger.LogInformation("Status {Status}{Detail}", update.Status, detail);
        }

        return Task.CompletedTask;
    }
}

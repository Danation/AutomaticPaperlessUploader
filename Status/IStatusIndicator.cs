namespace AutomaticPaperlessUploader.Status;

public enum DeviceStatus {
    /// <summary>Service is starting and has not yet armed the keypad.</summary>
    Starting,

    /// <summary>Waiting for a key press.</summary>
    Ready,

    /// <summary>A cycle is in flight. Do not cut power.</summary>
    Working,

    /// <summary>Everything on the drive reached Paperless.</summary>
    Succeeded,

    /// <summary>The drive held no uploadable files.</summary>
    NothingToUpload,

    /// <summary>
    /// At least one file did not upload. Those files are still on the drive, so this
    /// needs to persist until the next cycle rather than flashing by unnoticed.
    /// </summary>
    Failed,
}

/// <param name="Status">What the device is doing.</param>
/// <param name="Detail">
/// Short human readable context, for indicators that can render text. Indicators that
/// only have a colour to work with are free to ignore it.
/// </param>
public record StatusUpdate(DeviceStatus Status, string? Detail = null) {
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.Now;

    /// <summary>
    /// A failure is the only state the user must not miss, because it means a scan is
    /// still sitting on the drive believing it was filed. Indicators that can hold a
    /// state should keep showing this until the next cycle replaces it, rather than
    /// clearing it after a moment the way a success can be cleared.
    /// </summary>
    public bool ShouldLatch => Status == DeviceStatus.Failed;
}

/// <summary>
/// Something that can tell the user what the device is doing: an LED, a buzzer, a
/// display, or just the log.
///
/// Implementations should not throw. Feedback hardware failing is never a reason to stop
/// uploading, so <see cref="StatusReporter"/> isolates faults as a backstop.
/// </summary>
public interface IStatusIndicator {
    Task ShowAsync(StatusUpdate update, CancellationToken cancellationToken = default);
}

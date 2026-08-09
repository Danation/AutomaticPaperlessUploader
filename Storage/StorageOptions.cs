namespace AutomaticPaperlessUploader.Storage;

public class StorageOptions {
    /// <summary>
    /// The two backing images that alternate. One is exposed to the scanner while
    /// the other is mounted locally for uploading.
    /// </summary>
    public List<string> Images { get; set; } = new();

    /// <summary>
    /// Sysfs path of the mass storage LUN backing file. Writing a path here swaps the
    /// media the USB host sees; writing an empty string ejects it.
    /// </summary>
    public string LunFilePath { get; set; } = "";

    public string MountPoint { get; set; } = "/mnt/usb_share";

    /// <summary>
    /// Pause between ejecting and inserting so the host registers a media change.
    /// </summary>
    public int MediaChangeDelayMs { get; set; } = 500;

    /// <summary>
    /// How many times to retry writing the LUN backing file before giving up.
    ///
    /// Applies to every write, not just the eject. The host holds the LUN busy while it
    /// reacts to a media change, so inserting the replacement image can fail for exactly
    /// the same reason ejecting the old one can.
    /// </summary>
    public int LunWriteRetries { get; set; } = 5;

    public int LunWriteRetryDelayMs { get; set; } = 500;

    /// <summary>
    /// How many polite ejects to try before forcing.
    ///
    /// Kept low deliberately. A host with the medium mounted holds the removal lock for as
    /// long as it stays mounted, so retrying is only worth doing in case it happens to be
    /// mid release; beyond that it is just delay before the inevitable forced eject.
    /// </summary>
    public int EjectAttemptsBeforeForcing { get; set; } = 2;

    /// <summary>
    /// Ignore repeat submissions within this window, so keypad bounce or an impatient
    /// second press cannot start a competing upload cycle.
    /// </summary>
    public int SubmitCooldownMs { get; set; } = 3000;

    /// <summary>
    /// How long the exposed image must go untouched before the scanner is considered
    /// finished. The gadget writes the host's data through the backing file, so its mtime
    /// is the only available signal that a scan is still arriving.
    /// </summary>
    public int QuietPeriodMs { get; set; } = 3000;

    /// <summary>
    /// How long to wait for that quiet period before giving up and swapping anyway.
    ///
    /// Proceeding risks reading a half written file, but the alternative is a key press
    /// that appears to do nothing, so the timeout is logged loudly instead.
    /// </summary>
    public int MaxWaitForQuietMs { get; set; } = 30000;
}

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
    /// How many times to retry a polite eject before forcing it. The scanner briefly
    /// holds the medium after writing, which makes the kernel report the LUN as busy.
    /// </summary>
    public int EjectRetries { get; set; } = 3;

    public int EjectRetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// Ignore repeat submissions within this window, so keypad bounce or an impatient
    /// second press cannot start a competing upload cycle.
    /// </summary>
    public int SubmitCooldownMs { get; set; } = 3000;
}

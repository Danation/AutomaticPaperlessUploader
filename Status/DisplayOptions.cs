namespace AutomaticPaperlessUploader.Status;

public class DisplayOptions {
    /// <summary>
    /// Leave false until a display is wired up. The indicator also disables itself if the
    /// bus or the device is missing, so enabling it early is survivable, just noisy.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>I2C bus. Bus 1 is the header on every modern Pi.</summary>
    public int BusId { get; set; } = 1;

    /// <summary>0x3C for most SSD1306 modules, 0x3D if the address jumper is bridged.</summary>
    public int Address { get; set; } = 0x3C;

    public int Width { get; set; } = 128;

    public int Height { get; set; } = 32;

    /// <summary>
    /// Font family. SkiaSharp silently falls back when a family is missing, so this is a
    /// preference rather than a guarantee.
    /// </summary>
    public string FontFamily { get; set; } = "DejaVu Sans";

    /// <summary>
    /// Blank the panel after this many seconds of sitting on the same screen. OLEDs burn
    /// in, and this one would otherwise show "Ready" for weeks at a time. Zero disables.
    /// A latched failure is never blanked.
    /// </summary>
    public int BlankAfterSeconds { get; set; } = 300;

    /// <summary>
    /// How long to wait before trying the panel again after it fails.
    ///
    /// Giving up permanently would mean a single glitch on a dupont wire leaves the
    /// display dark until someone restarts the service, and that a panel attached after
    /// startup is never noticed. Retrying costs one failed I2C write per interval.
    /// </summary>
    public int RetryAfterSeconds { get; set; } = 60;
}

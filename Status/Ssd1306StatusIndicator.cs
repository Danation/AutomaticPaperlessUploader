using System.Device.I2c;
using Iot.Device.Graphics.SkiaSharpAdapter;
using Iot.Device.Ssd13xx;
using Microsoft.Extensions.Options;

namespace AutomaticPaperlessUploader.Status;

/// <summary>
/// Shows status on an SSD1306 OLED over I2C.
///
/// The display is strictly optional. If the bus is disabled, the module is unplugged, or
/// a write fails, this disables itself and the service carries on uploading. Feedback
/// hardware must never be able to take down the thing it is reporting on.
/// </summary>
public sealed class Ssd1306StatusIndicator : IStatusIndicator, IDisposable {
    private ILogger<Ssd1306StatusIndicator> Logger { get; }
    private DisplayOptions Options { get; }
    private ScreenRenderer Renderer { get; }

    private Ssd1306? Display { get; set; }
    private bool Initialized { get; set; }
    private bool Unavailable { get; set; }

    /// <summary>Serialises access: the panel is a single shared piece of hardware.</summary>
    private SemaphoreSlim Gate { get; } = new(1, 1);

    private bool Blanked { get; set; }
    private Timer? BlankTimer { get; set; }

    public Ssd1306StatusIndicator(
        ILogger<Ssd1306StatusIndicator> logger,
        IOptions<DisplayOptions> options,
        ScreenRenderer renderer) {

        Logger = logger;
        Options = options.Value;
        Renderer = renderer;
    }

    public async Task ShowAsync(StatusUpdate update, CancellationToken cancellationToken = default) {
        if (!Options.Enabled || Unavailable) {
            return;
        }

        await Gate.WaitAsync(cancellationToken);
        try {
            if (!TryInitialize()) {
                return;
            }

            Render(update);
            ScheduleBlank(update);
        }
        finally {
            Gate.Release();
        }
    }

    private bool TryInitialize() {
        if (Initialized) {
            return true;
        }

        try {
            SkiaSharpAdapter.Register();

            var settings = new I2cConnectionSettings(Options.BusId, Options.Address);
            var device = I2cDevice.Create(settings);

            Display = new Ssd1306(device, Options.Width, Options.Height);
            Display.EnableDisplay(true);

            Initialized = true;
            Logger.LogInformation(
                "Display ready on i2c-{Bus} at 0x{Address:X2} ({Width}x{Height})",
                Options.BusId,
                Options.Address,
                Options.Width,
                Options.Height);

            return true;
        }
        catch (Exception exception) {
            // Most likely causes: dtparam=i2c_arm=on is missing from config.txt, or the
            // module is not plugged in. Either way, say so once and stop trying.
            Unavailable = true;
            Logger.LogWarning(
                exception,
                "No display on i2c-{Bus} at 0x{Address:X2}. Continuing without it.",
                Options.BusId,
                Options.Address);

            return false;
        }
    }

    private void Render(StatusUpdate update) {
        var display = Display;
        if (display is null) {
            return;
        }

        try {
            if (Blanked) {
                display.EnableDisplay(true);
                Blanked = false;
            }

            using var image = display.GetBackBufferCompatibleImage();
            Renderer.Draw(image, update, NetworkInfo.DescribeAddress());
            display.DrawBitmap(image);
        }
        catch (Exception exception) {
            Unavailable = true;
            Logger.LogWarning(exception, "Display write failed. Continuing without it.");
        }
    }

    /// <summary>
    /// Blanks the panel after a quiet period to avoid burn in, but never hides a failure:
    /// that message is the whole reason the display earns its place.
    /// </summary>
    private void ScheduleBlank(StatusUpdate update) {
        BlankTimer?.Dispose();
        BlankTimer = null;

        if (Options.BlankAfterSeconds <= 0 || update.ShouldLatch) {
            return;
        }

        BlankTimer = new Timer(
            _ => Blank(),
            null,
            TimeSpan.FromSeconds(Options.BlankAfterSeconds),
            Timeout.InfiniteTimeSpan);
    }

    private void Blank() {
        if (!Gate.Wait(TimeSpan.FromSeconds(1))) {
            return;
        }

        try {
            if (Blanked || Display is null || Unavailable) {
                return;
            }

            Display.EnableDisplay(false);
            Blanked = true;
        }
        catch (Exception exception) {
            Unavailable = true;
            Logger.LogWarning(exception, "Could not blank the display.");
        }
        finally {
            Gate.Release();
        }
    }

    public void Dispose() {
        BlankTimer?.Dispose();

        try {
            Display?.EnableDisplay(false);
        }
        catch {
            // Shutting down; nothing useful to do if the panel is already gone.
        }

        Display?.Dispose();
        Gate.Dispose();
    }
}

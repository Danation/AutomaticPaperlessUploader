using System.Device.I2c;
using Iot.Device.Graphics.SkiaSharpAdapter;
using Iot.Device.Ssd13xx;
using Microsoft.Extensions.Options;

namespace AutomaticPaperlessUploader.Status;

/// <summary>
/// Shows status on an SSD1306 OLED over I2C.
///
/// The display is strictly optional. If the bus is disabled, the module is unplugged, or
/// a write fails, this backs off and carries on. Feedback hardware must never be able to
/// take down the thing it is reporting on.
/// </summary>
public sealed class Ssd1306StatusIndicator : IStatusIndicator, IDisposable {
    private ILogger<Ssd1306StatusIndicator> Logger { get; }
    private DisplayOptions Options { get; }
    private ScreenRenderer Renderer { get; }

    private Ssd1306? Display { get; set; }

    /// <summary>
    /// When the panel may next be tried. Failures set this into the future rather than
    /// disabling permanently, so a panel attached after startup, or a momentary glitch on
    /// the wiring, recovers on its own.
    /// </summary>
    private DateTimeOffset RetryAt { get; set; } = DateTimeOffset.MinValue;

    /// <summary>Keeps repeated failures from filling the journal with the same line.</summary>
    private bool FailureLogged { get; set; }

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
        if (!Options.Enabled || DateTimeOffset.UtcNow < RetryAt) {
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
        if (Display is not null) {
            return true;
        }

        try {
            SkiaSharpAdapter.Register();

            var settings = new I2cConnectionSettings(Options.BusId, Options.Address);
            var device = I2cDevice.Create(settings);

            var display = new Ssd1306(device, Options.Width, Options.Height);
            display.EnableDisplay(true);
            Display = display;

            Logger.LogInformation(
                "Display ready on i2c-{Bus} at 0x{Address:X2} ({Width}x{Height})",
                Options.BusId,
                Options.Address,
                Options.Width,
                Options.Height);

            FailureLogged = false;
            return true;
        }
        catch (Exception exception) {
            // Most likely causes: dtparam=i2c_arm=on missing from config.txt, the i2c-dev
            // module not loaded, or nothing wired up yet.
            HandleFailure(exception, "No display on i2c-{Bus} at 0x{Address:X2}.");
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
            // Drop the handle so the next attempt reopens the device from scratch.
            Discard();
            HandleFailure(exception, "Display write failed on i2c-{Bus} at 0x{Address:X2}.");
        }
    }

    private void HandleFailure(Exception exception, string message) {
        Discard();
        RetryAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, Options.RetryAfterSeconds));

        var fullMessage = message + " Retrying in {Retry}s.";

        if (!FailureLogged) {
            Logger.LogWarning(exception, fullMessage, Options.BusId, Options.Address, Options.RetryAfterSeconds);
            FailureLogged = true;
        }
        else {
            // Already reported once; keep the journal readable.
            Logger.LogDebug(fullMessage, Options.BusId, Options.Address, Options.RetryAfterSeconds);
        }
    }

    private void Discard() {
        try {
            Display?.Dispose();
        }
        catch {
            // The device is already gone; nothing useful to do.
        }

        Display = null;
        Blanked = false;
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
            if (Blanked || Display is null) {
                return;
            }

            Display.EnableDisplay(false);
            Blanked = true;
        }
        catch (Exception exception) {
            Discard();
            HandleFailure(exception, "Could not blank the display on i2c-{Bus} at 0x{Address:X2}.");
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

        Discard();
        Gate.Dispose();
    }
}

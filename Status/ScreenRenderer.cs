using System.Drawing;
using Iot.Device.Graphics;
using Iot.Device.Graphics.SkiaSharpAdapter;
using Microsoft.Extensions.Options;

namespace AutomaticPaperlessUploader.Status;

/// <summary>
/// Decides what a status screen looks like, separately from how it gets to the panel.
///
/// Keeping layout out of the driver means the design can be previewed by rendering to a
/// file on any machine, without an OLED attached.
/// </summary>
public class ScreenRenderer {
    private DisplayOptions Options { get; }

    public ScreenRenderer(IOptions<DisplayOptions> options) {
        Options = options.Value;
    }

    public void Draw(BitmapImage image, StatusUpdate update, string address) {
        image.Clear(Color.Black);
        var graphics = image.GetDrawingApi();

        // Headline: the one word that should be readable from across the room.
        graphics.DrawText(Headline(update.Status), Options.FontFamily, 16, Color.White, new Point(0, 0));

        var y = 22;
        foreach (var line in WrapDetail(update)) {
            graphics.DrawText(line, Options.FontFamily, 10, Color.White, new Point(0, y));
            y += 12;
        }

        // Bottom line is always the address, because the times this device is most
        // confusing are the times it is not on the network.
        graphics.DrawText(address, Options.FontFamily, 10, Color.White, new Point(0, Options.Height - 12));
    }

    public static string Headline(DeviceStatus status) => status switch {
        DeviceStatus.Starting => "Starting",
        DeviceStatus.Ready => "Ready",
        DeviceStatus.Working => "Working",
        DeviceStatus.Succeeded => "Done",
        DeviceStatus.NothingToUpload => "Empty",
        DeviceStatus.Failed => "FAILED",
        _ => status.ToString(),
    };

    /// <summary>
    /// Naive character wrap. The drawing API can measure text, but the panel is a known
    /// width at a known font size, so counting characters is accurate enough and cannot
    /// throw partway through a redraw.
    /// </summary>
    public static IEnumerable<string> WrapDetail(StatusUpdate update) {
        var detail = update.Detail;

        if (string.IsNullOrWhiteSpace(detail)) {
            yield return update.OccurredAt.ToString("HH:mm:ss");
            yield break;
        }

        const int charactersPerLine = 25;
        const int maximumLines = 2;

        var remaining = detail.Trim();
        var emitted = 0;

        while (remaining.Length > 0 && emitted < maximumLines) {
            if (remaining.Length <= charactersPerLine) {
                yield return remaining;
                yield break;
            }

            // Prefer breaking on a space so words stay intact.
            var breakAt = remaining.LastIndexOf(' ', Math.Min(charactersPerLine, remaining.Length - 1));
            if (breakAt <= 0) {
                breakAt = charactersPerLine;
            }

            yield return remaining[..breakAt].TrimEnd();
            remaining = remaining[breakAt..].TrimStart();
            emitted++;
        }
    }
}

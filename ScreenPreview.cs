using AutomaticPaperlessUploader.Status;
using Iot.Device.Graphics;
using Iot.Device.Graphics.SkiaSharpAdapter;
using Microsoft.Extensions.Options;

// Renders every status screen to PNG so the layout can be reviewed before the panel
// arrives. Not part of the service; run with: dotnet run --preview-screens
public static class ScreenPreview {
    public static void Run(string outputDirectory) {
        SkiaSharpAdapter.Register();

        var options = new DisplayOptions();
        var renderer = new ScreenRenderer(Options.Create(options));

        Directory.CreateDirectory(outputDirectory);

        var samples = new (string Name, StatusUpdate Update)[] {
            ("01-starting", new StatusUpdate(DeviceStatus.Starting)),
            ("02-ready", new StatusUpdate(DeviceStatus.Ready)),
            ("03-working", new StatusUpdate(DeviceStatus.Working, "Uploading 3 file(s)")),
            ("04-done", new StatusUpdate(DeviceStatus.Succeeded, "3 file(s) uploaded")),
            ("05-empty", new StatusUpdate(DeviceStatus.NothingToUpload)),
            ("06-failed", new StatusUpdate(DeviceStatus.Failed,
                "Uploaded 0 of 1. HTTP 401: {\"detail\":\"Invalid token.\"}")),
        };

        foreach (var (name, update) in samples) {
            using var image = BitmapImage.CreateBitmap(options.Width, options.Height, PixelFormat.Format32bppArgb);
            renderer.Draw(image, update, "10.10.10.25");

            var path = Path.Combine(outputDirectory, $"{name}.png");
            image.SaveToFile(path, ImageFileType.Png);
            Console.WriteLine($"wrote {path}");
        }
    }
}

using Microsoft.Extensions.Options;

namespace AutomaticPaperlessUploader.Storage;

/// <summary>
/// Controls which backing image the USB host currently sees.
///
/// The g_mass_storage gadget exposes a writable sysfs file for the LUN backing store.
/// Writing a path swaps the media in place, so the USB device is never disconnected.
/// Writing an empty string ejects the media.
/// </summary>
public class GadgetController {
    private ILogger<GadgetController> Logger { get; }
    private StorageOptions StorageOptions { get; }

    public GadgetController(ILogger<GadgetController> logger, IOptions<StorageOptions> storageOptions) {
        Logger = logger;
        StorageOptions = storageOptions.Value;
    }

    /// <summary>
    /// The image the scanner is currently writing to, according to the kernel.
    /// </summary>
    public async Task<string> GetExposedImageAsync(CancellationToken cancellationToken = default) {
        var value = await File.ReadAllTextAsync(StorageOptions.LunFilePath, cancellationToken);
        return value.Trim();
    }

    /// <summary>
    /// Swaps the exposed media to the other image and returns the image that was just
    /// released, which is now safe to mount locally.
    /// </summary>
    public async Task<string> SwapAsync(CancellationToken cancellationToken = default) {
        var exposed = await GetExposedImageAsync(cancellationToken);
        var released = exposed;
        var incoming = ChooseIncomingImage(exposed);

        Logger.LogInformation("Swapping exposed media from '{Released}' to '{Incoming}'", released, incoming);

        // Eject first so the host raises a media change event rather than silently
        // reading stale geometry from the previous image.
        await EjectAsync(cancellationToken);

        try {
            await Task.Delay(StorageOptions.MediaChangeDelayMs, cancellationToken);
            await WriteLunAsync(incoming, cancellationToken);
        }
        catch {
            // The medium is already ejected, so failing here would leave the scanner with
            // no drive at all. Put the original image back so the next attempt starts from
            // a sane state.
            Logger.LogError("Failed to insert '{Incoming}'. Restoring '{Released}'.", incoming, released);
            await TryRestoreAsync(released);
            throw;
        }

        var confirmed = await GetExposedImageAsync(cancellationToken);
        if (confirmed != incoming) {
            throw new InvalidOperationException(
                $"Failed to swap media. Expected '{incoming}' to be exposed but kernel reports '{confirmed}'.");
        }

        Logger.LogInformation("Scanner now sees '{Incoming}'. Released '{Released}' for upload.", incoming, released);
        return released;
    }

    private string ChooseIncomingImage(string exposed) {
        var images = StorageOptions.Images;
        if (images.Count < 2) {
            throw new InvalidOperationException(
                $"Two images are required to alternate but {images.Count} were configured.");
        }

        var incoming = images.FirstOrDefault(x => !string.Equals(x, exposed, StringComparison.Ordinal));
        if (incoming is null) {
            throw new InvalidOperationException($"No alternate image configured for exposed image '{exposed}'.");
        }

        foreach (var image in images) {
            if (!File.Exists(image)) {
                throw new FileNotFoundException($"Configured image does not exist: {image}", image);
            }
        }

        return incoming;
    }

    private async Task WriteLunAsync(string value, CancellationToken cancellationToken) {
        // The sysfs attribute expects a bare value with no trailing newline.
        await File.WriteAllTextAsync(StorageOptions.LunFilePath, value, cancellationToken);
    }

    private async Task TryRestoreAsync(string imagePath) {
        try {
            await WriteLunAsync(imagePath, CancellationToken.None);
        }
        catch (Exception exception) {
            Logger.LogError(exception, "Could not restore '{Image}'. The scanner may see no drive.", imagePath);
        }
    }

    /// <summary>
    /// Ejects the current medium.
    ///
    /// The scanner keeps the medium held for a moment after it finishes writing, and while
    /// it does the kernel rejects a normal eject with EBUSY. Retry politely first so the
    /// host gets a chance to release it on its own, then force the eject rather than
    /// abandoning the cycle and stranding the scan on the drive.
    /// </summary>
    private async Task EjectAsync(CancellationToken cancellationToken) {
        for (var attempt = 1; attempt <= StorageOptions.EjectRetries; attempt++) {
            try {
                await WriteLunAsync("", cancellationToken);
                return;
            }
            catch (IOException exception) {
                Logger.LogWarning(
                    "Eject attempt {Attempt} of {Total} failed because the host still holds the medium: {Message}",
                    attempt,
                    StorageOptions.EjectRetries,
                    exception.Message);

                if (attempt < StorageOptions.EjectRetries) {
                    await Task.Delay(StorageOptions.EjectRetryDelayMs, cancellationToken);
                }
            }
        }

        await ForceEjectAsync(cancellationToken);
    }

    private async Task ForceEjectAsync(CancellationToken cancellationToken) {
        var forcedEjectPath = Path.Combine(
            Path.GetDirectoryName(StorageOptions.LunFilePath) ?? string.Empty,
            "forced_eject");

        if (!File.Exists(forcedEjectPath)) {
            throw new InvalidOperationException(
                $"The medium is busy and this kernel does not expose '{forcedEjectPath}' to force the eject.");
        }

        Logger.LogWarning("Forcing eject via '{Path}'", forcedEjectPath);
        await File.WriteAllTextAsync(forcedEjectPath, "1", cancellationToken);

        var remaining = await GetExposedImageAsync(cancellationToken);
        if (!string.IsNullOrEmpty(remaining)) {
            throw new InvalidOperationException($"Forced eject did not release the medium. Still exposing '{remaining}'.");
        }
    }
}

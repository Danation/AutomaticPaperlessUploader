using Microsoft.Extensions.Options;

namespace AutomaticPaperlessUploader.Storage;

/// <summary>
/// Controls which backing image the USB host currently sees.
///
/// The g_mass_storage gadget exposes a writable sysfs file for the LUN backing store.
/// Writing a path swaps the media in place, so the USB device is never disconnected.
/// Writing an empty string ejects the media.
///
/// Every write to that file can fail with EBUSY while the host is touching the medium, so
/// they all go through <see cref="TryWriteLunAsync"/> rather than being written directly.
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

        // The host re-probes the moment the medium disappears, and it holds the LUN busy
        // while it does. This settle time is a first guess; the retries below do the real
        // work of waiting it out.
        await Task.Delay(StorageOptions.MediaChangeDelayMs, cancellationToken);

        if (!await TryWriteLunAsync(incoming, $"insert '{incoming}'", null, cancellationToken)) {
            // The medium is already ejected, so giving up here would leave the scanner
            // with no drive at all.
            Logger.LogError("Could not insert '{Incoming}'. Restoring '{Released}'.", incoming, released);

            if (!await TryWriteLunAsync(released, $"restore '{released}'", null, CancellationToken.None)) {
                Logger.LogError("Could not restore '{Released}' either. The scanner has no drive.", released);
            }

            throw new InvalidOperationException(
                $"Failed to insert '{incoming}' after {StorageOptions.LunWriteRetries} attempts.");
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

    /// <summary>
    /// Writes the LUN backing file, retrying while the host holds it busy.
    /// Returns false rather than throwing so callers can decide what to do about it.
    /// </summary>
    private async Task<bool> TryWriteLunAsync(
        string value,
        string description,
        int? maximumAttempts,
        CancellationToken cancellationToken) {

        var attempts = Math.Max(1, maximumAttempts ?? StorageOptions.LunWriteRetries);

        for (var attempt = 1; attempt <= attempts; attempt++) {
            try {
                // The sysfs attribute expects a bare value with no trailing newline.
                await File.WriteAllTextAsync(StorageOptions.LunFilePath, value, cancellationToken);
                return true;
            }
            catch (IOException exception) {
                Logger.LogWarning(
                    "Attempt {Attempt} of {Total} to {Description} failed while the host held the medium: {Message}",
                    attempt,
                    attempts,
                    description,
                    exception.Message);

                if (attempt < attempts) {
                    await Task.Delay(StorageOptions.LunWriteRetryDelayMs, cancellationToken);
                }
            }
        }

        return false;
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
        // Write a newline rather than an empty string. An empty write never reaches the
        // kernel's store handler, so it reports success and ejects nothing; the kernel
        // strips the trailing newline itself and treats the result as "no medium".
        if (await TryWriteLunAsync("\n", "eject", StorageOptions.EjectAttemptsBeforeForcing, cancellationToken)
            && await IsEjectedAsync(cancellationToken)) {
            return;
        }

        // Expected whenever the host has the medium mounted: it locks removal with SCSI
        // PREVENT ALLOW MEDIUM REMOVAL, and the kernel then refuses a polite eject. Forcing
        // is what that control exists for.
        await ForceEjectAsync(cancellationToken);
    }

    private async Task<bool> IsEjectedAsync(CancellationToken cancellationToken) {
        var exposed = await GetExposedImageAsync(cancellationToken);

        if (string.IsNullOrEmpty(exposed)) {
            return true;
        }

        // A write that succeeds without changing anything is worse than one that fails,
        // because everything downstream then acts on a drive the host still owns.
        Logger.LogWarning("Eject reported success but '{Exposed}' is still mounted.", exposed);
        return false;
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

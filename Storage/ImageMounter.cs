using Microsoft.Extensions.Options;

namespace AutomaticPaperlessUploader.Storage;

/// <summary>
/// Loop mounts a released image so its files can be read and uploaded.
/// Only ever mount an image the USB host is not currently using, or the two writers
/// will corrupt the filesystem.
/// </summary>
public class ImageMounter {
    private ILogger<ImageMounter> Logger { get; }
    private StorageOptions StorageOptions { get; }

    public ImageMounter(ILogger<ImageMounter> logger, IOptions<StorageOptions> storageOptions) {
        Logger = logger;
        StorageOptions = storageOptions.Value;
    }

    /// <summary>
    /// Mounts the image, hands the mount point to <paramref name="action"/>, and always
    /// unmounts afterwards even if the action throws.
    /// </summary>
    public async Task UseMountedImageAsync(
        string imagePath,
        Func<string, Task> action,
        CancellationToken cancellationToken = default) {

        // Flush any writes the gadget still holds before we read the image.
        await ProcessRunner.RunAsync("sync", Array.Empty<string>(), cancellationToken);

        // A previous crash can leave the mount point occupied.
        if (await IsMountedAsync(cancellationToken)) {
            Logger.LogWarning("{MountPoint} was already mounted. Unmounting it first.", StorageOptions.MountPoint);
            await UnmountAsync(cancellationToken);
        }

        Directory.CreateDirectory(StorageOptions.MountPoint);
        await MountAsync(imagePath, cancellationToken);

        try {
            await action(StorageOptions.MountPoint);
        }
        finally {
            // Unmount even on cancellation, otherwise the image stays locked and the
            // next swap exposes a filesystem that is still mounted here.
            await ProcessRunner.RunAsync("sync", Array.Empty<string>(), CancellationToken.None);
            await UnmountAsync(CancellationToken.None);
        }
    }

    private async Task MountAsync(string imagePath, CancellationToken cancellationToken) {
        Logger.LogInformation("Mounting '{Image}' at '{MountPoint}'", imagePath, StorageOptions.MountPoint);

        var result = await ProcessRunner.RunAsync(
            "mount",
            ["-o", "loop", imagePath, StorageOptions.MountPoint],
            cancellationToken);

        if (!result.Succeeded) {
            throw new InvalidOperationException(
                $"Failed to mount '{imagePath}' at '{StorageOptions.MountPoint}': {result.CombinedOutput}");
        }
    }

    private async Task UnmountAsync(CancellationToken cancellationToken) {
        var result = await ProcessRunner.RunAsync("umount", [StorageOptions.MountPoint], cancellationToken);

        if (result.Succeeded) {
            Logger.LogInformation("Unmounted '{MountPoint}'", StorageOptions.MountPoint);
            return;
        }

        Logger.LogError("Failed to unmount '{MountPoint}': {Output}", StorageOptions.MountPoint, result.CombinedOutput);
    }

    private async Task<bool> IsMountedAsync(CancellationToken cancellationToken) {
        var result = await ProcessRunner.RunAsync("mountpoint", ["-q", StorageOptions.MountPoint], cancellationToken);
        return result.Succeeded;
    }
}

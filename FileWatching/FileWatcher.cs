using Microsoft.Extensions.Options;

namespace AutomaticPaperlessUploader.FileWatching;

public class FileWatcher {

    public event EventHandler? FilesReady;

    private FileSystemWatcher Watcher { get; }

    private FileWatcherOptions FileWatcherOptions { get; }

    public FileWatcher(ILogger<FileWatcher> logger, IOptions<FileWatcherOptions> options)
    {
        logger.LogInformation("Initializing FileWatcher");
        FileWatcherOptions = options.Value;

        Watcher = new FileSystemWatcher(FileWatcherOptions.DirectoryName)
        {
            NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
            Filter = FileWatcherOptions.FileFilter,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };

        Watcher.Created += (sender, e) =>
        {
            logger.LogInformation($"Detected file creation: {e.FullPath}");
            FilesReady?.Invoke(this, EventArgs.Empty);
        };
        Watcher.Error += (sender, e) => logger.LogError(e.GetException(), "Encountered an error while watching for file creation");

        logger.LogInformation("Finished initializing FileWatcher");
    }
}
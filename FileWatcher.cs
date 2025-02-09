namespace AutomaticPaperlessUploader;

public class FileWatcher {

    private FileSystemWatcher Watcher { get; }

    public FileWatcher(ILogger<FileWatcher> logger)
    {
        logger.LogInformation("Initializing FileWatcher");

        Watcher = new FileSystemWatcher("TODO PATH")
        {
            NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
            Filter = "*.txt",
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };

        Watcher.Created += (sender, e) =>
        {
            logger.LogInformation($"File Created: {e.FullPath}");
        };
        Watcher.Error += (sender, e) => logger.LogError(e.GetException(), "Encountered an error while watching for file creations");

        logger.LogInformation("Finished initializing FileWatcher");
    }
}
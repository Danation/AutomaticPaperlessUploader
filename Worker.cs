using AutomaticPaperlessUploader.FileWatching;
using Microsoft.Extensions.Options;

namespace AutomaticPaperlessUploader;

public class Worker : BackgroundService
{
    private ILogger<Worker> Logger { get; }
    private FileWatcher FileWatcher { get; }
    private FileWatcherOptions FileWatcherOptions { get; }

    public Worker(ILogger<Worker> logger, FileWatcher fileWatcher, IOptions<FileWatcherOptions> fileWatcherOptions)
    {
        Logger = logger;
        FileWatcher = fileWatcher;
        FileWatcherOptions = fileWatcherOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        FileWatcher.FilesReady += async (sender, args) => await ProcessFiles();

        while (!stoppingToken.IsCancellationRequested)
        {
            Logger.LogTrace("Worker running at: {time}", DateTimeOffset.Now);
            await Task.Delay(1000, stoppingToken);
        }
    }

    private async Task ProcessFiles()
    {
        var files = Directory.GetFiles(FileWatcherOptions.DirectoryName, FileWatcherOptions.FileFilter);
        foreach (var file in files)
        {
            Logger.LogInformation($"Processing file {file}");
        }
        await Task.CompletedTask;
    }
}

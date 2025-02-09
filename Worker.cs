namespace AutomaticPaperlessUploader;

public class Worker : BackgroundService
{
    private ILogger<Worker> Logger { get; }
    private FileWatcher FileWatcher { get; }

    public Worker(ILogger<Worker> logger, FileWatcher fileWatcher)
    {
        Logger = logger;
        FileWatcher = fileWatcher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }
            await Task.Delay(1000, stoppingToken);
        }
    }
}

using AutomaticPaperlessUploader.UserInput;
using Microsoft.Extensions.Options;

namespace AutomaticPaperlessUploader;

public class Worker : BackgroundService
{
    private ILogger<Worker> Logger { get; }
    private UserInputOptions UserInputOptions { get; }

    public Worker(ILogger<Worker> logger, IOptions<UserInputOptions> userInputOptions)
    {
        Logger = logger;
        UserInputOptions = userInputOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Logger.LogTrace("Worker running at: {time}", DateTimeOffset.Now);
            await Task.Delay(1000, stoppingToken);
        }
    }
}

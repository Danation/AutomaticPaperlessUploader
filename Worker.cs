using AutomaticPaperlessUploader.UserInput;
using Microsoft.Extensions.Options;

namespace AutomaticPaperlessUploader;

public class Worker : BackgroundService
{
    private ILogger<Worker> Logger { get; }
    public UserInputInterpreter UserInputInterpreter { get; }

    public Worker(ILogger<Worker> logger, UserInputInterpreter userInputInterpreter)
    {
        Logger = logger;
        UserInputInterpreter = userInputInterpreter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        UserInputInterpreter.UserSubmitted += (_s, _e) => Logger.LogInformation("User submitted. Ready to read files");
        UserInputInterpreter.ListenForUserDecision();

        while (!stoppingToken.IsCancellationRequested)
        {
            Logger.LogTrace("Worker running at: {time}", DateTimeOffset.Now);
            await Task.Delay(1000, stoppingToken);
        }
    }
}

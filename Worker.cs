using AutomaticPaperlessUploader.UserInput;

namespace AutomaticPaperlessUploader;

public class Worker : BackgroundService
{
    private ILogger<Worker> Logger { get; }
    private UserInputInterpreter UserInputInterpreter { get; }
    private UploadCycle UploadCycle { get; }

    public Worker(ILogger<Worker> logger, UserInputInterpreter userInputInterpreter, UploadCycle uploadCycle)
    {
        Logger = logger;
        UserInputInterpreter = userInputInterpreter;
        UploadCycle = uploadCycle;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        UserInputInterpreter.UserSubmitted += (_s, _e) => {
            // The keypad event is raised on the GPIO callback thread, so hand the work
            // off rather than blocking it for the length of an upload.
            _ = Task.Run(() => UploadCycle.RunAsync(stoppingToken), stoppingToken);
        };

        UserInputInterpreter.ListenForUserDecision();
        Logger.LogInformation("Ready. Press any key on the keypad to upload.");

        try {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) {
            Logger.LogInformation("Shutting down.");
        }
    }
}

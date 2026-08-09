using AutomaticPaperlessUploader.Status;
using AutomaticPaperlessUploader.UserInput;

namespace AutomaticPaperlessUploader;

public class Worker : BackgroundService
{
    private ILogger<Worker> Logger { get; }
    private UserInputInterpreter UserInputInterpreter { get; }
    private UploadCycle UploadCycle { get; }
    private StatusReporter StatusReporter { get; }

    public Worker(
        ILogger<Worker> logger,
        UserInputInterpreter userInputInterpreter,
        UploadCycle uploadCycle,
        StatusReporter statusReporter)
    {
        Logger = logger;
        UserInputInterpreter = userInputInterpreter;
        UploadCycle = uploadCycle;
        StatusReporter = statusReporter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await StatusReporter.ReportAsync(DeviceStatus.Starting, cancellationToken: stoppingToken);

        UserInputInterpreter.UserSubmitted += (_s, _e) => {
            // The keypad event is raised on the GPIO callback thread, so hand the work
            // off rather than blocking it for the length of an upload.
            //
            // Nothing here decides what the device shows afterwards: the cycle reports
            // what happened and StatusReporter decides how long it stays.
            _ = Task.Run(() => UploadCycle.RunAsync(stoppingToken), stoppingToken);
        };

        UserInputInterpreter.ListenForUserDecision();
        Logger.LogInformation("Ready. Press any key on the keypad to upload.");

        await StatusReporter.ReportAsync(DeviceStatus.Ready, cancellationToken: stoppingToken);

        try {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) {
            Logger.LogInformation("Shutting down.");
        }
    }
}

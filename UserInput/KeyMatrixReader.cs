using Iot.Device.KeyMatrix;
using Microsoft.Extensions.Options;
using System.Device.Gpio;

namespace AutomaticPaperlessUploader.UserInput;

public class KeyMatrixReader {
    private ILogger<KeyMatrixReader> Logger { get; }
    
    private UserInputOptions UserInputOptions { get; }

    public event EventHandler<KeyMatrixReaderKeyHitEventArgs>? KeyHit;

    public KeyMatrixReader(ILogger<KeyMatrixReader> logger, IOptions<UserInputOptions> userInputOptions) {
        Logger = logger;
        UserInputOptions = userInputOptions.Value;
    }

    public void Initialize() {
        InitializeConsole();
    }

    private void InitializeKeyMatrix() {
        Logger.LogInformation("Initializing KeyMatrixReader");
        var gpioOptions = UserInputOptions.KeyMatrix.GPIO;
        var keyMatrix = new KeyMatrix(gpioOptions.OutputPins, gpioOptions.InputPins, TimeSpan.FromMilliseconds(gpioOptions.ScanIntervalMs), gpioOptions.PinMode);
        keyMatrix.KeyEvent += (_, e) => {
            if (e.EventType == PinEventTypes.Falling) {
                char key = UserInputOptions.KeyMatrix.Layout[e.Output][e.Input];
                Logger.LogInformation($"Received: {key}");
                KeyHit?.Invoke(this, new KeyMatrixReaderKeyHitEventArgs { Key = key });
            }
        };
        keyMatrix.StartListeningKeyEvent();
        Logger.LogInformation("KeyMatrixReader is listening for events.");
    }

    private void InitializeConsole() {
        Task.Run(async () => {
            while (true) {
                var text = await Console.In.ReadLineAsync();
                if (text?.Length != 1) {
                    Logger.LogWarning("Invalid console input: Enter exactly one character per line.");
                    continue;
                }
                Logger.LogInformation($"Received: {text}");
                KeyHit?.Invoke(this, new KeyMatrixReaderKeyHitEventArgs { Key = text[0] });
            }
        });
    }
}

public class KeyMatrixReaderKeyHitEventArgs : EventArgs
{
    public char Key { get; set; }
}
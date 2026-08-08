using Iot.Device.KeyMatrix;
using Microsoft.Extensions.Options;
using System.Device.Gpio;

namespace AutomaticPaperlessUploader.UserInput;

public sealed class KeyMatrixReader : IDisposable {
    private ILogger<KeyMatrixReader> Logger { get; }

    private UserInputOptions UserInputOptions { get; }

    /// <summary>
    /// Held so it can be stopped and disposed on shutdown. The binding runs its scan on a
    /// non background thread, so leaving it running keeps the process alive after the host
    /// stops and systemd eventually has to SIGKILL it.
    /// </summary>
    private KeyMatrix? KeyMatrix { get; set; }

    public event EventHandler<KeyMatrixReaderKeyHitEventArgs>? KeyHit;

    public KeyMatrixReader(ILogger<KeyMatrixReader> logger, IOptions<UserInputOptions> userInputOptions) {
        Logger = logger;
        UserInputOptions = userInputOptions.Value;
    }

    public void Initialize() {
        if (UserInputOptions.UseConsoleInput) {
            InitializeConsole();
            return;
        }

        InitializeKeyMatrix();
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
        KeyMatrix = keyMatrix;
        Logger.LogInformation("KeyMatrixReader is listening for events.");
    }

    private void InitializeConsole() {
        Logger.LogInformation("Reading keys from the console. Enter one character then press return.");
        Task.Run(async () => {
            while (true) {
                var text = await Console.In.ReadLineAsync();
                if (text is null) {
                    Logger.LogInformation("Console input closed.");
                    return;
                }

                if (text.Length != 1) {
                    Logger.LogWarning("Invalid console input: Enter exactly one character per line.");
                    continue;
                }

                Logger.LogInformation($"Received: {text}");
                KeyHit?.Invoke(this, new KeyMatrixReaderKeyHitEventArgs { Key = text[0] });
            }
        });
    }

    public void Dispose() {
        if (KeyMatrix is null) {
            return;
        }

        // Order matters. Dispose only closes the pins, it does not stop the scan loop, so
        // stopping first lets the loop exit instead of finding its pins closed underneath it.
        KeyMatrix.StopListeningKeyEvent();
        KeyMatrix.Dispose();
        KeyMatrix = null;

        Logger.LogInformation("Released the keypad.");
    }
}

public class KeyMatrixReaderKeyHitEventArgs : EventArgs
{
    public char Key { get; set; }
}

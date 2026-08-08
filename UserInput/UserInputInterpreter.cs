using AutomaticPaperlessUploader.Storage;
using Microsoft.Extensions.Options;

namespace AutomaticPaperlessUploader.UserInput;

public class UserInputInterpreter {
    public ILogger<UserInputInterpreter> Logger { get; }
    public KeyMatrixReader KeyMatrixReader { get; }
    public UserInputOptions UserInputOptions { get; }
    private StorageOptions StorageOptions { get; }
    private string CurrentCommand { get; set; } = "";
    private DateTimeOffset LastSubmittedAt { get; set; } = DateTimeOffset.MinValue;

    public event EventHandler? UserSubmitted;

    public UserInputInterpreter(
        ILogger<UserInputInterpreter> logger,
        KeyMatrixReader keyMatrixReader,
        IOptions<UserInputOptions> userInputOptions,
        IOptions<StorageOptions> storageOptions) {

        Logger = logger;
        KeyMatrixReader = keyMatrixReader;
        UserInputOptions = userInputOptions.Value;
        StorageOptions = storageOptions.Value;
    }

    public void ListenForUserDecision() {
        KeyMatrixReader.KeyHit += (_, e) => {
            Logger.LogInformation($"Key Event Raised: {e.Key}");

            if (UserInputOptions.AnyKeySubmits) {
                HandleAnyKey(e.Key);
                return;
            }

            HandleCommandKey(e.Key);
        };
        KeyMatrixReader.Initialize();
    }

    private void HandleAnyKey(char key) {
        // Contact bounce and impatient repeat presses both arrive as separate key events,
        // so ignore anything that lands inside the cooldown window.
        var elapsed = DateTimeOffset.UtcNow - LastSubmittedAt;
        if (elapsed.TotalMilliseconds < StorageOptions.SubmitCooldownMs) {
            Logger.LogDebug($"Ignoring '{key}' received {elapsed.TotalMilliseconds:F0}ms after the last submission.");
            return;
        }

        LastSubmittedAt = DateTimeOffset.UtcNow;
        Logger.LogInformation($"'{key}' pressed. Starting upload.");
        UserSubmitted?.Invoke(this, new());
    }

    private void HandleCommandKey(char key) {
        CurrentCommand += key;

        var potentialActions = UserInputOptions.Actions.Where(x => x.InputCommand.StartsWith(CurrentCommand));
        if (!potentialActions.Any()) {
            Logger.LogWarning($"No action corresponds with {CurrentCommand}");
            CurrentCommand = "";
        }

        var completedActions = potentialActions.Where(x => x.InputCommand == CurrentCommand);
        if (completedActions.Any()) {
            CurrentCommand = "";
            Logger.LogInformation($"Commands found: {completedActions.Count()}");
            if (completedActions.Any(x => x.Action == ActionName.Submit)) {
                Logger.LogInformation("User submitted.");
                UserSubmitted?.Invoke(this, new());
            }
        }
    }
}

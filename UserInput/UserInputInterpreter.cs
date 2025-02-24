using Microsoft.Extensions.Options;

namespace AutomaticPaperlessUploader.UserInput;

public class UserInputInterpreter {
    public ILogger<UserInputInterpreter> Logger { get; }
    public KeyMatrixReader KeyMatrixReader { get; }
    public UserInputOptions UserInputOptions { get; }
    private string CurrentCommand { get; set; } = "";

    public event EventHandler? UserSubmitted;

    public UserInputInterpreter(ILogger<UserInputInterpreter> logger, KeyMatrixReader keyMatrixReader, IOptions<UserInputOptions> userInputOptions) {
        Logger = logger;
        KeyMatrixReader = keyMatrixReader;
        UserInputOptions = userInputOptions.Value;
    }

    public void ListenForUserDecision() {
        KeyMatrixReader.KeyHit += (_, e) => {
            Logger.LogInformation($"Key Event Raised: {e.Key}");
            CurrentCommand += e.Key;

            var potentialActions = UserInputOptions.Actions.Where(x => x.InputCommand.StartsWith(CurrentCommand));
            if (!potentialActions.Any()) {
                Logger.LogWarning($"No action corresponds with {CurrentCommand}");
                CurrentCommand = "";
            }

            var completedActions = potentialActions.Where(x => x.InputCommand == CurrentCommand);
            if (completedActions.Any()) {
                Logger.LogInformation($"Commands found: {completedActions.Count()}");
                if (completedActions.Any(x => x.Action == ActionName.Submit)) {
                    Logger.LogInformation("User submitted.");
                    UserSubmitted?.Invoke(this, new());
                }
            }
        };
        KeyMatrixReader.Initialize();
    }
}
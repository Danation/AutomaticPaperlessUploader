using System.Device.Gpio;
using System.Text.Json.Serialization;

namespace AutomaticPaperlessUploader.UserInput;

public class UserInputOptions {
    public KeyMatrixOptions KeyMatrix { get; set; } = new();

    public List<ActionOptions> Actions { get; set; } = new();
}

public class KeyMatrixOptions {
    public List<List<char>> Layout { get; set; } = new();

    public GPIOOptions GPIO { get; set; } = new();
}

public class GPIOOptions {
    public int[] OutputPins { get; set; } = [];

    public int[] InputPins { get; set; } = [];

    public int ScanIntervalMs { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<PinMode>))]
    public PinMode PinMode { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<ActionName>))]
public enum ActionName {
    Unknown,
    Submit,
}

public class ActionOptions {
    public string InputCommand { get; set; } = "";

    public ActionName Action { get; set; }

    public string[] Parameters { get; set;} = [];
}
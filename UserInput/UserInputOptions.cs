using System.Device.Gpio;
using System.Text.Json.Serialization;

namespace AutomaticPaperlessUploader.UserInput;

public class UserInputOptions {
    /// <summary>
    /// When true any key starts an upload, and the Actions list is ignored. The document
    /// metadata that the keypad used to select is now handled downstream in Paperless.
    /// </summary>
    public bool AnyKeySubmits { get; set; } = true;

    /// <summary>
    /// Read keys from stdin instead of the GPIO keypad. Useful for testing the upload
    /// pipeline over SSH without the hardware attached.
    /// </summary>
    public bool UseConsoleInput { get; set; } = false;

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

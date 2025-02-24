using System.Device.Gpio;
using System.Text.Json.Serialization;

namespace AutomaticPaperlessUploader.UserInput;

public class UserInputOptions {
    public KeyMatrix KeyMatrix { get; set; } = new();
}

public class KeyMatrix {
    public List<List<char>> Layout { get; set; } = new();

    public GPIO GPIO { get; set; } = new();
}

public class GPIO {
    public int[] OutputPins { get; set; } = [];

    public int[] InputPins { get; set; } = [];

    public int ScanIntervalMs { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<PinMode>))]
    public PinMode PinMode { get; set; }
}
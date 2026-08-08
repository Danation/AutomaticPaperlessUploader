using System.Diagnostics;

namespace AutomaticPaperlessUploader.Storage;

public record ProcessResult(int ExitCode, string StandardOutput, string StandardError) {
    public bool Succeeded => ExitCode == 0;

    public string CombinedOutput =>
        string.Join(Environment.NewLine,
            new[] { StandardOutput, StandardError }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

/// <summary>
/// Minimal wrapper over Process. Deliberately dependency free so the Pi does not need
/// to restore extra NuGet packages over wifi.
/// </summary>
public static class ProcessRunner {
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default) {

        var startInfo = new ProcessStartInfo {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments) {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(process.ExitCode, (await stdoutTask).Trim(), (await stderrTask).Trim());
    }
}

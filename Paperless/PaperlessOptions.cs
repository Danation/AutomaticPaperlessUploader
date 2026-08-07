namespace AutomaticPaperlessUploader.Paperless;

public class PaperlessOptions {
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// Paperless API token. Supplied via the PAPERLESS__TOKEN environment variable from a
    /// root only EnvironmentFile so it never reaches appsettings.json or git.
    /// </summary>
    public string Token { get; set; } = "";

    /// <summary>
    /// Only files with these extensions are uploaded. Anything else on the drive is left
    /// alone, which avoids uploading scanner housekeeping files.
    /// </summary>
    public List<string> AllowedExtensions { get; set; } =
        new() { ".pdf", ".jpg", ".jpeg", ".png", ".tif", ".tiff" };

    /// <summary>
    /// Delete a file from the image once Paperless has accepted it.
    /// </summary>
    public bool DeleteAfterUpload { get; set; } = true;

    /// <summary>
    /// Tag names applied to every uploaded document. Resolved to ids against the API at
    /// runtime rather than hardcoded, so renaming or recreating a tag in Paperless does
    /// not silently break tagging.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    public int TimeoutSeconds { get; set; } = 120;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Token);
}

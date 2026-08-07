using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AutomaticPaperlessUploader.Paperless;

public record UploadResult(string FileName, bool Succeeded, string? TaskId, string? Error);

public class PaperlessClient {
    private ILogger<PaperlessClient> Logger { get; }
    private PaperlessOptions PaperlessOptions { get; }
    private HttpClient HttpClient { get; }

    /// <summary>
    /// Tag name to id, resolved once per process. Failures are not cached so a transient
    /// network problem does not permanently disable tagging.
    /// </summary>
    private Dictionary<string, int>? ResolvedTagIds { get; set; }

    private SemaphoreSlim TagResolutionGate { get; } = new(1, 1);

    public PaperlessClient(
        ILogger<PaperlessClient> logger,
        IOptions<PaperlessOptions> paperlessOptions,
        HttpClient httpClient) {

        Logger = logger;
        PaperlessOptions = paperlessOptions.Value;
        HttpClient = httpClient;
    }

    public bool IsConfigured => PaperlessOptions.IsConfigured;

    private Uri BuildUri(string relativePath) =>
        new(new Uri(PaperlessOptions.BaseUrl.TrimEnd('/') + "/"), relativePath);

    private void Authorize(HttpRequestMessage request) {
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", PaperlessOptions.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Looks up the configured tag names and returns their ids.
    /// Throws if a tag cannot be found, because uploading an untagged document is worse
    /// than not uploading it: a failed file stays on the drive and retries next cycle,
    /// whereas an untagged document has to be hunted down in Paperless by hand.
    /// </summary>
    private async Task<Dictionary<string, int>> ResolveTagIdsAsync(CancellationToken cancellationToken) {
        if (ResolvedTagIds is not null) {
            return ResolvedTagIds;
        }

        await TagResolutionGate.WaitAsync(cancellationToken);
        try {
            if (ResolvedTagIds is not null) {
                return ResolvedTagIds;
            }

            var resolved = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var tagName in PaperlessOptions.Tags) {
                var uri = BuildUri($"api/tags/?name__iexact={Uri.EscapeDataString(tagName)}");

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                Authorize(request);

                using var response = await HttpClient.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode) {
                    throw new InvalidOperationException(
                        $"Could not look up tag '{tagName}': HTTP {(int)response.StatusCode}: {body.Trim()}");
                }

                using var document = JsonDocument.Parse(body);
                var results = document.RootElement.GetProperty("results");

                if (results.GetArrayLength() == 0) {
                    throw new InvalidOperationException(
                        $"Tag '{tagName}' does not exist in Paperless. Create it or remove it from configuration.");
                }

                var id = results[0].GetProperty("id").GetInt32();
                resolved[tagName] = id;
                Logger.LogInformation("Resolved tag '{TagName}' to id {TagId}", tagName, id);
            }

            ResolvedTagIds = resolved;
            return resolved;
        }
        finally {
            TagResolutionGate.Release();
        }
    }

    /// <summary>
    /// Posts a single document. Paperless queues the file and returns a task id, so a
    /// success here means accepted for processing rather than fully consumed.
    /// </summary>
    public async Task<UploadResult> UploadAsync(string filePath, CancellationToken cancellationToken = default) {
        var fileName = Path.GetFileName(filePath);

        if (!IsConfigured) {
            return new UploadResult(fileName, false, null, "Paperless is not configured. Set BaseUrl and PAPERLESS__TOKEN.");
        }

        try {
            var tagIds = await ResolveTagIdsAsync(cancellationToken);

            using var content = new MultipartFormDataContent();
            await using var stream = File.OpenRead(filePath);
            using var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "document", fileName);

            // Paperless expects one repeated "tags" field per tag id.
            foreach (var tagId in tagIds.Values) {
                content.Add(new StringContent(tagId.ToString()), "tags");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("api/documents/post_document/")) {
                Content = content,
            };
            Authorize(request);

            Logger.LogInformation(
                "Uploading '{FileName}' with tag(s) [{Tags}]",
                fileName,
                string.Join(", ", tagIds.Keys));

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();

            if (!response.IsSuccessStatusCode) {
                return new UploadResult(fileName, false, null, $"HTTP {(int)response.StatusCode}: {body}");
            }

            // Paperless returns the task UUID as a bare quoted string.
            var taskId = body.Trim('"');
            Logger.LogInformation("Uploaded '{FileName}'. Paperless task {TaskId}", fileName, taskId);
            return new UploadResult(fileName, true, taskId, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) {
            return new UploadResult(fileName, false, null, exception.Message);
        }
    }
}

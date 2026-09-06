using System.Drawing;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WallhavenScreensaver;

internal sealed class WallhavenClient : IDisposable
{
    private const int MaxApiRequestsPerMinute = 30;
    private static readonly object ApiRateLock = new();
    private static readonly Queue<DateTimeOffset> ApiRequests = new();

    private readonly HttpClient _http;

    public WallhavenClient()
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        var version =
            typeof(WallhavenClient)
                .Assembly
                .GetName()
                .Version?
                .ToString(3) ?? "0.1.1";

        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(
                "WallhavenScreensaver",
                version));
    }

    public async Task<WallhavenSearchResult> SearchAsync(
        AppSettings settings,
        Size target,
        int page,
        string? seed,
        bool broadQuery,
        CancellationToken cancellationToken)
    {
        await WaitApiSlotAsync(cancellationToken)
            .ConfigureAwait(false);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            WallhavenQueryBuilder.Build(
                settings,
                target,
                page,
                seed,
                broadQuery));

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        using var response =
            await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using var stream =
            await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

        var payload =
            await JsonSerializer.DeserializeAsync<WallhavenSearchResponse>(
                    stream,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

        return new WallhavenSearchResult(
            payload?.Data ?? new List<WallpaperItem>(),
            payload?.Meta?.Seed,
            Math.Max(1, payload?.Meta?.LastPage ?? 1));
    }

    public async Task<WallpaperMetadata> MetadataAsync(
        string id,
        CancellationToken cancellationToken)
    {
        await WaitApiSlotAsync(cancellationToken)
            .ConfigureAwait(false);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://wallhaven.cc/api/v1/w/{Uri.EscapeDataString(id)}");

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        using var response =
            await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using var stream =
            await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

        var payload =
            await JsonSerializer.DeserializeAsync<WallhavenMetadataResponse>(
                    stream,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

        var data = payload?.Data;

        return new WallpaperMetadata(
            data?.Category ?? "",
            data?.Tags?
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList() ?? new List<string>());
    }

    public async Task DownloadAsync(
        WallpaperItem item,
        string destination,
        CancellationToken cancellationToken)
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                item.Path);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("image/*"));

        using var response =
            await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var input =
            await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

        await using var output =
            new FileStream(
                destination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

        await input
            .CopyToAsync(output, cancellationToken)
            .ConfigureAwait(false);

        await output
            .FlushAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WaitApiSlotAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan wait;

            lock (ApiRateLock)
            {
                var now = DateTimeOffset.UtcNow;

                while (ApiRequests.Count > 0 &&
                       now - ApiRequests.Peek() >=
                       TimeSpan.FromMinutes(1))
                {
                    ApiRequests.Dequeue();
                }

                if (ApiRequests.Count <
                    MaxApiRequestsPerMinute)
                {
                    ApiRequests.Enqueue(now);
                    return;
                }

                wait =
                    TimeSpan.FromMinutes(1) -
                    (now - ApiRequests.Peek()) +
                    TimeSpan.FromMilliseconds(50);
            }

            await Task.Delay(wait, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public void Dispose() => _http.Dispose();
}

internal sealed record WallhavenSearchResult(
    IReadOnlyList<WallpaperItem> Items,
    string? Seed,
    int LastPage);

internal sealed record WallpaperMetadata(
    string Category,
    IReadOnlyList<string> Tags);

internal sealed class WallhavenSearchResponse
{
    [JsonPropertyName("data")]
    public List<WallpaperItem> Data { get; set; } = new();

    [JsonPropertyName("meta")]
    public WallhavenMeta? Meta { get; set; }
}

internal sealed class WallhavenMeta
{
    [JsonPropertyName("seed")]
    public string? Seed { get; set; }

    [JsonPropertyName("last_page")]
    public int LastPage { get; set; } = 1;
}

internal sealed class WallpaperItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("dimension_x")]
    public int Width { get; set; }

    [JsonPropertyName("dimension_y")]
    public int Height { get; set; }
}

internal sealed class WallhavenMetadataResponse
{
    [JsonPropertyName("data")]
    public WallhavenMetadataData? Data { get; set; }
}

internal sealed class WallhavenMetadataData
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<WallhavenTag> Tags { get; set; } = new();
}

internal sealed class WallhavenTag
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

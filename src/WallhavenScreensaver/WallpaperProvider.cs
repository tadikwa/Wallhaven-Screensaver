using System.Drawing;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WallhavenScreensaver;

internal sealed class WallpaperProvider : IDisposable
{
    private readonly HttpClient _http;
    private readonly AppSettings _settings;
    private readonly HistoryStore _history;
    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private readonly Queue<WallpaperItem> _pool = new();
    private Size _poolTarget;

    public WallpaperProvider(AppSettings settings)
    {
        _settings = settings;
        _history = new HistoryStore(settings.HistoryMaxIds);
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var version = typeof(WallpaperProvider).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WallhavenScreensaver", version));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<string?> GetNextImagePathAsync(Size target, CancellationToken cancellationToken)
    {
        AppPaths.EnsureCreated();
        await _fetchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var refillAttempt = 0; refillAttempt < 4; refillAttempt++)
            {
                if (_pool.Count == 0 || !SimilarTarget(_poolTarget, target))
                {
                    _pool.Clear();
                    _poolTarget = target;
                    await RefillPoolAsync(target, cancellationToken).ConfigureAwait(false);
                }

                while (_pool.Count > 0)
                {
                    var item = _pool.Dequeue();
                    if (_history.Contains(item.Id))
                        continue;

                    try
                    {
                        var path = await DownloadAsync(item, cancellationToken).ConfigureAwait(false);
                        _history.Add(item.Id);
                        CleanupCache();
                        return path;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Log.Write("WARN", $"Download failed for {item.Id}: {ex.Message}");
                    }
                }

                // Try another Wallhaven page before falling back to the cache.
                _pool.Clear();
            }

            if (_settings.UseCacheFallback)
                return GetRandomCachedImage();

            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Write("WARN", $"Wallhaven request failed: {ex.Message}");
            return _settings.UseCacheFallback ? GetRandomCachedImage() : null;
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    private async Task RefillPoolAsync(Size target, CancellationToken cancellationToken)
    {
        var query = WallhavenQueryBuilder.Build(_settings, target);
        using var response = await _http.GetAsync(query, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<WallhavenSearchResponse>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (payload?.Data is null || payload.Data.Count == 0)
            return;

        var unseen = payload.Data.Where(x => !_history.Contains(x.Id)).ToList();
        var source = unseen.Count > 0 ? unseen : payload.Data;

        foreach (var item in source.OrderBy(_ => Random.Shared.Next()))
            _pool.Enqueue(item);
    }

    private async Task<string> DownloadAsync(WallpaperItem item, CancellationToken cancellationToken)
    {
        var uri = new Uri(item.Path);
        var extension = Path.GetExtension(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 5)
            extension = ".jpg";

        var finalPath = Path.Combine(AppPaths.CacheDirectory, $"{item.Id}{extension.ToLowerInvariant()}");
        if (File.Exists(finalPath))
        {
            File.SetLastWriteTime(finalPath, DateTime.Now);
            return finalPath;
        }

        var tempPath = finalPath + ".tmp";
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var output = File.Create(tempPath))
        {
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, finalPath, true);
        return finalPath;
    }

    private void CleanupCache()
    {
        try
        {
            var files = Directory.EnumerateFiles(AppPaths.CacheDirectory)
                .Where(path => !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .ToList();

            var maxBytes = (long)_settings.CacheMaxMiB * 1024L * 1024L;
            long retainedBytes = 0;
            var retainedFiles = 0;

            foreach (var file in files)
            {
                var fitsCount = retainedFiles < _settings.CacheMaxFiles;
                var fitsBytes = retainedBytes + file.Length <= maxBytes;
                if (fitsCount && fitsBytes)
                {
                    retainedFiles++;
                    retainedBytes += file.Length;
                    continue;
                }

                try { file.Delete(); } catch { }
            }
        }
        catch { }
    }

    private static string? GetRandomCachedImage()
    {
        try
        {
            var files = Directory.EnumerateFiles(AppPaths.CacheDirectory)
                .Where(path => path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                            || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                            || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return files.Length == 0 ? null : files[Random.Shared.Next(files.Length)];
        }
        catch
        {
            return null;
        }
    }

    private static bool SimilarTarget(Size a, Size b)
    {
        if (a.Width == 0 || a.Height == 0 || b.Width == 0 || b.Height == 0)
            return false;
        return Math.Abs((double)a.Width / a.Height - (double)b.Width / b.Height) < 0.05;
    }

    public void Dispose()
    {
        _http.Dispose();
        _fetchLock.Dispose();
    }
}

internal sealed class HistoryStore
{
    private readonly int _max;
    private readonly LinkedList<string> _ids = new();
    private readonly HashSet<string> _set = new(StringComparer.OrdinalIgnoreCase);

    public HistoryStore(int max)
    {
        _max = Math.Max(50, max);
        Load();
    }

    public bool Contains(string id) => _set.Contains(id);

    public void Add(string id)
    {
        if (_set.Remove(id))
        {
            var existing = _ids.Find(id);
            if (existing is not null)
                _ids.Remove(existing);
        }

        _ids.AddLast(id);
        _set.Add(id);

        while (_ids.Count > _max)
        {
            var first = _ids.First;
            if (first is null) break;
            _set.Remove(first.Value);
            _ids.RemoveFirst();
        }

        Save();
    }

    private void Load()
    {
        try
        {
            AppPaths.EnsureCreated();
            if (!File.Exists(AppPaths.HistoryPath)) return;
            var ids = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(AppPaths.HistoryPath));
            if (ids is null) return;
            foreach (var id in ids.TakeLast(_max))
            {
                if (_set.Add(id))
                    _ids.AddLast(id);
            }
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(AppPaths.HistoryPath, JsonSerializer.Serialize(_ids.ToArray()));
        }
        catch { }
    }
}

internal sealed class WallhavenSearchResponse
{
    [JsonPropertyName("data")]
    public List<WallpaperItem> Data { get; set; } = new();
}

internal sealed class WallpaperItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
}

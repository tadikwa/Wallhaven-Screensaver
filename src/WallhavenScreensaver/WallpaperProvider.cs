using System.Drawing;

namespace WallhavenScreensaver;

internal sealed record PreparedWallpaper(
    CacheLease Lease,
    Size Target)
{
    public string Id => Lease.Id;
    public string Path => Lease.Path;
    public string PoolKey => Lease.PoolKey;
}

internal sealed class WallpaperProvider : IDisposable
{
    private const int CacheLowWatermark = 4;
    private const int MaxSearchPagesPerRefill = 4;

    private readonly AppSettings _settings;
    private readonly HistoryStore _history;
    private readonly CacheStore _cache;
    private readonly WallhavenClient _client;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Dictionary<string, PoolCursor> _cursors = new();
    private Task? _backgroundRefill;

    public WallpaperProvider(AppSettings settings)
    {
        _settings = settings;
        _history = new HistoryStore(settings.HistoryMaxIds);
        _cache = new CacheStore(
            maxFiles: settings.CacheMaxFiles,
            maxBytes: (long)settings.CacheMaxMiB * 1024L * 1024L);
        _client = new WallhavenClient();
    }

    public async Task<PreparedWallpaper?> GetNextWallpaperAsync(
        Size target,
        CancellationToken cancellationToken)
    {
        AppPaths.EnsureCreated();
        var poolKey = PoolKeyBuilder.Build(_settings, target);
        PreparedWallpaper? prepared = null;
        var scheduleRefill = false;

        await _gate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            _cache.EnforceLimits();

            var history = _history.Snapshot();
            var lease = _cache.TryLease(
                poolKey,
                history,
                allowRecent: false);

            if (lease is null)
            {
                await RefillPoolAsync(
                        poolKey,
                        target,
                        requestedTarget: 1,
                        allowRecent: false,
                        cancellationToken)
                    .ConfigureAwait(false);

                history = _history.Snapshot();
                lease = _cache.TryLease(
                    poolKey,
                    history,
                    allowRecent: false);
            }

            // Long-term history is a strong preference rather than a permanent
            // ban. Only after fresh refill is exhausted do we allow the oldest
            // historical entries. seenToday remains an absolute exclusion.
            if (lease is null)
            {
                await RefillPoolAsync(
                        poolKey,
                        target,
                        requestedTarget: 1,
                        allowRecent: true,
                        cancellationToken)
                    .ConfigureAwait(false);

                history = _history.Snapshot();
                lease = _cache.TryLease(
                    poolKey,
                    history,
                    allowRecent: true);
            }

            if (lease is null)
            {
                Log.Write(
                    "WARN",
                    $"No eligible wallpaper for pool={poolKey}; keeping current image.");
                return null;
            }

            prepared = new PreparedWallpaper(lease, target);
            scheduleRefill =
                _cache.CountPool(poolKey) <= CacheLowWatermark;
        }
        finally
        {
            _gate.Release();
        }

        if (scheduleRefill && prepared is not null)
            ScheduleRefill(prepared.PoolKey, target);

        return prepared;
    }

    public void CommitDisplayed(PreparedWallpaper wallpaper)
    {
        WallpaperDisplayCommitter.CommitSuccess(
            _history,
            _cache,
            wallpaper.Lease);

        DiagnosticsStore.Increment(
            DiagnosticCounters.Accepted);

        Log.Write(
            "INFO",
            $"candidate_accepted id={wallpaper.Id} pool={wallpaper.PoolKey}");

        ScheduleRefill(wallpaper.PoolKey, wallpaper.Target);
    }

    public void MarkDisplayFailed(PreparedWallpaper wallpaper)
    {
        WallpaperDisplayCommitter.CommitFailure(
            _cache,
            wallpaper.Lease);

        Log.Write(
            "WARN",
            $"display_failed id={wallpaper.Id} pool={wallpaper.PoolKey}; history not consumed");

        ScheduleRefill(wallpaper.PoolKey, wallpaper.Target);
    }

    private void ScheduleRefill(string poolKey, Size target)
    {
        if (_disposeCts.IsCancellationRequested)
            return;

        if (_backgroundRefill is { IsCompleted: false })
            return;

        _backgroundRefill = Task.Run(async () =>
        {
            try
            {
                await _gate.WaitAsync(_disposeCts.Token)
                    .ConfigureAwait(false);

                try
                {
                    if (_cache.CountPool(poolKey) <
                        _settings.CacheTargetFiles)
                    {
                        await RefillPoolAsync(
                                poolKey,
                                target,
                                _settings.CacheTargetFiles,
                                allowRecent: false,
                                _disposeCts.Token)
                            .ConfigureAwait(false);
                    }
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Write(
                    "WARN",
                    $"background_refill_failed pool={poolKey}: {ex.Message}");
            }
        });
    }

    private async Task RefillPoolAsync(
        string poolKey,
        Size target,
        int requestedTarget,
        bool allowRecent,
        CancellationToken cancellationToken)
    {
        requestedTarget = Math.Clamp(
            requestedTarget,
            1,
            _settings.CacheTargetFiles);

        if (_cache.CountPool(poolKey) >= requestedTarget)
            return;

        if (!_cursors.TryGetValue(poolKey, out var cursor))
        {
            cursor = new PoolCursor
            {
                Page = Random.Shared.Next(1, 5)
            };
            _cursors[poolKey] = cursor;
        }

        var attempts = 0;
        var metadataChecks = 0;

        while (_cache.CountPool(poolKey) < requestedTarget &&
               attempts < MaxSearchPagesPerRefill &&
               !cancellationToken.IsCancellationRequested)
        {
            WallhavenSearchResult result;

            try
            {
                result = await _client.SearchAsync(
                        _settings,
                        target,
                        cursor.Page,
                        cursor.Seed,
                        broadQuery: false,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Write(
                    "WARN",
                    $"search_failed pool={poolKey} page={cursor.Page}: {ex.Message}");
                break;
            }

            attempts++;

            var filteredQuery = ContentFilterPolicy.Compose(
                _settings.Query,
                _settings.ContentFilter);

            // Negative terms are only a search optimisation. If they collapse a
            // narrow listing, retry the same SFW page broadly and keep metadata
            // filtering authoritative.
            if (result.Items.Count == 0 &&
                _settings.ContentFilter != ContentFilterMode.Standard &&
                !string.Equals(
                    filteredQuery,
                    _settings.Query.Trim(),
                    StringComparison.Ordinal))
            {
                Log.Write(
                    "WARN",
                    $"filtered_empty_fallback pool={poolKey} page={cursor.Page}");

                result = await _client.SearchAsync(
                        _settings,
                        target,
                        cursor.Page,
                        cursor.Seed,
                        broadQuery: true,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (_settings.Sorting == WallhavenSorting.Random &&
                string.IsNullOrWhiteSpace(cursor.Seed) &&
                !string.IsNullOrWhiteSpace(result.Seed))
            {
                cursor.Seed = result.Seed;
            }

            var history = _history.Snapshot();
            var metadataLimitReached = false;

            var orderedItems = allowRecent
                ? result.Items.OrderBy(item =>
                    history.LastSeenUtc(item.Id) ??
                    DateTimeOffset.MinValue)
                : result.Items.OrderBy(_ => Random.Shared.Next());

            foreach (var item in orderedItems)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_cache.CountPool(poolKey) >= requestedTarget)
                    break;

                if (history.IsSeenToday(item.Id))
                {
                    Reject(
                        DiagnosticCounters.DailyRepeat,
                        item.Id,
                        poolKey);
                    continue;
                }

                if (!allowRecent && history.IsRecent(item.Id))
                {
                    Reject(
                        DiagnosticCounters.RecentHistory,
                        item.Id,
                        poolKey);
                    continue;
                }

                var extension = Path.GetExtension(
                    new Uri(item.Path).AbsolutePath);

                var reservation = _cache.TryReserveDownload(
                    item.Id,
                    poolKey,
                    extension);

                if (reservation is null)
                {
                    Reject(
                        DiagnosticCounters.PendingDuplicate,
                        item.Id,
                        poolKey);
                    continue;
                }

                try
                {
                    if (ContentFilterPolicy.RequiresMetadataInspection(
                            _settings.ContentFilter))
                    {
                        if (metadataChecks >=
                            ContentFilterPolicy.MaxMetadataChecksPerRefill)
                        {
                            metadataLimitReached = true;
                            _cache.CancelDownload(reservation);
                            break;
                        }

                        metadataChecks++;

                        WallpaperMetadata metadata;
                        try
                        {
                            metadata = await _client.MetadataAsync(
                                    item.Id,
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                            when (ex is not OperationCanceledException)
                        {
                            // Filtered modes fail closed if metadata cannot be
                            // inspected.
                            Log.Write(
                                "WARN",
                                $"metadata_failure id={item.Id}: {ex.Message}");
                            _cache.CancelDownload(reservation);
                            continue;
                        }

                        var decision = ContentFilterPolicy.Evaluate(
                            metadata.Category,
                            metadata.Tags,
                            _settings.ContentFilter);

                        if (!decision.Allowed)
                        {
                            var counter =
                                _settings.ContentFilter ==
                                ContentFilterMode.Strict
                                    ? DiagnosticCounters.StrictFilter
                                    : DiagnosticCounters.ReducedFilter;

                            DiagnosticsStore.Increment(counter);

                            Log.Write(
                                "INFO",
                                $"{counter} id={item.Id} category={decision.Category} " +
                                $"score={decision.Score} tags={string.Join(',', decision.BlockedTags)} " +
                                $"reasons={string.Join('|', decision.Reasons)}");

                            _cache.CancelDownload(reservation);
                            continue;
                        }
                    }

                    await _client.DownloadAsync(
                            item,
                            reservation.TempPath,
                            cancellationToken)
                        .ConfigureAwait(false);

                    _cache.CommitDownload(reservation);
                }
                catch (OperationCanceledException)
                {
                    _cache.CancelDownload(reservation);
                    throw;
                }
                catch (Exception ex)
                {
                    _cache.CancelDownload(reservation);
                    Log.Write(
                        "WARN",
                        $"download_failed id={item.Id}: {ex.Message}");
                }
            }

            cursor.Page++;

            var lastPage = Math.Max(1, result.LastPage);
            if (cursor.Page > lastPage || cursor.Page > 20)
            {
                cursor.Page = 1;

                if (_settings.Sorting ==
                    WallhavenSorting.Random)
                {
                    cursor.Seed = null;
                }
            }

            if (result.Items.Count == 0 ||
                metadataLimitReached)
            {
                break;
            }
        }

        _cache.EnforceLimits();
    }

    private static void Reject(
        string counter,
        string id,
        string poolKey)
    {
        DiagnosticsStore.Increment(counter);
        Log.Write(
            "INFO",
            $"{counter} id={id} pool={poolKey}");
    }

    public void Dispose()
    {
        _disposeCts.Cancel();

        try
        {
            _backgroundRefill?.Wait(TimeSpan.FromSeconds(1));
        }
        catch { }

        _client.Dispose();
        _gate.Dispose();
        _disposeCts.Dispose();
    }

    private sealed class PoolCursor
    {
        public int Page { get; set; } = 1;
        public string? Seed { get; set; }
    }
}

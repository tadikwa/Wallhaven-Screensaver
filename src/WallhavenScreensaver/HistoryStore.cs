using System.Text.Json;

namespace WallhavenScreensaver;

internal sealed record HistoryEntry(
    string Id,
    DateTimeOffset DisplayedAtUtc);

internal sealed class HistorySnapshot
{
    private readonly Dictionary<string, DateTimeOffset> _lastSeen;

    public HistorySnapshot(
        HashSet<string> seenToday,
        Dictionary<string, DateTimeOffset> lastSeen)
    {
        SeenToday = seenToday;
        _lastSeen = lastSeen;
    }

    public HashSet<string> SeenToday { get; }
    public int TotalCount => _lastSeen.Count;

    public bool IsSeenToday(string id) => SeenToday.Contains(id);
    public bool IsRecent(string id) => _lastSeen.ContainsKey(id);

    public DateTimeOffset? LastSeenUtc(string id) =>
        _lastSeen.TryGetValue(id, out var value) ? value : null;
}

internal sealed class HistoryStore
{
    private sealed class HistoryDocument
    {
        public int Version { get; set; } = 2;
        public List<HistoryEntry> Entries { get; set; } = new();
    }

    private readonly string _path;
    private readonly string _legacyPath;
    private readonly int _max;
    private readonly Func<DateTimeOffset> _nowLocal;
    private readonly string _mutexName;

    public HistoryStore(
        int max,
        string? path = null,
        Func<DateTimeOffset>? nowLocal = null,
        string? mutexName = null,
        string? legacyPath = null)
    {
        _max = Math.Max(1000, max);
        _path = path ?? AppPaths.HistoryPath;
        _legacyPath = legacyPath ?? AppPaths.LegacyHistoryPath;
        _nowLocal = nowLocal ?? (() => DateTimeOffset.Now);
        _mutexName = mutexName ?? @"Local\WallhavenSharedHistoryV2";
    }

    public HistorySnapshot Snapshot()
    {
        using var lease = AcquireMutex();
        var entries = LoadEntriesLocked(out var migrated);

        if (migrated)
            SaveEntriesLocked(entries);

        var lastSeen = entries
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.MaxBy(x => x.DisplayedAtUtc)!.DisplayedAtUtc,
                StringComparer.OrdinalIgnoreCase);

        var localToday = _nowLocal().Date;
        var seenToday = entries
            .Where(x => x.DisplayedAtUtc.ToLocalTime().Date == localToday)
            .Select(x => x.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new HistorySnapshot(seenToday, lastSeen);
    }

    public void RecordDisplayed(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        using var lease = AcquireMutex();
        var entries = LoadEntriesLocked(out _);

        entries.RemoveAll(x =>
            string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

        entries.Add(new HistoryEntry(
            id,
            _nowLocal().ToUniversalTime()));

        if (entries.Count > _max)
            entries.RemoveRange(0, entries.Count - _max);

        SaveEntriesLocked(entries);
    }

    public void Clear()
    {
        using var lease = AcquireMutex();
        SaveEntriesLocked(new List<HistoryEntry>());
    }

    private List<HistoryEntry> LoadEntriesLocked(out bool migrated)
    {
        migrated = false;

        if (File.Exists(_path))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(_path));
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("Entries", out _))
                {
                    var parsed = JsonSerializer.Deserialize<HistoryDocument>(
                        root.GetRawText(),
                        SettingsStore.JsonOptions);

                    return (parsed?.Entries ?? new List<HistoryEntry>())
                        .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                        .OrderBy(x => x.DisplayedAtUtc)
                        .TakeLast(_max)
                        .ToList();
                }

                if (root.ValueKind == JsonValueKind.Array)
                {
                    var legacy = ParseLegacyArray(root);
                    if (legacy.Count > 0)
                    {
                        migrated = true;
                        return legacy;
                    }
                }
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(_legacyPath) &&
            File.Exists(_legacyPath))
        {
            try
            {
                using var legacyDoc =
                    JsonDocument.Parse(File.ReadAllText(_legacyPath));

                var legacy = ParseLegacyArray(legacyDoc.RootElement);
                if (legacy.Count > 0)
                {
                    migrated = true;
                    return legacy;
                }
            }
            catch { }
        }

        return new List<HistoryEntry>();
    }

    private List<HistoryEntry> ParseLegacyArray(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
            return new List<HistoryEntry>();

        // Old Windows history contained IDs only. Their original timestamps are
        // unknowable, so migration intentionally fails closed for the current day.
        var migratedAt = _nowLocal().ToUniversalTime();

        return root
            .EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .TakeLast(_max)
            .Select(x => new HistoryEntry(x!, migratedAt))
            .ToList();
    }

    private void SaveEntriesLocked(List<HistoryEntry> entries)
    {
        var document = new HistoryDocument
        {
            Version = 2,
            Entries = entries
                .OrderBy(x => x.DisplayedAtUtc)
                .TakeLast(_max)
                .ToList()
        };

        AtomicFile.WriteJson(
            _path,
            document,
            SettingsStore.JsonOptions);
    }

    private IDisposable AcquireMutex()
    {
        var mutex = new Mutex(false, _mutexName);
        try
        {
            mutex.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // The abandoned mutex is now owned by this process.
        }

        return new MutexLease(mutex);
    }

    private sealed class MutexLease : IDisposable
    {
        private readonly Mutex _mutex;
        private bool _disposed;

        public MutexLease(Mutex mutex) => _mutex = mutex;

        public void Dispose()
        {
            if (_disposed)
                return;

            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
            _disposed = true;
        }
    }
}

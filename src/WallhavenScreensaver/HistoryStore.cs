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
        public int Version { get; set; } = 3;
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

                    var entries = (parsed?.Entries ?? new List<HistoryEntry>())
                        .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                        .OrderBy(x => x.DisplayedAtUtc)
                        .TakeLast(_max)
                        .ToList();

                    // The first PR #4 test build wrote every legacy ID with the
                    // same current timestamp. That made the complete old history
                    // look like it had been displayed today and could starve the
                    // provider. Repair only these synthetic same-timestamp groups
                    // while upgrading the document from v2 to v3.
                    if ((parsed?.Version ?? 0) <= 2 &&
                        RepairBuggyV2Migration(entries))
                    {
                        migrated = true;
                    }

                    return entries;
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

        var ids = root
            .EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .TakeLast(_max)
            .Select(x => x!)
            .ToList();

        return BuildSyntheticLegacyHistory(ids);
    }

    private List<HistoryEntry> BuildSyntheticLegacyHistory(
        IReadOnlyList<string> ids)
    {
        if (ids.Count == 0)
            return new List<HistoryEntry>();

        // The old ID-only format preserves order but not display timestamps.
        // Keep the IDs in long-term recent history without inventing "today"
        // membership. Up to 20,000 synthetic entries fit comfortably within
        // yesterday afternoon/evening at one-second spacing.
        var now = _nowLocal();
        var todayStart = new DateTimeOffset(now.Date, now.Offset);
        var start = todayStart.AddDays(-1).AddHours(12);

        return ids
            .Select((id, index) =>
                new HistoryEntry(
                    id,
                    start.AddSeconds(index).ToUniversalTime()))
            .ToList();
    }

    private bool RepairBuggyV2Migration(List<HistoryEntry> entries)
    {
        if (entries.Count == 0)
            return false;

        var syntheticGroups = entries
            .GroupBy(x => x.DisplayedAtUtc)
            .Where(g => g.Count() > 1)
            .ToList();

        if (syntheticGroups.Count == 0)
            return false;

        var syntheticIds = syntheticGroups
            .SelectMany(g => g)
            .Select(x => x.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var repaired = BuildSyntheticLegacyHistory(
            entries
                .Where(x => syntheticIds.Contains(x.Id))
                .Select(x => x.Id)
                .ToList());

        entries.RemoveAll(x => syntheticIds.Contains(x.Id));
        entries.AddRange(repaired);
        entries.Sort((a, b) =>
            a.DisplayedAtUtc.CompareTo(b.DisplayedAtUtc));

        Log.Write(
            "INFO",
            $"history_migration_v2_repaired ids={syntheticIds.Count}");

        return true;
    }

    private void SaveEntriesLocked(List<HistoryEntry> entries)
    {
        var document = new HistoryDocument
        {
            Version = 3,
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

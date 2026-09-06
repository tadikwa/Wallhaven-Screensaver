using System.Text.Json;

namespace WallhavenScreensaver;

internal static class DiagnosticCounters
{
    public const string DailyRepeat = "candidate_rejected_daily_repeat";
    public const string RecentHistory = "candidate_rejected_recent_history";
    public const string PendingDuplicate = "candidate_rejected_pending_duplicate";
    public const string StrictFilter = "candidate_rejected_strict_filter";
    public const string ReducedFilter = "candidate_rejected_reduced_filter";
    public const string Accepted = "candidate_accepted";
}

internal static class DiagnosticsStore
{
    private const string MutexName =
        @"Local\WallhavenScreensaverDiagnosticsV1";

    public static void Increment(string key, long delta = 1)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        using var lease = AcquireMutex();
        var counters = LoadLocked();

        counters.TryGetValue(key, out var current);
        counters[key] = current + delta;

        AtomicFile.WriteJson(
            AppPaths.DiagnosticsPath,
            counters,
            SettingsStore.JsonOptions);
    }

    public static IReadOnlyDictionary<string, long> Snapshot()
    {
        using var lease = AcquireMutex();
        return new Dictionary<string, long>(
            LoadLocked(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, long> LoadLocked()
    {
        try
        {
            AppPaths.EnsureCreated();
            if (!File.Exists(AppPaths.DiagnosticsPath))
                return new(StringComparer.OrdinalIgnoreCase);

            return JsonSerializer.Deserialize<Dictionary<string, long>>(
                       File.ReadAllText(AppPaths.DiagnosticsPath),
                       SettingsStore.JsonOptions)
                   ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static IDisposable AcquireMutex()
    {
        var mutex = new Mutex(false, MutexName);
        try { mutex.WaitOne(); }
        catch (AbandonedMutexException) { }
        return new MutexLease(mutex);
    }

    private sealed class MutexLease : IDisposable
    {
        private readonly Mutex _mutex;
        private bool _disposed;

        public MutexLease(Mutex mutex) => _mutex = mutex;

        public void Dispose()
        {
            if (_disposed) return;
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
            _disposed = true;
        }
    }
}

namespace WallhavenScreensaver;

internal static class WallpaperDisplayCommitter
{
    public static void CommitSuccess(
        HistoryStore history,
        CacheStore cache,
        CacheLease lease)
    {
        // Persist history before cache cleanup: if deletion fails, the hard daily
        // exclusion is still safely committed.
        history.RecordDisplayed(lease.Id);
        cache.CommitDisplay(lease);
    }

    public static void CommitFailure(
        CacheStore cache,
        CacheLease lease)
    {
        // Failed display/decode never consumes the Wallhaven ID in history.
        cache.FailDisplay(lease);
    }
}

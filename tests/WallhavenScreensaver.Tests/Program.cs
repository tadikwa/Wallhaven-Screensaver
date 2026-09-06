using System.Drawing;
using System.Text.Json;
using WallhavenScreensaver;

var failures = new List<string>();

void Check(bool condition, string name)
{
    if (condition)
    {
        Console.WriteLine($"PASS {name}");
    }
    else
    {
        Console.Error.WriteLine($"FAIL {name}");
        failures.Add(name);
    }
}

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "WallhavenScreensaverTests-" + Guid.NewGuid().ToString("N"));

Directory.CreateDirectory(tempRoot);

try
{
    // Strict v5 metadata policy.
    Check(
        !ContentFilterPolicy.Evaluate(
            "general",
            ["big boobs", "portrait display"],
            ContentFilterMode.Strict).Allowed,
        "strict hard-blocks big boobs");

    Check(
        !ContentFilterPolicy.Evaluate(
            "general",
            ["open shirt", "portrait display"],
            ContentFilterMode.Strict).Allowed,
        "strict hard-blocks open shirt");

    Check(
        !ContentFilterPolicy.Evaluate(
            "general",
            ["spread legs", "portrait display"],
            ContentFilterMode.Strict).Allowed,
        "strict hard-blocks spread legs");

    var sparseAnime = ContentFilterPolicy.Evaluate(
        "anime",
        ["samurai", "armor", "sword"],
        ContentFilterMode.Strict);

    Check(
        !sparseAnime.Allowed &&
        sparseAnime.Reasons.Contains("strict:anime_unclassified"),
        "strict fail-closed sparse anime");

    var sparsePeople = ContentFilterPolicy.Evaluate(
        "people",
        ["portrait display", "studio", "fashion"],
        ContentFilterMode.Strict);

    Check(
        !sparsePeople.Allowed &&
        sparsePeople.Reasons.Contains("strict:people_unclassified"),
        "strict fail-closed sparse people");

    Check(
        !ContentFilterPolicy.Evaluate(
            "anime",
            ["anime", "anime girls", "flowers"],
            ContentFilterMode.Strict).Allowed,
        "strict rejects female subject");

    Check(
        ContentFilterPolicy.Evaluate(
            "anime",
            ["anime", "anime girls", "flowers"],
            ContentFilterMode.Reduced).Allowed,
        "reduced keeps ordinary female anime");

    Check(
        !ContentFilterPolicy.Evaluate(
            "people",
            ["women", "lingerie"],
            ContentFilterMode.Reduced).Allowed,
        "reduced rejects strong suggestive metadata");

    Check(
        ContentFilterPolicy.Evaluate(
            "people",
            ["women", "lingerie"],
            ContentFilterMode.Standard).Allowed,
        "standard does not apply local filtering");

    Check(
        ContentFilterPolicy.Evaluate(
            "anime",
            ["Solo Leveling", "anime boys"],
            ContentFilterMode.Strict).Allowed,
        "strict allows explicit male anime");

    Check(
        ContentFilterPolicy.Evaluate(
            "anime",
            ["mecha", "robot", "space art"],
            ContentFilterMode.Strict).Allowed,
        "strict allows non-human anime");

    var scoreDecision = ContentFilterPolicy.Evaluate(
        "general",
        ["kneeling", "bare shoulders", "parted lips"],
        ContentFilterMode.Strict);

    Check(
        !scoreDecision.Allowed &&
        scoreDecision.Score >= 4,
        "strict risk score rejects combined weak cues");

    Check(
        ContentFilterPolicy.Compose(
            "+nature -people",
            ContentFilterMode.Standard) == "+nature -people",
        "standard preserves custom query");

    Check(
        ContentFilterPolicy.Compose(
            "id:123",
            ContentFilterMode.Strict) == "id:123",
        "exact id query is preserved");

    var strictQuery = ContentFilterPolicy.Compose(
        "+nature",
        ContentFilterMode.Strict);

    Check(
        strictQuery.Contains("+nature") &&
        strictQuery.Contains("-cleavage") &&
        strictQuery.Contains("-lingerie"),
        "strict combines user query with negatives");

    var qSettings = new AppSettings
    {
        Query = "+nature",
        ContentFilter = ContentFilterMode.Strict
    };

    var query = Uri.UnescapeDataString(
        WallhavenQueryBuilder.Build(
            qSettings,
            new Size(3440, 1440)).Query);

    Check(
        query.Contains("purity=100"),
        "Wallhaven purity always SFW");

    // Timestamped daily history + persistence + local date rollover.
    var historyPath = Path.Combine(tempRoot, "history.json");
    var noLegacyPath = Path.Combine(tempRoot, "legacy-does-not-exist.json");
    var localNow = DateTimeOffset.Now;
    var now = localNow;
    var mutexName =
        @"Local\WallhavenScreensaverTests_" +
        Guid.NewGuid().ToString("N");

    var history = new HistoryStore(
        5000,
        path: historyPath,
        nowLocal: () => now,
        mutexName: mutexName,
        legacyPath: noLegacyPath);

    history.RecordDisplayed("daily1");

    Check(
        history.Snapshot().IsSeenToday("daily1"),
        "displayed ID enters seenToday");

    var reloaded = new HistoryStore(
        5000,
        path: historyPath,
        nowLocal: () => now,
        mutexName: mutexName,
        legacyPath: noLegacyPath);

    Check(
        reloaded.Snapshot().IsSeenToday("daily1"),
        "seenToday survives reload from disk");

    now = now.AddDays(1);
    var tomorrow = reloaded.Snapshot();

    Check(
        !tomorrow.IsSeenToday("daily1") &&
        tomorrow.IsRecent("daily1"),
        "date change clears daily exclusion but keeps long history");

    // Legacy ID-only history remains long-term recent without pretending it
    // was displayed today.
    var legacyPath = Path.Combine(tempRoot, "legacy.json");
    var migratedPath = Path.Combine(tempRoot, "migrated.json");
    File.WriteAllText(
        legacyPath,
        JsonSerializer.Serialize(new[] { "legacy1", "legacy2" }));

    now = localNow;
    var migrated = new HistoryStore(
        5000,
        path: migratedPath,
        nowLocal: () => now,
        mutexName: @"Local\WallhavenScreensaverMigrationTests_" +
                   Guid.NewGuid().ToString("N"),
        legacyPath: legacyPath);

    var migratedSnapshot = migrated.Snapshot();

    Check(
        !migratedSnapshot.IsSeenToday("legacy1") &&
        migratedSnapshot.IsRecent("legacy1") &&
        File.Exists(migratedPath),
        "legacy history stays recent without poisoning seenToday");

    // Repair the exact v2 starvation failure produced by the first PR build:
    // several migrated IDs sharing one identical current-day timestamp.
    var buggyV2Path = Path.Combine(tempRoot, "buggy-v2.json");
    var buggyTimestamp = localNow.ToUniversalTime().ToString("O");

    File.WriteAllText(
        buggyV2Path,
        $$"""
        {
          "Version": 2,
          "Entries": [
            { "Id": "buggy1", "DisplayedAtUtc": "{{buggyTimestamp}}" },
            { "Id": "buggy2", "DisplayedAtUtc": "{{buggyTimestamp}}" },
            { "Id": "buggy3", "DisplayedAtUtc": "{{buggyTimestamp}}" }
          ]
        }
        """);

    var repairedV2 = new HistoryStore(
        5000,
        path: buggyV2Path,
        nowLocal: () => localNow,
        mutexName: @"Local\WallhavenScreensaverRepairTests_" +
                   Guid.NewGuid().ToString("N"),
        legacyPath: noLegacyPath);

    var repairedSnapshot = repairedV2.Snapshot();

    Check(
        !repairedSnapshot.IsSeenToday("buggy1") &&
        repairedSnapshot.IsRecent("buggy1") &&
        !repairedSnapshot.IsSeenToday("buggy2"),
        "buggy v2 migration is repaired out of seenToday");
    // Cache / pending dedup across pools.
    var cacheRoot = Path.Combine(tempRoot, "cache");
    var cache = new CacheStore(
        cacheRoot,
        maxFiles: 50,
        maxBytes: 500L * 1024L * 1024L);

    var reserveA = cache.TryReserveDownload(
        "dup123",
        "aaaaaaaaaaaaaaaa",
        ".jpg");

    Check(
        reserveA is not null,
        "first pending ID reservation succeeds");

    var reserveB = cache.TryReserveDownload(
        "dup123",
        "bbbbbbbbbbbbbbbb",
        ".jpg");

    Check(
        reserveB is null,
        "same ID in another pool is rejected while pending");

    if (reserveA is not null)
        cache.CancelDownload(reserveA);

    var crossPoolReservation = cache.TryReserveDownload(
        "cachedcross",
        "aaaaaaaaaaaaaaaa",
        ".jpg");

    if (crossPoolReservation is not null)
    {
        File.WriteAllBytes(
            crossPoolReservation.TempPath,
            [1, 2, 3, 4]);
        cache.CommitDownload(crossPoolReservation);
    }

    Check(
        cache.TryReserveDownload(
            "cachedcross",
            "bbbbbbbbbbbbbbbb",
            ".jpg") is null,
        "same cached ID is rejected across pools");

    // Clear cache never affects history.
    cache.ClearAll();

    Check(
        reloaded.Snapshot().IsRecent("daily1"),
        "clear cache preserves history");

    // Display failure does not consume history.
    now = localNow;

    var failReservation = cache.TryReserveDownload(
        "fail1",
        "aaaaaaaaaaaaaaaa",
        ".jpg");

    if (failReservation is not null)
    {
        File.WriteAllBytes(
            failReservation.TempPath,
            [1, 2, 3]);
        cache.CommitDownload(failReservation);
    }

    var failLease = cache.TryLease(
        "aaaaaaaaaaaaaaaa",
        history.Snapshot(),
        allowRecent: false);

    Check(
        failLease is not null &&
        failLease.Id == "fail1",
        "display-failure candidate lease");

    if (failLease is not null)
        WallpaperDisplayCommitter.CommitFailure(cache, failLease);

    Check(
        !history.Snapshot().IsRecent("fail1"),
        "display failure does not consume ID");

    // Display success commits both daily and long-term history.
    var successReservation = cache.TryReserveDownload(
        "success1",
        "aaaaaaaaaaaaaaaa",
        ".jpg");

    if (successReservation is not null)
    {
        File.WriteAllBytes(
            successReservation.TempPath,
            [1, 2, 3]);
        cache.CommitDownload(successReservation);
    }

    var successLease = cache.TryLease(
        "aaaaaaaaaaaaaaaa",
        history.Snapshot(),
        allowRecent: false);

    Check(
        successLease is not null &&
        successLease.Id == "success1",
        "display-success candidate lease");

    if (successLease is not null)
    {
        WallpaperDisplayCommitter.CommitSuccess(
            history,
            cache,
            successLease);
    }

    Check(
        history.Snapshot().IsSeenToday("success1"),
        "display success consumes ID into daily history");

    // Even recycle mode can never return a same-day ID.
    var duplicateReservation = cache.TryReserveDownload(
        "success1",
        "aaaaaaaaaaaaaaaa",
        ".jpg");

    if (duplicateReservation is not null)
    {
        File.WriteAllBytes(
            duplicateReservation.TempPath,
            [1, 2, 3]);
        cache.CommitDownload(duplicateReservation);
    }

    var duplicateLease = cache.TryLease(
        "aaaaaaaaaaaaaaaa",
        history.Snapshot(),
        allowRecent: true);

    Check(
        duplicateLease is null,
        "seenToday ID cannot be selected even in recycle mode");

    // Profile/filter changes must create a different cache pool key.
    var standardSettings = new AppSettings
    {
        ContentFilter = ContentFilterMode.Standard
    };

    var strictSettings = new AppSettings
    {
        ContentFilter = ContentFilterMode.Strict
    };

    Check(
        PoolKeyBuilder.Build(
            standardSettings,
            new Size(3440, 1440)) !=
        PoolKeyBuilder.Build(
            strictSettings,
            new Size(3440, 1440)),
        "content filter changes pool key");
}
finally
{
    try
    {
        Directory.Delete(tempRoot, recursive: true);
    }
    catch { }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine(
        $"{failures.Count} regression check(s) failed.");
    Environment.ExitCode = 1;
}
else
{
    Console.WriteLine(
        "All regression checks passed.");
}


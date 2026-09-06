using System.Text.Json;
using System.Text.Json.Serialization;

namespace WallhavenScreensaver;

internal enum WallhavenSorting
{
    Random,
    Trending,
    Popular,
    Newest
}

internal enum WallhavenCategory
{
    All,
    General,
    Anime,
    People
}

internal enum ContentFilterMode
{
    Standard,
    Reduced,
    Strict
}

internal enum ImageScaleMode
{
    Fill,
    Fit
}

internal enum MultiMonitorMode
{
    SameImage,
    DifferentImage
}

internal sealed class AppSettings
{
    public WallhavenSorting Sorting { get; set; } = WallhavenSorting.Random;
    public WallhavenCategory Category { get; set; } = WallhavenCategory.All;
    public string Query { get; set; } = "";
    public ContentFilterMode ContentFilter { get; set; } = ContentFilterMode.Reduced;
    public int IntervalMinutes { get; set; } = 1;
    public int FadeMilliseconds { get; set; } = 750;
    public ImageScaleMode ScaleMode { get; set; } = ImageScaleMode.Fill;
    public MultiMonitorMode MultiMonitorMode { get; set; } = MultiMonitorMode.SameImage;
    public bool DisplayAwareFiltering { get; set; } = true;
    public int CacheTargetFiles { get; set; } = 12;
    public int CacheMaxFiles { get; set; } = 50;
    public int CacheMaxMiB { get; set; } = 500;
    public int HistoryMaxIds { get; set; } = 5000;

    public void Normalize()
    {
        Query = (Query ?? "").Trim();
        if (Query.Length > 512)
            Query = Query[..512];

        IntervalMinutes = Math.Clamp(IntervalMinutes, 1, 120);
        FadeMilliseconds = Math.Clamp(FadeMilliseconds, 0, 3000);
        CacheTargetFiles = Math.Clamp(CacheTargetFiles, 8, 20);
        CacheMaxFiles = Math.Clamp(CacheMaxFiles, CacheTargetFiles, 200);
        CacheMaxMiB = Math.Clamp(CacheMaxMiB, 100, 5000);
        HistoryMaxIds = Math.Clamp(HistoryMaxIds, 1000, 20000);
    }
}

internal static class SettingsStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        AppPaths.EnsureCreated();
        try
        {
            if (!File.Exists(AppPaths.SettingsPath))
            {
                var fresh = new AppSettings();
                fresh.Normalize();
                return fresh;
            }

            var json = File.ReadAllText(AppPaths.SettingsPath);
            var settings =
                JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ??
                new AppSettings();

            if (!json.Contains("\"ContentFilter\"", StringComparison.OrdinalIgnoreCase))
            {
                settings.ContentFilter = ContentFilterMode.Reduced;
                settings.HistoryMaxIds = Math.Max(settings.HistoryMaxIds, 5000);
                settings.CacheTargetFiles = 12;
            }

            settings.Normalize();
            return settings;
        }
        catch
        {
            var fallback = new AppSettings();
            fallback.Normalize();
            return fallback;
        }
    }

    public static void Save(AppSettings settings)
    {
        AppPaths.EnsureCreated();
        settings.Normalize();
        AtomicFile.WriteJson(AppPaths.SettingsPath, settings, JsonOptions);
    }
}

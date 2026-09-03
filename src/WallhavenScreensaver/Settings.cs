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
    public int IntervalMinutes { get; set; } = 1;
    public int FadeMilliseconds { get; set; } = 750;
    public ImageScaleMode ScaleMode { get; set; } = ImageScaleMode.Fill;
    public MultiMonitorMode MultiMonitorMode { get; set; } = MultiMonitorMode.SameImage;
    public bool DisplayAwareFiltering { get; set; } = true;
    public bool UseCacheFallback { get; set; } = true;
    public int CacheMaxFiles { get; set; } = 50;
    public int CacheMaxMiB { get; set; } = 500;
    public int HistoryMaxIds { get; set; } = 1000;

    public void Normalize()
    {
        IntervalMinutes = Math.Clamp(IntervalMinutes, 1, 120);
        FadeMilliseconds = Math.Clamp(FadeMilliseconds, 0, 3000);
        CacheMaxFiles = Math.Clamp(CacheMaxFiles, 5, 200);
        CacheMaxMiB = Math.Clamp(CacheMaxMiB, 100, 5000);
        HistoryMaxIds = Math.Clamp(HistoryMaxIds, 50, 5000);
    }
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
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
                return new AppSettings();

            var json = File.ReadAllText(AppPaths.SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        AppPaths.EnsureCreated();
        settings.Normalize();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(AppPaths.SettingsPath, json);
    }
}

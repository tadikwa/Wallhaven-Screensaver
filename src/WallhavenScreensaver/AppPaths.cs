namespace WallhavenScreensaver;

internal static class AppPaths
{
    public const string AppFolderName = "WallhavenScreensaver";

    public static readonly string BaseDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    public static readonly string SharedDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Wallhaven");

    public static readonly string CacheDirectory = Path.Combine(BaseDirectory, "cache");
    public static readonly string SettingsPath = Path.Combine(BaseDirectory, "settings.json");
    public static readonly string LegacyHistoryPath = Path.Combine(BaseDirectory, "history.json");
    public static readonly string HistoryPath = Path.Combine(SharedDirectory, "history-v2.json");
    public static readonly string DiagnosticsPath = Path.Combine(BaseDirectory, "diagnostics.json");
    public static readonly string LogDirectory = Path.Combine(BaseDirectory, "logs");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(SharedDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}

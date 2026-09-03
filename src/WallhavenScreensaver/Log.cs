namespace WallhavenScreensaver;

internal static class Log
{
    private static readonly object Sync = new();

    public static void Write(string level, string message)
    {
        try
        {
            AppPaths.EnsureCreated();
            lock (Sync)
            {
                Cleanup();
                var path = Path.Combine(AppPaths.LogDirectory, $"wallhaven-screensaver-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never break the screensaver.
        }
    }

    private static void Cleanup()
    {
        foreach (var file in Directory.EnumerateFiles(AppPaths.LogDirectory, "wallhaven-screensaver-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-7))
                    File.Delete(file);
            }
            catch { }
        }
    }
}

using System.Windows.Forms;
using System.Globalization;

namespace WallhavenScreensaver;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var command = ScreenSaverCommand.Parse(args);
        switch (command.Mode)
        {
            case ScreenSaverMode.Configure:
                Application.Run(new ConfigForm());
                break;

            case ScreenSaverMode.Preview:
                if (command.PreviewHandle != IntPtr.Zero)
                {
                    using var context = SaverApplicationContext.CreatePreview(command.PreviewHandle);
                    Application.Run(context);
                }
                break;

            case ScreenSaverMode.FullScreen:
            default:
                using (var context = SaverApplicationContext.CreateFullScreen())
                {
                    Application.Run(context);
                }
                break;
        }
    }
}

internal enum ScreenSaverMode
{
    FullScreen,
    Configure,
    Preview
}

internal sealed record ScreenSaverCommand(ScreenSaverMode Mode, IntPtr PreviewHandle)
{
    public static ScreenSaverCommand Parse(string[] args)
    {
        if (args.Length == 0)
            return new(ScreenSaverMode.Configure, IntPtr.Zero);

        var raw = string.Join(' ', args).Trim();
        var first = args[0].Trim().ToLowerInvariant();

        if (first is "/c" or "-c" || first.StartsWith("/c:", StringComparison.Ordinal))
            return new(ScreenSaverMode.Configure, IntPtr.Zero);

        if (first is "/p" or "-p" || first.StartsWith("/p:", StringComparison.Ordinal))
        {
            string? handleText = null;
            if (first.Contains(':'))
                handleText = first[(first.IndexOf(':') + 1)..];
            else if (args.Length > 1)
                handleText = args[1];

            if (long.TryParse(handleText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var handle))
                return new(ScreenSaverMode.Preview, new IntPtr(handle));

            return new(ScreenSaverMode.Preview, IntPtr.Zero);
        }

        if (first is "/s" or "-s" || raw.StartsWith("/s ", StringComparison.OrdinalIgnoreCase))
            return new(ScreenSaverMode.FullScreen, IntPtr.Zero);

        return new(ScreenSaverMode.Configure, IntPtr.Zero);
    }
}

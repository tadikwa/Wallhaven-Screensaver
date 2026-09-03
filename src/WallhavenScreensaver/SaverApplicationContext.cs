using System.Drawing;
using System.Windows.Forms;

namespace WallhavenScreensaver;

internal sealed class SaverApplicationContext : ApplicationContext, IDisposable
{
    private readonly AppSettings _settings;
    private readonly WallpaperProvider _provider;
    private readonly List<SaverForm> _forms;
    private readonly System.Windows.Forms.Timer _rotationTimer;
    private readonly System.Windows.Forms.Timer? _previewHostTimer;
    private readonly CancellationTokenSource _cts = new();
    private readonly bool _preview;
    private readonly IntPtr _previewParent;
    private bool _refreshing;
    private bool _exiting;

    private SaverApplicationContext(
        AppSettings settings,
        List<SaverForm> forms,
        bool preview,
        IntPtr previewParent = default)
    {
        _settings = settings;
        _forms = forms;
        _preview = preview;
        _previewParent = previewParent;
        _provider = new WallpaperProvider(settings);

        _rotationTimer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(1, settings.IntervalMinutes) * 60_000
        };
        _rotationTimer.Tick += async (_, _) => await RefreshImagesAsync();

        if (_preview && _previewParent != IntPtr.Zero)
        {
            // Preview windows are owned by the Windows screen-saver settings
            // dialog. End this process promptly when that host HWND disappears.
            _previewHostTimer = new System.Windows.Forms.Timer
            {
                Interval = 500
            };
            _previewHostTimer.Tick += (_, _) =>
            {
                if (!NativeMethods.IsWindow(_previewParent))
                    RequestExit();
            };
            _previewHostTimer.Start();
        }

        foreach (var form in _forms)
        {
            form.ExitRequested += (_, _) => RequestExit();
            form.FormClosed += (_, _) =>
            {
                if (!_exiting && _forms.All(f => f.IsDisposed || !f.Visible))
                    ExitThread();
            };
            form.Show();
        }

        if (!preview)
        {
            try { Cursor.Hide(); } catch { }
        }

        _rotationTimer.Start();
        _ = RefreshImagesAsync();
    }

    public static SaverApplicationContext CreateFullScreen()
    {
        var settings = SettingsStore.Load();
        var forms = Screen.AllScreens
            .Select(screen => new SaverForm(screen.Bounds, settings))
            .ToList();

        if (forms.Count == 0)
        {
            forms.Add(
                new SaverForm(
                    Screen.PrimaryScreen?.Bounds ??
                    new Rectangle(0, 0, 1920, 1080),
                    settings));
        }

        return new SaverApplicationContext(settings, forms, preview: false);
    }

    public static SaverApplicationContext CreatePreview(IntPtr parentHandle)
    {
        var settings = SettingsStore.Load();
        var size = new Size(320, 180);

        if (NativeMethods.GetClientRect(parentHandle, out var rect))
        {
            size = new Size(
                Math.Max(1, rect.Width),
                Math.Max(1, rect.Height));
        }

        var form = new SaverForm(
            new Rectangle(Point.Empty, size),
            settings,
            preview: true,
            previewParent: parentHandle);

        return new SaverApplicationContext(
            settings,
            new List<SaverForm> { form },
            preview: true,
            previewParent: parentHandle);
    }

    private async Task RefreshImagesAsync()
    {
        if (_refreshing || _cts.IsCancellationRequested)
            return;

        _refreshing = true;
        try
        {
            if (_settings.MultiMonitorMode == MultiMonitorMode.SameImage ||
                _forms.Count == 1)
            {
                var target = _forms
                    .Select(f => f.TargetSize)
                    .OrderByDescending(s => (long)s.Width * s.Height)
                    .FirstOrDefault();

                var path =
                    await _provider.GetNextImagePathAsync(target, _cts.Token);

                if (!string.IsNullOrWhiteSpace(path))
                {
                    foreach (var form in _forms.Where(f => !f.IsDisposed))
                    {
                        form.TransitionTo(
                            path,
                            _preview ? 0 : _settings.FadeMilliseconds);
                    }
                }
            }
            else
            {
                foreach (var form in _forms.Where(f => !f.IsDisposed))
                {
                    var path =
                        await _provider.GetNextImagePathAsync(
                            form.TargetSize,
                            _cts.Token);

                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        form.TransitionTo(
                            path,
                            _preview ? 0 : _settings.FadeMilliseconds);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Write("WARN", $"Refresh failed: {ex.Message}");
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RequestExit()
    {
        if (_exiting)
            return;

        _exiting = true;
        _rotationTimer.Stop();
        _previewHostTimer?.Stop();
        _cts.Cancel();

        foreach (var form in _forms.ToArray())
        {
            try { form.Close(); } catch { }
        }

        if (!_preview)
        {
            try { Cursor.Show(); } catch { }
        }

        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        if (!_exiting)
        {
            _exiting = true;
            _cts.Cancel();
            _rotationTimer.Stop();
            _previewHostTimer?.Stop();
        }

        foreach (var form in _forms.ToArray())
        {
            try
            {
                if (!form.IsDisposed)
                    form.Close();
            }
            catch { }
        }

        if (!_preview)
        {
            try { Cursor.Show(); } catch { }
        }

        base.ExitThreadCore();
    }

    public new void Dispose()
    {
        _cts.Cancel();
        _rotationTimer.Dispose();
        _previewHostTimer?.Dispose();
        _provider.Dispose();
        _cts.Dispose();

        foreach (var form in _forms)
            form.Dispose();

        base.Dispose();
    }
}
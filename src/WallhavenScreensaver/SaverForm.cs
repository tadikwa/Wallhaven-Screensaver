using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace WallhavenScreensaver;

internal sealed class SaverForm : Form
{
    private readonly AppSettings _settings;
    private readonly bool _preview;
    private readonly IntPtr _previewParent;
    private readonly System.Windows.Forms.Timer _fadeTimer;
    private Image? _currentImage;
    private Image? _nextImage;
    private DateTime _fadeStarted;
    private float _fadeAlpha;
    private int _fadeDurationMs;
    private Point _initialMousePosition;
    private bool _mouseInitialized;

    public event EventHandler? ExitRequested;

    public Size TargetSize =>
        ClientSize.Width > 0 && ClientSize.Height > 0
            ? ClientSize
            : Bounds.Size;

    public SaverForm(
        Rectangle bounds,
        AppSettings settings,
        bool preview = false,
        IntPtr previewParent = default)
    {
        _settings = settings;
        _preview = preview;
        _previewParent = previewParent;

        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Black;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        TopMost = !preview;
        KeyPreview = true;

        _fadeTimer = new System.Windows.Forms.Timer { Interval = 33 };
        _fadeTimer.Tick += (_, _) => AdvanceFade();

        KeyDown += (_, _) => RequestExit();
        MouseDown += (_, _) => RequestExit();
        MouseWheel += (_, _) => RequestExit();
        MouseMove += OnMouseMoveForExit;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (_preview && _previewParent != IntPtr.Zero)
        {
            var style = NativeMethods
                .GetWindowLongPtr(Handle, NativeMethods.GwlStyle)
                .ToInt64();

            style = (style | NativeMethods.WsChild) & ~NativeMethods.WsPopup;

            NativeMethods.SetWindowLongPtr(
                Handle,
                NativeMethods.GwlStyle,
                new IntPtr(style));

            NativeMethods.SetParent(Handle, _previewParent);

            if (NativeMethods.GetClientRect(_previewParent, out var rect))
            {
                SetBounds(
                    0,
                    0,
                    Math.Max(1, rect.Width),
                    Math.Max(1, rect.Height));
            }
        }
        else
        {
            Activate();
        }
    }

    public bool TryTransitionTo(string path, int fadeMilliseconds)
    {
        if (IsDisposed || Disposing)
            return false;

        if (InvokeRequired)
        {
            try
            {
                return (bool)Invoke(
                    new Func<bool>(
                        () => TryTransitionTo(path, fadeMilliseconds)));
            }
            catch
            {
                return false;
            }
        }

        try
        {
            var loaded = LoadDetachedImage(path);

            if (_currentImage is null || fadeMilliseconds <= 0)
            {
                _fadeTimer.Stop();
                _nextImage?.Dispose();
                _nextImage = null;
                _currentImage?.Dispose();
                _currentImage = loaded;
                _fadeAlpha = 1f;
                Invalidate();
                return true;
            }

            _nextImage?.Dispose();
            _nextImage = loaded;
            _fadeAlpha = 0f;
            _fadeDurationMs = Math.Max(1, fadeMilliseconds);
            _fadeStarted = DateTime.UtcNow;
            _fadeTimer.Start();
            return true;
        }
        catch (Exception ex)
        {
            Log.Write(
                "WARN",
                $"Unable to display '{path}': {ex.Message}");
            return false;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(Color.Black);
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        e.Graphics.CompositingQuality = CompositingQuality.HighQuality;

        if (_currentImage is not null)
            DrawImage(e.Graphics, _currentImage, 1f);

        if (_nextImage is not null)
            DrawImage(e.Graphics, _nextImage, _fadeAlpha);
    }

    private void DrawImage(Graphics graphics, Image image, float alpha)
    {
        var destination = ClientRectangle;
        if (destination.Width <= 0 || destination.Height <= 0)
            return;

        RectangleF destRect;
        RectangleF srcRect;

        var imageRatio = (float)image.Width / image.Height;
        var targetRatio = (float)destination.Width / destination.Height;

        if (_settings.ScaleMode == ImageScaleMode.Fit)
        {
            srcRect = new RectangleF(0, 0, image.Width, image.Height);

            if (imageRatio > targetRatio)
            {
                var height = destination.Width / imageRatio;
                destRect = new RectangleF(
                    0,
                    (destination.Height - height) / 2f,
                    destination.Width,
                    height);
            }
            else
            {
                var width = destination.Height * imageRatio;
                destRect = new RectangleF(
                    (destination.Width - width) / 2f,
                    0,
                    width,
                    destination.Height);
            }
        }
        else
        {
            destRect = destination;

            if (imageRatio > targetRatio)
            {
                var cropWidth = image.Height * targetRatio;
                srcRect = new RectangleF(
                    (image.Width - cropWidth) / 2f,
                    0,
                    cropWidth,
                    image.Height);
            }
            else
            {
                var cropHeight = image.Width / targetRatio;
                srcRect = new RectangleF(
                    0,
                    (image.Height - cropHeight) / 2f,
                    image.Width,
                    cropHeight);
            }
        }

        using var attributes = new ImageAttributes();
        var matrix = new ColorMatrix
        {
            Matrix33 = Math.Clamp(alpha, 0f, 1f)
        };

        attributes.SetColorMatrix(
            matrix,
            ColorMatrixFlag.Default,
            ColorAdjustType.Bitmap);

        graphics.DrawImage(
            image,
            Rectangle.Round(destRect),
            srcRect.X,
            srcRect.Y,
            srcRect.Width,
            srcRect.Height,
            GraphicsUnit.Pixel,
            attributes);
    }

    private void AdvanceFade()
    {
        var elapsed = (DateTime.UtcNow - _fadeStarted).TotalMilliseconds;
        _fadeAlpha = (float)Math.Clamp(
            elapsed / _fadeDurationMs,
            0d,
            1d);

        Invalidate();

        if (_fadeAlpha >= 1f && _nextImage is not null)
        {
            _fadeTimer.Stop();
            _currentImage?.Dispose();
            _currentImage = _nextImage;
            _nextImage = null;
            _fadeAlpha = 1f;
            Invalidate();
        }
    }

    private static Image LoadDetachedImage(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        using var image = Image.FromStream(
            stream,
            useEmbeddedColorManagement: true,
            validateImageData: true);

        return new Bitmap(image);
    }

    private void OnMouseMoveForExit(object? sender, MouseEventArgs e)
    {
        if (_preview)
            return;

        if (!_mouseInitialized)
        {
            _initialMousePosition = Cursor.Position;
            _mouseInitialized = true;
            return;
        }

        var position = Cursor.Position;
        if (Math.Abs(position.X - _initialMousePosition.X) > 8 ||
            Math.Abs(position.Y - _initialMousePosition.Y) > 8)
        {
            RequestExit();
        }
    }

    private void RequestExit()
    {
        if (!_preview)
            ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fadeTimer.Dispose();
            _currentImage?.Dispose();
            _nextImage?.Dispose();
        }

        base.Dispose(disposing);
    }
}

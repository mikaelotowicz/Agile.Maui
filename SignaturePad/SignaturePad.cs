using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;

namespace Agile.Maui;

/// <summary>
/// Touch signature field with variable-width ink, biometric-style capture
/// including physical pressure when the hardware reports it, and image export.
/// Drawing and export are cross-platform; only touch and pressure capture uses native interop.
/// Register with <c>builder.UseAgileSignaturePad()</c>.
/// </summary>
public sealed class SignaturePad : GraphicsView
{
    // Velocity in DIP/ms that produces the minimum stroke width.
    // Calibrated for typical handwriting speeds (~0.3..1.0 DIP/ms), so ink narrows early.
    private const double VelocityForMinWidth = 1.0;

    private readonly SignaturePadDrawable _drawable;
    private readonly List<RenderStroke> _strokes = new();
    private readonly Stack<RenderStroke> _redo = new();

    private RenderStroke? _current;
    private double _sessionStartMs;
    private bool _sessionStarted;

    // Velocity state for the current stroke.
    private float _lastX, _lastY;
    private double _lastMs;
    private double _lastVelocity;

    public SignaturePad()
    {
        _drawable = new SignaturePadDrawable(this);
        Drawable = _drawable;
        // Touch capture is native, including pressure where available, and is injected through
        // internal OnTouchDown/Move/Up methods connected by UseAgileSignaturePad. GraphicsView
        // interaction events are therefore not used.
    }

    // ---------------------------------------------------------------- Bindables: appearance

    public static readonly BindableProperty StrokeColorProperty =
        BindableProperty.Create(nameof(StrokeColor), typeof(Color), typeof(SignaturePad), Colors.Black,
            propertyChanged: Redraw);

    public static readonly BindableProperty MinStrokeWidthProperty =
        BindableProperty.Create(nameof(MinStrokeWidth), typeof(double), typeof(SignaturePad), 1.0);

    public static readonly BindableProperty MaxStrokeWidthProperty =
        BindableProperty.Create(nameof(MaxStrokeWidth), typeof(double), typeof(SignaturePad), 3.5);

    public static readonly BindableProperty VelocityFilterWeightProperty =
        BindableProperty.Create(nameof(VelocityFilterWeight), typeof(double), typeof(SignaturePad), 0.7);

    public static readonly BindableProperty ShowSignatureLineProperty =
        BindableProperty.Create(nameof(ShowSignatureLine), typeof(bool), typeof(SignaturePad), false,
            propertyChanged: Redraw);

    public static readonly BindableProperty SignatureLineColorProperty =
        BindableProperty.Create(nameof(SignatureLineColor), typeof(Color), typeof(SignaturePad), Colors.Gray,
            propertyChanged: Redraw);

    public static readonly BindableProperty PromptTextProperty =
        BindableProperty.Create(nameof(PromptText), typeof(string), typeof(SignaturePad), null,
            propertyChanged: Redraw);

    public static readonly BindableProperty PromptTextColorProperty =
        BindableProperty.Create(nameof(PromptTextColor), typeof(Color), typeof(SignaturePad), Colors.Gray,
            propertyChanged: Redraw);

    public static readonly BindableProperty StrokeCompletedCommandProperty =
        BindableProperty.Create(nameof(StrokeCompletedCommand), typeof(ICommand), typeof(SignaturePad));

    private static readonly BindablePropertyKey IsEmptyPropertyKey =
        BindableProperty.CreateReadOnly(nameof(IsEmpty), typeof(bool), typeof(SignaturePad), true);

    public static readonly BindableProperty IsEmptyProperty = IsEmptyPropertyKey.BindableProperty;

    /// <summary>On-screen stroke color. Default is black.</summary>
    public Color StrokeColor { get => (Color)GetValue(StrokeColorProperty); set => SetValue(StrokeColorProperty, value); }

    /// <summary>Minimum stroke width in DIP, usually reached by fast movement. Default is 1.</summary>
    public double MinStrokeWidth { get => (double)GetValue(MinStrokeWidthProperty); set => SetValue(MinStrokeWidthProperty, value); }

    /// <summary>Maximum stroke width in DIP, usually reached by slow movement or high pressure. Default is 3.5.</summary>
    public double MaxStrokeWidth { get => (double)GetValue(MaxStrokeWidthProperty); set => SetValue(MaxStrokeWidthProperty, value); }

    /// <summary>Exponential velocity smoothing weight from 0 to 1. Higher values are smoother. Default is 0.7.</summary>
    public double VelocityFilterWeight { get => (double)GetValue(VelocityFilterWeightProperty); set => SetValue(VelocityFilterWeightProperty, value); }

    /// <summary>Shows the signature guide line ("X ____") while the pad is empty.</summary>
    public bool ShowSignatureLine { get => (bool)GetValue(ShowSignatureLineProperty); set => SetValue(ShowSignatureLineProperty, value); }

    /// <summary>Guide line color. Default is gray.</summary>
    public Color SignatureLineColor { get => (Color)GetValue(SignatureLineColorProperty); set => SetValue(SignatureLineColorProperty, value); }

    /// <summary>Centered prompt text shown while the pad is empty, for example "Sign here".</summary>
    public string? PromptText { get => (string?)GetValue(PromptTextProperty); set => SetValue(PromptTextProperty, value); }

    /// <summary>Prompt text color. Default is gray.</summary>
    public Color PromptTextColor { get => (Color)GetValue(PromptTextColorProperty); set => SetValue(PromptTextColorProperty, value); }

    /// <summary>Command executed when each stroke completes. Parameter: <see cref="StrokeCompletedEventArgs"/>.</summary>
    public ICommand? StrokeCompletedCommand { get => (ICommand?)GetValue(StrokeCompletedCommandProperty); set => SetValue(StrokeCompletedCommandProperty, value); }

    /// <summary>True while no completed strokes exist. Updated automatically and read-only.</summary>
    public bool IsEmpty { get => (bool)GetValue(IsEmptyProperty); private set => SetValue(IsEmptyPropertyKey, value); }

    // ---------------------------------------------------------------- Events

    /// <summary>Raised when a stroke is completed, after finger/stylus release.</summary>
    public event EventHandler<StrokeCompletedEventArgs>? StrokeCompleted;

    /// <summary>Raised after <see cref="Clear"/>.</summary>
    public event EventHandler? Cleared;

    // ---------------------------------------------------------------- Public API: actions

    /// <summary>Removes all strokes and resets biometric-style state.</summary>
    public void Clear()
    {
        _strokes.Clear();
        _redo.Clear();
        _current = null;
        _sessionStarted = false;
        IsEmpty = true;
        Invalidate();
        Cleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Undoes the last completed stroke.</summary>
    public void Undo()
    {
        if (_current != null || _strokes.Count == 0)
            return;

        _redo.Push(_strokes[^1]);
        _strokes.RemoveAt(_strokes.Count - 1);
        IsEmpty = _strokes.Count == 0;
        Invalidate();
    }

    /// <summary>Redoes the last undone stroke.</summary>
    public void Redo()
    {
        if (_current != null || _redo.Count == 0)
            return;

        _strokes.Add(_redo.Pop());
        IsEmpty = false;
        Invalidate();
    }

    // ---------------------------------------------------------------- Public API: biometric-style data

    /// <summary>Returns the full biometric-style snapshot: strokes, timing, pressure, and geometry.</summary>
    public SignatureData GetSignatureData()
    {
        var strokes = new SignatureStroke[_strokes.Count];
        for (var i = 0; i < _strokes.Count; i++)
            strokes[i] = _strokes[i].ToPublic();

        return new SignatureData(strokes, new Size(Width, Height));
    }

    /// <summary>Returns only the completed strokes.</summary>
    public IReadOnlyList<SignatureStroke> GetStrokes()
    {
        var strokes = new SignatureStroke[_strokes.Count];
        for (var i = 0; i < _strokes.Count; i++)
            strokes[i] = _strokes[i].ToPublic();
        return strokes;
    }

    /// <summary>
    /// Serializes biometric-style strokes to stable JSON for database or file storage.
    /// Includes geometry, timing, pressure, stroke colors, and canvas size in DIP.
    /// </summary>
    public string GetSignatureJson(bool indented = false)
    {
        var data = GetSignatureData();
        var dto = new SignatureJsonDocument
        {
            Version = 1,
            CanvasWidth = data.CanvasSize.Width,
            CanvasHeight = data.CanvasSize.Height,
            Strokes = data.Strokes.Select(stroke => new SignatureJsonStroke
            {
                Color = ToHex(stroke.Color),
                Points = stroke.Points.Select(point => new SignatureJsonPoint
                {
                    X = point.X,
                    Y = point.Y,
                    TimestampMs = point.TimestampMs,
                    Pressure = point.Pressure,
                    PressureSupported = point.PressureSupported
                }).ToList()
            }).ToList()
        };

        return JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = indented });
    }

    /// <summary>
    /// Restores strokes saved by <see cref="GetSignatureJson"/>.
    /// Coordinates are loaded in the same DIP coordinate space in which they were captured.
    /// </summary>
    public void LoadSignatureJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Signature JSON cannot be empty.", nameof(json));

        var dto = JsonSerializer.Deserialize<SignatureJsonDocument>(json)
            ?? throw new InvalidOperationException("Invalid signature JSON.");

        if (dto.Version != 1)
            throw new NotSupportedException($"Unsupported signature JSON version: {dto.Version}.");

        var strokes = dto.Strokes.Select(stroke =>
            new SignatureStroke(
                stroke.Points.Select(point => new SignaturePoint(
                    point.X,
                    point.Y,
                    point.TimestampMs,
                    point.Pressure,
                    point.PressureSupported)).ToArray(),
                FromHex(stroke.Color))).ToArray();

        LoadStrokes(strokes);
    }

    /// <summary>Replaces pad content with the provided strokes for replay/restore and recalculates render widths.</summary>
    public void LoadStrokes(IEnumerable<SignatureStroke> strokes)
    {
        _strokes.Clear();
        _redo.Clear();
        _current = null;
        _sessionStarted = false;

        foreach (var s in strokes)
        {
            var render = new RenderStroke(s.Color ?? StrokeColor);
            ResetVelocity();
            foreach (var p in s.Points)
            {
                render.Points.Add(p);
                render.Widths.Add(ComputeWidth(p.X, p.Y, p.TimestampMs, p.Pressure, p.PressureSupported));
                _lastX = p.X; _lastY = p.Y; _lastMs = p.TimestampMs;
            }
            if (render.Points.Count > 0)
                _strokes.Add(render);
        }

        IsEmpty = _strokes.Count == 0;
        Invalidate();
    }

    /// <summary>
    /// Exports the signature as an image. PNG supports transparent backgrounds.
    /// Returns a <see cref="Stream"/> positioned at the beginning; the caller must dispose it.
    /// </summary>
    public Task<Stream> GetImageStreamAsync(
        SignatureImageFormat format = SignatureImageFormat.Png,
        SignatureExportOptions? options = null)
    {
        options ??= new SignatureExportOptions();

        // Snapshot on the caller thread, usually the UI thread, while state is consistent.
        // Completed strokes are immutable enough for export, so a reference array is sufficient.
        var bounds = ComputeContentBounds(options, (float)Width, (float)Height);
        var snapshot = _strokes.ToArray();
        var background = options.BackgroundColor;
        var strokeOverride = options.StrokeColorOverride;
        var jpegQuality = options.JpegQuality;
        var scale = (float)Math.Max(options.Scale, 0.1);

        var isJpeg = format == SignatureImageFormat.Jpeg;

#if WINDOWS
        // On Windows, MAUI Graphics' Win2D backend can throw NotImplementedException for
        // WriteToStream/Image.Save, so use Win2D directly through CanvasRenderTarget.
        return Platforms.Windows.SignatureImageExporter.ExportAsync(
            snapshot, bounds, scale, background, strokeOverride, isJpeg, jpegQuality);
#else
        // Render and encode off the UI thread to avoid blocking touch/UI responsiveness.
        return Task.Run<Stream>(() =>
        {
            var pxW = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
            var pxH = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));

            using var context = new PlatformBitmapExportService().CreateContext(pxW, pxH, scale);
            var canvas = context.Canvas;

            if (background is { } bg)
            {
                canvas.FillColor = bg;
                canvas.FillRectangle(0, 0, bounds.Width, bounds.Height);
            }
            else if (isJpeg)
            {
                // JPEG has no transparency, so use white by default.
                canvas.FillColor = Colors.White;
                canvas.FillRectangle(0, 0, bounds.Width, bounds.Height);
            }

            // Move cropped content to the image origin.
            canvas.Translate(-bounds.Left, -bounds.Top);
            SignaturePadDrawable.DrawStrokes(canvas, snapshot, strokeOverride);

            var stream = new MemoryStream();
            if (isJpeg)
                context.Image.Save(stream, ImageFormat.Jpeg, jpegQuality);
            else
                context.WriteToStream(stream);

            stream.Position = 0;
            return stream;
        });
#endif
    }

    // ---------------------------------------------------------------- Native capture, called by interop layers

    internal bool HasActiveStroke => _current != null;

    internal IReadOnlyList<RenderStroke> AllStrokesForRender
    {
        get
        {
            if (_current == null)
                return _strokes;

            var list = new List<RenderStroke>(_strokes.Count + 1);
            list.AddRange(_strokes);
            list.Add(_current);
            return list;
        }
    }

    /// <param name="timestampMs">Event time in ms using the platform's arbitrary base; normalized internally.</param>
    internal void OnTouchDown(float xDip, float yDip, float pressure, bool pressureSupported, double timestampMs)
    {
        if (!_sessionStarted)
        {
            _sessionStartMs = timestampMs;
            _sessionStarted = true;
        }

        _redo.Clear();
        _current = new RenderStroke(StrokeColor);
        ResetVelocity();
        AddSample(xDip, yDip, pressure, pressureSupported, timestampMs);
        Invalidate();
    }

    internal void OnTouchMove(float xDip, float yDip, float pressure, bool pressureSupported, double timestampMs)
    {
        if (_current == null)
            return;

        AddSample(xDip, yDip, pressure, pressureSupported, timestampMs);
        Invalidate();
    }

    internal void OnTouchUp(float xDip, float yDip, float pressure, bool pressureSupported, double timestampMs)
    {
        if (_current == null)
            return;

        AddSample(xDip, yDip, pressure, pressureSupported, timestampMs);

        var completed = _current;
        _strokes.Add(completed);
        _current = null;
        IsEmpty = false;
        Invalidate();

        var args = new StrokeCompletedEventArgs(completed.ToPublic(), isEmpty: false);
        StrokeCompleted?.Invoke(this, args);
        if (StrokeCompletedCommand?.CanExecute(args) == true)
            StrokeCompletedCommand.Execute(args);
    }

    /// <summary>Cancels the in-progress stroke without committing it, for example after a system gesture cancellation.</summary>
    internal void OnTouchCancel()
    {
        if (_current == null)
            return;

        _current = null;
        Invalidate();
    }

    // ---------------------------------------------------------------- Width and velocity calculation

    private void ResetVelocity()
    {
        _lastVelocity = 0;
        _lastMs = double.NaN;
    }

    private void AddSample(float x, float y, float pressure, bool pressureSupported, double timestampMs)
    {
        var tNorm = _sessionStarted ? timestampMs - _sessionStartMs : 0;
        var width = ComputeWidth(x, y, tNorm, pressure, pressureSupported);

        // When physical pressure is unavailable, store a 0..1 proxy derived from velocity-based width.
        var min = MinStrokeWidth;
        var max = Math.Max(MaxStrokeWidth, min + 0.001);
        var storedPressure = pressureSupported
            ? Math.Clamp(pressure, 0f, 1f)
            : (float)Math.Clamp((width - min) / (max - min), 0, 1);

        _current!.Points.Add(new SignaturePoint(x, y, tNorm, storedPressure, pressureSupported));
        _current.Widths.Add(width);

        _lastX = x;
        _lastY = y;
        _lastMs = tNorm;
    }

    private float ComputeWidth(float x, float y, double tNorm, float pressure, bool pressureSupported)
    {
        var min = MinStrokeWidth;
        var max = Math.Max(MaxStrokeWidth, min + 0.001);

        double velWidth;
        if (double.IsNaN(_lastMs))
        {
            // First point of the stroke: intermediate width.
            velWidth = (min + max) / 2.0;
        }
        else
        {
            var dt = Math.Max(tNorm - _lastMs, 1.0);
            var dist = Math.Sqrt((x - _lastX) * (x - _lastX) + (y - _lastY) * (y - _lastY));
            var v = dist / dt; // DIP/ms

            var w = Math.Clamp(VelocityFilterWeight, 0.0, 1.0);
            v = w * v + (1 - w) * _lastVelocity;
            _lastVelocity = v;

            var f = Math.Clamp(v / VelocityForMinWidth, 0.0, 1.0); // 0 lento .. 1 rapido
            velWidth = max - (max - min) * f;
        }

        if (pressureSupported && pressure > 0f)
        {
            var pWidth = min + (max - min) * Math.Clamp(pressure, 0f, 1f);
            return (float)((velWidth + pWidth) / 2.0);
        }

        return (float)velWidth;
    }

    private RectF ComputeContentBounds(SignatureExportOptions options, float viewWidth, float viewHeight)
    {
        var fullW = viewWidth > 0 ? viewWidth : 1;
        var fullH = viewHeight > 0 ? viewHeight : 1;

        if (!options.CropToContent || _strokes.Count == 0)
            return new RectF(0, 0, fullW, fullH);

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var stroke in _strokes)
        {
            for (var i = 0; i < stroke.Points.Count; i++)
            {
                var p = stroke.Points[i];
                var half = stroke.Widths[i] / 2f;
                minX = Math.Min(minX, p.X - half);
                minY = Math.Min(minY, p.Y - half);
                maxX = Math.Max(maxX, p.X + half);
                maxY = Math.Max(maxY, p.Y + half);
            }
        }

        var pad = (float)options.Padding;
        minX -= pad; minY -= pad; maxX += pad; maxY += pad;

        // Keep inside the visible area when possible.
        minX = Math.Max(0, minX);
        minY = Math.Max(0, minY);
        if (viewWidth > 0) maxX = Math.Min(fullW, maxX);
        if (viewHeight > 0) maxY = Math.Min(fullH, maxY);

        var width = Math.Max(1f, maxX - minX);
        var height = Math.Max(1f, maxY - minY);
        return new RectF(minX, minY, width, height);
    }

    private static void Redraw(BindableObject bindable, object oldValue, object newValue) =>
        ((SignaturePad)bindable).Invalidate();

    private static string ToHex(Color color)
    {
        var a = ToByte(color.Alpha);
        var r = ToByte(color.Red);
        var g = ToByte(color.Green);
        var b = ToByte(color.Blue);
        return $"#{a:X2}{r:X2}{g:X2}{b:X2}";
    }

    private static Color FromHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Colors.Black;

        var hex = value.Trim();
        if (hex[0] == '#')
            hex = hex[1..];

        if (hex.Length == 6)
            hex = "FF" + hex;

        if (hex.Length != 8)
            throw new FormatException("Signature color must use #RRGGBB or #AARRGGBB.");

        var a = ParseByte(hex, 0);
        var r = ParseByte(hex, 2);
        var g = ParseByte(hex, 4);
        var b = ParseByte(hex, 6);
        return Color.FromRgba(r, g, b, a);
    }

    private static byte ParseByte(string hex, int start) =>
        byte.Parse(hex.Substring(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static byte ToByte(float value) =>
        (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
}

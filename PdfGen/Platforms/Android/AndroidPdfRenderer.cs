#if ANDROID
using System.Collections.Generic;
using Android.Graphics;
using Android.Graphics.Pdf;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;
using Agile.Maui.PdfGen.Text;

namespace Agile.Maui.PdfGen.Platforms.Android;

/// <summary>
/// Renderer nativo do Android baseado em <see cref="PdfDocument"/> + <see cref="Canvas"/>.
/// O Canvas usa origem no topo-esquerda com Y para baixo — igual ao motor de layout — sem flip.
/// </summary>
public sealed class AndroidPdfRenderer : IPdfRenderer
{
    PdfDocument? _doc;
    PdfDocument.Page? _page;
    System.IDisposable? _current;

    public void BeginDocument() => _doc = new PdfDocument();

    public IRenderContext BeginPage(PdfSize size)
    {
        var info = new PdfDocument.PageInfo.Builder(
            (int)MathF.Round(size.Width), (int)MathF.Round(size.Height), 1).Create();
        _page = _doc!.StartPage(info);
        var context = new AndroidRenderContext(_page!.Canvas!);
        _current = context;
        return context;
    }

    public void EndPage()
    {
        if (_page is not null)
        {
            _current?.Dispose();
            _current = null;
            _doc!.FinishPage(_page);
            _page = null;
        }
    }

    public byte[] EndDocument()
    {
        using var ms = new System.IO.MemoryStream();
        _doc!.WriteTo(ms);
        _doc.Close();
        _doc = null;
        return ms.ToArray();
    }
}

file sealed class AndroidRenderContext : IRenderContext, System.IDisposable
{
    readonly Canvas _canvas;
    readonly Dictionary<Rendering.PdfImage, Bitmap> _bitmaps = new();

    public AndroidRenderContext(Canvas canvas)
    {
        _canvas = canvas;
    }

    static Paint NewPaint() => new(PaintFlags.AntiAlias);

    static Color ToColor(PdfColor c) => new(c.R, c.G, c.B, c.A);

    static Typeface? ToTypeface(TextStyle style)
    {
        Typeface baseFace = style.Family switch
        {
            PdfFontFamily.Times => Typeface.Serif!,
            PdfFontFamily.Courier => Typeface.Monospace!,
            _ => Typeface.SansSerif!,
        };

        TypefaceStyle ts = (style.IsBold, style.IsItalic) switch
        {
            (true, true) => TypefaceStyle.BoldItalic,
            (true, false) => TypefaceStyle.Bold,
            (false, true) => TypefaceStyle.Italic,
            _ => TypefaceStyle.Normal,
        };

        return Typeface.Create(baseFace, ts);
    }

    public void DrawText(string text, PdfPoint baselineOrigin, TextStyle style)
    {
        using Paint paint = NewPaint();
        paint.Color = ToColor(style.Color);
        paint.TextSize = style.FontSize;
        paint.SetTypeface(ToTypeface(style));
        _canvas.DrawText(text, baselineOrigin.X, baselineOrigin.Y, paint);
    }

    public void DrawImage(Rendering.PdfImage image, PdfRect destination)
    {
        if (!_bitmaps.TryGetValue(image, out Bitmap? bmp))
        {
            bmp = BitmapFactory.DecodeByteArray(image.Data, 0, image.Data.Length);
            if (bmp is null)
                return;
            _bitmaps[image] = bmp;
        }

        var src = new Rect(0, 0, bmp.Width, bmp.Height);
        var dst = new RectF(destination.Left, destination.Top, destination.Right, destination.Bottom);
        using Paint paint = NewPaint();
        paint.FilterBitmap = true;
        _canvas.DrawBitmap(bmp, src, dst, paint);
    }

    public void DrawLine(PdfPoint from, PdfPoint to, PdfColor color, float thickness)
    {
        using Paint paint = NewPaint();
        paint.Color = ToColor(color);
        paint.StrokeWidth = thickness;
        paint.SetStyle(Paint.Style.Stroke);
        _canvas.DrawLine(from.X, from.Y, to.X, to.Y, paint);
    }

    public void DrawRectangle(PdfRect rect, PdfColor color, float thickness, float cornerRadius = 0f)
    {
        using Paint paint = NewPaint();
        paint.Color = ToColor(color);
        paint.StrokeWidth = thickness;
        paint.SetStyle(Paint.Style.Stroke);
        var r = new RectF(rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (cornerRadius > 0f)
            _canvas.DrawRoundRect(r, cornerRadius, cornerRadius, paint);
        else
            _canvas.DrawRect(r, paint);
    }

    public void FillRectangle(PdfRect rect, PdfColor color, float cornerRadius = 0f)
    {
        using Paint paint = NewPaint();
        paint.Color = ToColor(color);
        paint.SetStyle(Paint.Style.Fill);
        var r = new RectF(rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (cornerRadius > 0f)
            _canvas.DrawRoundRect(r, cornerRadius, cornerRadius, paint);
        else
            _canvas.DrawRect(r, paint);
    }

    public void SaveState() => _canvas.Save();

    public void RestoreState() => _canvas.Restore();

    public void ClipRectangle(PdfRect rect) =>
        _canvas.ClipRect(rect.Left, rect.Top, rect.Right, rect.Bottom);

    public void Dispose()
    {
        foreach (Bitmap bitmap in _bitmaps.Values)
        {
            if (!bitmap.IsRecycled)
                bitmap.Recycle();
            bitmap.Dispose();
        }
        _bitmaps.Clear();
    }
}
#endif

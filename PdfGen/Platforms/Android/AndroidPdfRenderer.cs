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
/// Fontes embutidas (<see cref="EmbeddedFont"/>) são usadas via Typeface, cacheado por processo;
/// bitmaps são cacheados por documento e liberados no EndDocument.
/// </summary>
public sealed class AndroidPdfRenderer : IPdfRenderer
{
    // Typeface por processo (chave = conteúdo da fonte), não por documento: o Android nunca
    // libera um Typeface criado por CreateFromFile, nem com GC Java — um por documento vazava
    // ~60 KB de heap nativo e mantinha mapeado o .ttf temporário já apagado.
    static readonly EmbeddedFontCache<Typeface> EmbeddedTypefaces = new();

    PdfDocument? _doc;
    PdfDocument.Page? _page;

    // Cache por documento: imagem repetida em N páginas decodifica uma vez só.
    readonly Dictionary<Rendering.PdfImage, Bitmap> _bitmaps = new();

    public void BeginDocument() => _doc = new PdfDocument();

    public IRenderContext BeginPage(PdfSize size)
    {
        var info = new PdfDocument.PageInfo.Builder(
            (int)MathF.Round(size.Width), (int)MathF.Round(size.Height), 1).Create();
        _page = _doc!.StartPage(info);
        return new AndroidRenderContext(_page!.Canvas!, this);
    }

    public void EndPage()
    {
        if (_page is not null)
        {
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
        ReleaseCaches();
        return ms.ToArray();
    }

    void ReleaseCaches()
    {
        foreach (Bitmap bitmap in _bitmaps.Values)
        {
            if (!bitmap.IsRecycled)
                bitmap.Recycle();
            bitmap.Dispose();
        }
        _bitmaps.Clear();
    }

    /// <summary>Bitmap da imagem, decodificado uma única vez por documento.</summary>
    internal Bitmap? GetBitmap(Rendering.PdfImage image)
    {
        if (_bitmaps.TryGetValue(image, out Bitmap? cached))
            return cached;

        Bitmap? bmp = BitmapFactory.DecodeByteArray(image.Data, 0, image.Data.Length);
        if (bmp is not null)
            _bitmaps[image] = bmp;
        return bmp;
    }

    /// <summary>
    /// Typeface da fonte embutida, criado uma vez por conteúdo de fonte no processo. Falha fica
    /// cacheada como null e o texto recai na fonte do sistema. O Typeface é compartilhado entre
    /// documentos: nunca descartar o peer.
    /// </summary>
    internal Typeface? GetEmbeddedTypeface(EmbeddedFont font) =>
        EmbeddedTypefaces.GetOrCreate(font, CreateTypeface);

    /// <summary>
    /// No minSdk 24 não há API de Typeface a partir de bytes em memória: grava no temp do app
    /// (cache) e usa CreateFromFile. O arquivo é mapeado na criação e o mapeamento sobrevive à
    /// exclusão, então o temp é apagado logo em seguida.
    /// </summary>
    static Typeface? CreateTypeface(EmbeddedFont font)
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"agile-pdfgen-{System.Guid.NewGuid():N}.ttf");
        try
        {
            System.IO.File.WriteAllBytes(path, font.FontData);
            return Typeface.CreateFromFile(path);
        }
        catch
        {
            return null;
        }
        finally
        {
            try { System.IO.File.Delete(path); }
            catch { /* temp no cache do app: o sistema limpa se a exclusão falhar */ }
        }
    }
}

file sealed class AndroidRenderContext : IRenderContext
{
    readonly Canvas _canvas;
    readonly AndroidPdfRenderer _renderer;

    public AndroidRenderContext(Canvas canvas, AndroidPdfRenderer renderer)
    {
        _canvas = canvas;
        _renderer = renderer;
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

        // Fonte embutida real quando disponível; senão, família do sistema.
        Typeface? embedded = style.Embedded is EmbeddedFont font ? _renderer.GetEmbeddedTypeface(font) : null;
        paint.SetTypeface(embedded ?? ToTypeface(style));

        // DrawText posiciona pelo Y da baseline — mesma convenção do Td do escritor gerenciado.
        _canvas.DrawText(text, baselineOrigin.X, baselineOrigin.Y, paint);
    }

    public void DrawImage(Rendering.PdfImage image, PdfRect destination)
    {
        Bitmap? bmp = _renderer.GetBitmap(image);
        if (bmp is null)
            return;

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
}
#endif

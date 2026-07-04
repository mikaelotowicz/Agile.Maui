#if IOS || MACCATALYST
using System.Collections.Generic;
using CoreGraphics;
using CoreText;
using Foundation;
using ImageIO;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;
using Agile.Maui.PdfGen.Text;

namespace Agile.Maui.PdfGen.Platforms.iOS;

/// <summary>
/// Renderer nativo para iOS e Mac Catalyst usando CoreGraphics (<see cref="CGContextPDF"/>) — a
/// mesma API de PDF que o UIGraphicsPDFRenderer encapsula — com texto via CoreText. Compartilhado
/// entre iOS e macOS Catalyst (mesma implementação). O contexto CG é y-para-cima; convertemos as
/// coordenadas do layout (y-para-baixo) exatamente como no escritor gerenciado.
/// </summary>
public sealed class ApplePdfRenderer : IPdfRenderer
{
    NSMutableData? _data;
    CGContextPDF? _ctx;
    System.IDisposable? _current;

    public void BeginDocument()
    {
        _data = new NSMutableData();
        var consumer = new CGDataConsumer(_data);
        _ctx = new CGContextPDF(consumer, CGRect.Empty, null);
    }

    public IRenderContext BeginPage(PdfSize size)
    {
        var box = new CGRect(0, 0, size.Width, size.Height);
        _ctx!.BeginPage(box);
        var rc = new AppleRenderContext(_ctx, size.Height);
        _current = rc;
        return rc;
    }

    public void EndPage()
    {
        _current?.Dispose();
        _current = null;
        _ctx!.EndPage();
    }

    public byte[] EndDocument()
    {
        _ctx!.Close();
        byte[] bytes = _data!.ToArray();
        _ctx.Dispose();
        _ctx = null;
        _data.Dispose();
        _data = null;
        return bytes;
    }
}

file sealed class AppleRenderContext : IRenderContext, System.IDisposable
{
    readonly CGContext _ctx;
    readonly float _pageHeight;
    readonly Dictionary<Rendering.PdfImage, CGImage> _images = new();

    public AppleRenderContext(CGContext ctx, float pageHeight)
    {
        _ctx = ctx;
        _pageHeight = pageHeight;
    }

    float FlipY(float y) => _pageHeight - y;

    static CGColor ToColor(PdfColor c) => new(c.RedF, c.GreenF, c.BlueF, c.AlphaF);

    public void DrawText(string text, PdfPoint baselineOrigin, TextStyle style)
    {
        if (string.IsNullOrEmpty(text))
            return;

        using var font = new CTFont(style.Font.BaseName, style.FontSize);
        var attrs = new CTStringAttributes { Font = font, ForegroundColor = ToColor(style.Color) };
        using var attributed = new NSAttributedString(text, attrs);
        using var line = new CTLine(attributed);

        _ctx.TextPosition = new CGPoint(baselineOrigin.X, FlipY(baselineOrigin.Y));
        line.Draw(_ctx);
    }

    public void DrawImage(Rendering.PdfImage image, PdfRect destination)
    {
        if (!_images.TryGetValue(image, out CGImage? cg))
        {
            using var data = NSData.FromArray(image.Data);
            using var src = CGImageSource.FromData(data);
            cg = src?.CreateImage(0, null!);
            if (cg is null)
                return;
            _images[image] = cg;
        }

        var rect = new CGRect(destination.Left, FlipY(destination.Bottom), destination.Width, destination.Height);
        _ctx.DrawImage(rect, cg);
    }

    public void DrawLine(PdfPoint from, PdfPoint to, PdfColor color, float thickness)
    {
        _ctx.SetStrokeColor(ToColor(color));
        _ctx.SetLineWidth(thickness);
        _ctx.MoveTo(from.X, FlipY(from.Y));
        _ctx.AddLineToPoint(to.X, FlipY(to.Y));
        _ctx.StrokePath();
    }

    public void DrawRectangle(PdfRect rect, PdfColor color, float thickness, float cornerRadius = 0f)
    {
        _ctx.SetStrokeColor(ToColor(color));
        _ctx.SetLineWidth(thickness);
        AddRectPath(rect, cornerRadius);
        _ctx.StrokePath();
    }

    public void FillRectangle(PdfRect rect, PdfColor color, float cornerRadius = 0f)
    {
        _ctx.SetFillColor(ToColor(color));
        AddRectPath(rect, cornerRadius);
        _ctx.FillPath();
    }

    public void SaveState() => _ctx.SaveState();

    public void RestoreState() => _ctx.RestoreState();

    public void ClipRectangle(PdfRect rect)
    {
        var r = new CGRect(rect.Left, FlipY(rect.Bottom), rect.Width, rect.Height);
        _ctx.ClipToRect(r);
    }

    void AddRectPath(PdfRect rect, float radius)
    {
        var cg = new CGRect(rect.Left, FlipY(rect.Bottom), rect.Width, rect.Height);
        if (radius <= 0f)
        {
            _ctx.AddRect(cg);
            return;
        }

        float r = MathF.Min(radius, MathF.Min(rect.Width, rect.Height) / 2f);
        using var path = new CGPath();
        path.AddRoundedRect(cg, r, r);
        _ctx.AddPath(path);
    }

    public void Dispose()
    {
        foreach (CGImage img in _images.Values)
            img.Dispose();
        _images.Clear();
    }
}
#endif

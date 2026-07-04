using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;
using Agile.Maui.PdfGen.Text;

namespace Agile.Maui.PdfGen.Svg;

/// <summary>
/// Exportador SVG: gera um único documento SVG com todas as páginas empilhadas verticalmente.
/// Compartilha o mesmo motor de layout e a mesma interface <see cref="IRenderContext"/> do PDF; o
/// espaço SVG já é y-para-baixo, igual ao layout, então não há inversão de coordenadas. Fontes
/// embutidas são referenciadas pelo nome (não embarcadas) — o texto ainda é selecionável.
/// </summary>
public sealed class SvgRenderer : IPdfRenderer
{
    const float PageGap = 16f;

    readonly List<(PdfSize size, string body)> _pages = new();
    readonly StringBuilder _defs = new();
    int _defCounter;

    StringBuilder? _current;
    PdfSize _currentSize;

    public void BeginDocument() { }

    public IRenderContext BeginPage(PdfSize size)
    {
        _current = new StringBuilder();
        _currentSize = size;
        return new SvgRenderContext(this, _current);
    }

    public void EndPage()
    {
        _pages.Add((_currentSize, _current!.ToString()));
        _current = null;
    }

    public byte[] EndDocument()
    {
        float width = 0f;
        float height = 0f;
        foreach (var (size, _) in _pages)
        {
            if (size.Width > width) width = size.Width;
            height += size.Height + PageGap;
        }
        if (_pages.Count > 0) height -= PageGap;

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"")
          .Append(Num(width)).Append("\" height=\"").Append(Num(height))
          .Append("\" viewBox=\"0 0 ").Append(Num(width)).Append(' ').Append(Num(height)).Append("\">\n");

        if (_defs.Length > 0)
            sb.Append("<defs>\n").Append(_defs).Append("</defs>\n");

        float offset = 0f;
        foreach (var (size, body) in _pages)
        {
            sb.Append("<g transform=\"translate(0,").Append(Num(offset)).Append(")\">\n");
            sb.Append(body);
            sb.Append("</g>\n");
            offset += size.Height + PageGap;
        }

        sb.Append("</svg>\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    internal string NextDefId() => "g" + _defCounter++;
    internal StringBuilder Defs => _defs;

    internal static string Num(float v)
    {
        if (float.IsNaN(v) || float.IsInfinity(v)) return "0";
        return v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

/// <summary>IRenderContext que emite elementos SVG absolutos (sem inversão de Y).</summary>
internal sealed class SvgRenderContext : IRenderContext
{
    readonly SvgRenderer _renderer;
    readonly StringBuilder _sb;
    readonly Stack<int> _saveFrames = new();   // grupos abertos por SaveState (para fechar no RestoreState)

    public SvgRenderContext(SvgRenderer renderer, StringBuilder sb)
    {
        _renderer = renderer;
        _sb = sb;
    }

    static string N(float v) => SvgRenderer.Num(v);

    static string Hex(PdfColor c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public void DrawText(string text, PdfPoint baselineOrigin, TextStyle style)
    {
        if (string.IsNullOrEmpty(text))
            return;

        string family = style.Embedded is not null
            ? style.Embedded.PostScriptName
            : style.Family switch
            {
                PdfFontFamily.Times => "Times New Roman, serif",
                PdfFontFamily.Courier => "monospace",
                _ => "Helvetica, Arial, sans-serif",
            };

        _sb.Append("<text x=\"").Append(N(baselineOrigin.X)).Append("\" y=\"").Append(N(baselineOrigin.Y))
           .Append("\" font-family=\"").Append(family).Append("\" font-size=\"").Append(N(style.FontSize))
           .Append("\" fill=\"").Append(Hex(style.Color)).Append('"');
        if (style.IsBold) _sb.Append(" font-weight=\"bold\"");
        if (style.IsItalic) _sb.Append(" font-style=\"italic\"");
        _sb.Append('>').Append(EscapeXml(text)).Append("</text>\n");
    }

    public void DrawImage(PdfImage image, PdfRect destination)
    {
        string mime = image.Format == ImageFormat.Jpeg ? "image/jpeg" : "image/png";
        string b64 = System.Convert.ToBase64String(image.Data);
        _sb.Append("<image x=\"").Append(N(destination.Left)).Append("\" y=\"").Append(N(destination.Top))
           .Append("\" width=\"").Append(N(destination.Width)).Append("\" height=\"").Append(N(destination.Height))
           .Append("\" preserveAspectRatio=\"none\" href=\"data:").Append(mime).Append(";base64,").Append(b64)
           .Append("\"/>\n");
    }

    public void DrawLine(PdfPoint from, PdfPoint to, PdfColor color, float thickness)
    {
        if (thickness <= 0f || color.IsTransparent)
            return;
        _sb.Append("<line x1=\"").Append(N(from.X)).Append("\" y1=\"").Append(N(from.Y))
           .Append("\" x2=\"").Append(N(to.X)).Append("\" y2=\"").Append(N(to.Y))
           .Append("\" stroke=\"").Append(Hex(color)).Append("\" stroke-width=\"").Append(N(thickness)).Append("\"/>\n");
    }

    public void DrawRectangle(PdfRect rect, PdfColor color, float thickness, float cornerRadius = 0f)
    {
        if (thickness <= 0f || color.IsTransparent)
            return;
        AppendRect(rect, cornerRadius, $"fill=\"none\" stroke=\"{Hex(color)}\" stroke-width=\"{N(thickness)}\"", color.A);
    }

    public void FillRectangle(PdfRect rect, PdfColor color, float cornerRadius = 0f)
    {
        if (color.IsTransparent)
            return;
        AppendRect(rect, cornerRadius, $"fill=\"{Hex(color)}\"", color.A);
    }

    public void FillGradient(PdfRect rect, GradientBrush brush, float cornerRadius = 0f)
    {
        string id = DefineGradient(brush, rect);
        AppendRect(rect, cornerRadius, $"fill=\"url(#{id})\"", 255);
    }

    public void StrokeGradient(PdfRect rect, GradientBrush brush, float thickness, float cornerRadius = 0f)
    {
        if (thickness <= 0f)
            return;
        string id = DefineGradient(brush, rect);
        AppendRect(rect, cornerRadius, $"fill=\"none\" stroke=\"url(#{id})\" stroke-width=\"{N(thickness)}\"", 255);
    }

    void AppendRect(PdfRect rect, float cornerRadius, string paint, byte alpha)
    {
        _sb.Append("<rect x=\"").Append(N(rect.Left)).Append("\" y=\"").Append(N(rect.Top))
           .Append("\" width=\"").Append(N(rect.Width)).Append("\" height=\"").Append(N(rect.Height)).Append('"');
        if (cornerRadius > 0f)
            _sb.Append(" rx=\"").Append(N(cornerRadius)).Append('"');
        _sb.Append(' ').Append(paint);
        if (alpha < 255)
            _sb.Append(" fill-opacity=\"").Append(N(alpha / 255f)).Append('"');
        _sb.Append("/>\n");
    }

    string DefineGradient(GradientBrush brush, PdfRect rect)
    {
        string id = _renderer.NextDefId();
        StringBuilder d = _renderer.Defs;

        if (brush.Kind == GradientKind.Radial)
        {
            float cx = rect.Left + rect.Width / 2f;
            float cy = rect.Top + rect.Height / 2f;
            float r = MathF.Sqrt(rect.Width * rect.Width + rect.Height * rect.Height) / 2f;
            d.Append("<radialGradient id=\"").Append(id).Append("\" gradientUnits=\"userSpaceOnUse\" cx=\"")
             .Append(N(cx)).Append("\" cy=\"").Append(N(cy)).Append("\" r=\"").Append(N(r)).Append("\">\n");
        }
        else
        {
            float rad = brush.AngleDegrees * MathF.PI / 180f;
            float dx = MathF.Cos(rad), dy = MathF.Sin(rad);
            float cx = rect.Left + rect.Width / 2f, cy = rect.Top + rect.Height / 2f;
            float extent = (MathF.Abs(dx) * rect.Width + MathF.Abs(dy) * rect.Height) / 2f;
            d.Append("<linearGradient id=\"").Append(id).Append("\" gradientUnits=\"userSpaceOnUse\" x1=\"")
             .Append(N(cx - dx * extent)).Append("\" y1=\"").Append(N(cy - dy * extent))
             .Append("\" x2=\"").Append(N(cx + dx * extent)).Append("\" y2=\"").Append(N(cy + dy * extent)).Append("\">\n");
        }

        foreach (GradientStop stop in brush.Stops)
        {
            d.Append("<stop offset=\"").Append(N(stop.Offset)).Append("\" stop-color=\"").Append(Hex(stop.Color)).Append('"');
            if (stop.Color.A < 255)
                d.Append(" stop-opacity=\"").Append(N(stop.Color.A / 255f)).Append('"');
            d.Append("/>\n");
        }

        d.Append(brush.Kind == GradientKind.Radial ? "</radialGradient>\n" : "</linearGradient>\n");
        return id;
    }

    public void SaveState() => _saveFrames.Push(0);

    public void RestoreState()
    {
        if (_saveFrames.Count == 0)
            return;
        int groups = _saveFrames.Pop();
        for (int i = 0; i < groups; i++)
            _sb.Append("</g>\n");
    }

    public void ClipRectangle(PdfRect rect)
    {
        string id = _renderer.NextDefId();
        _renderer.Defs.Append("<clipPath id=\"").Append(id).Append("\"><rect x=\"").Append(N(rect.Left))
            .Append("\" y=\"").Append(N(rect.Top)).Append("\" width=\"").Append(N(rect.Width))
            .Append("\" height=\"").Append(N(rect.Height)).Append("\"/></clipPath>\n");
        _sb.Append("<g clip-path=\"url(#").Append(id).Append(")\">\n");

        // Associa o grupo aberto ao frame de Save corrente (fechado no RestoreState).
        if (_saveFrames.Count > 0)
            _saveFrames.Push(_saveFrames.Pop() + 1);
    }

    static string EscapeXml(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}

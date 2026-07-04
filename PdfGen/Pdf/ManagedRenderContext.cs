using System.Text;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;
using Agile.Maui.PdfGen.Text;

namespace Agile.Maui.PdfGen.Pdf;

/// <summary>
/// IRenderContext que emite operadores de content stream PDF. Converte o sistema de coordenadas
/// do layout (origem no topo-esquerda, Y para baixo) para o do PDF (origem embaixo-esquerda, Y para cima).
/// </summary>
internal sealed class ManagedRenderContext : IRenderContext
{
    // Constante de Bézier para aproximar quarto de círculo.
    const float Kappa = 0.5522847498f;

    readonly ManagedPdfRenderer _renderer;
    readonly ManagedPage _page;
    readonly float _pageHeight;
    readonly StringBuilder _sb;

    public ManagedRenderContext(ManagedPdfRenderer renderer, ManagedPage page, float pageHeight)
    {
        _renderer = renderer;
        _page = page;
        _pageHeight = pageHeight;
        _sb = page.Content;
    }

    float FlipY(float y) => _pageHeight - y;

    bool BeginAlpha(byte alpha)
    {
        if (alpha >= 255)
            return false;

        string res = _renderer.GetAlphaResource(alpha, _page);
        _sb.Append("q\n/");
        _sb.Append(res).Append(" gs\n");
        return true;
    }

    bool BeginAlpha(PdfColor color) => BeginAlpha(color.A);

    void EndAlpha(bool active)
    {
        if (active)
            _sb.Append("Q\n");
    }

    public void DrawText(string text, PdfPoint baselineOrigin, TextStyle style)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (style.Embedded is Text.EmbeddedFont embedded)
        {
            DrawTextEmbedded(text, baselineOrigin, style, embedded);
            return;
        }

        string fontRes = _renderer.GetFontResource(style.Font, _page);
        PdfColor c = style.Color;

        bool alpha = BeginAlpha(c);
        _sb.Append("BT\n");
        _sb.Append('/').Append(fontRes).Append(' ').Append(PdfNum.F(style.FontSize)).Append(" Tf\n");
        _sb.Append(PdfNum.F(c.RedF)).Append(' ').Append(PdfNum.F(c.GreenF)).Append(' ')
           .Append(PdfNum.F(c.BlueF)).Append(" rg\n");
        _sb.Append(PdfNum.F(baselineOrigin.X)).Append(' ').Append(PdfNum.F(FlipY(baselineOrigin.Y))).Append(" Td\n");
        _sb.Append('(').Append(PdfNum.EscapeLiteral(text)).Append(") Tj\n");
        _sb.Append("ET\n");
        EndAlpha(alpha);
    }

    /// <summary>Desenha texto com fonte embutida (Type0/Identity-H): a string carrega IDs de glifo em hex.</summary>
    void DrawTextEmbedded(string text, PdfPoint baselineOrigin, TextStyle style, Text.EmbeddedFont embedded)
    {
        var entry = _renderer.GetEmbeddedFontEntry(embedded, _page);
        PdfColor c = style.Color;

        var hex = new StringBuilder(text.Length * 4);
        for (int i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            ushort gid = embedded.GlyphId(cp);
            entry.Use(gid, cp);
            hex.Append(gid.ToString("X4"));
        }

        bool alpha = BeginAlpha(c);
        _sb.Append("BT\n");
        _sb.Append('/').Append(entry.ResourceName).Append(' ').Append(PdfNum.F(style.FontSize)).Append(" Tf\n");
        _sb.Append(PdfNum.F(c.RedF)).Append(' ').Append(PdfNum.F(c.GreenF)).Append(' ')
           .Append(PdfNum.F(c.BlueF)).Append(" rg\n");
        _sb.Append(PdfNum.F(baselineOrigin.X)).Append(' ').Append(PdfNum.F(FlipY(baselineOrigin.Y))).Append(" Td\n");
        _sb.Append('<').Append(hex).Append("> Tj\n");
        _sb.Append("ET\n");
        EndAlpha(alpha);
    }

    public void DrawImage(PdfImage image, PdfRect destination)
    {
        string res = _renderer.GetImageResource(image, _page);
        float x = destination.Left;
        float y = FlipY(destination.Bottom);   // canto inferior no espaço PDF

        _sb.Append("q\n");
        _sb.Append(PdfNum.F(destination.Width)).Append(" 0 0 ").Append(PdfNum.F(destination.Height))
           .Append(' ').Append(PdfNum.F(x)).Append(' ').Append(PdfNum.F(y)).Append(" cm\n");
        _sb.Append('/').Append(res).Append(" Do\n");
        _sb.Append("Q\n");
    }

    public void DrawLine(PdfPoint from, PdfPoint to, PdfColor color, float thickness)
    {
        if (thickness <= 0f || color.IsTransparent)
            return;

        bool alpha = BeginAlpha(color);
        _sb.Append(PdfNum.F(color.RedF)).Append(' ').Append(PdfNum.F(color.GreenF)).Append(' ')
           .Append(PdfNum.F(color.BlueF)).Append(" RG\n");
        _sb.Append(PdfNum.F(thickness)).Append(" w\n");
        _sb.Append(PdfNum.F(from.X)).Append(' ').Append(PdfNum.F(FlipY(from.Y))).Append(" m\n");
        _sb.Append(PdfNum.F(to.X)).Append(' ').Append(PdfNum.F(FlipY(to.Y))).Append(" l\n");
        _sb.Append("S\n");
        EndAlpha(alpha);
    }

    public void DrawRectangle(PdfRect rect, PdfColor color, float thickness, float cornerRadius = 0f)
    {
        if (thickness <= 0f || color.IsTransparent)
            return;

        bool alpha = BeginAlpha(color);
        _sb.Append(PdfNum.F(color.RedF)).Append(' ').Append(PdfNum.F(color.GreenF)).Append(' ')
           .Append(PdfNum.F(color.BlueF)).Append(" RG\n");
        _sb.Append(PdfNum.F(thickness)).Append(" w\n");
        AppendRectPath(rect, cornerRadius);
        _sb.Append("S\n");
        EndAlpha(alpha);
    }

    public void FillRectangle(PdfRect rect, PdfColor color, float cornerRadius = 0f)
    {
        if (color.IsTransparent)
            return;

        bool alpha = BeginAlpha(color);
        _sb.Append(PdfNum.F(color.RedF)).Append(' ').Append(PdfNum.F(color.GreenF)).Append(' ')
           .Append(PdfNum.F(color.BlueF)).Append(" rg\n");
        AppendRectPath(rect, cornerRadius);
        _sb.Append("f\n");
        EndAlpha(alpha);
    }

    public void FillGradient(PdfRect rect, GradientBrush brush, float cornerRadius = 0f)
    {
        string name = _renderer.AddPattern(BuildPatternDict(brush, rect), _page);
        bool alpha = BeginAlpha(CommonAlpha(brush));
        _sb.Append("q\n");
        _sb.Append("/Pattern cs\n");
        _sb.Append('/').Append(name).Append(" scn\n");
        AppendRectPath(rect, cornerRadius);
        _sb.Append("f\n");
        _sb.Append("Q\n");
        EndAlpha(alpha);
    }

    public void StrokeGradient(PdfRect rect, GradientBrush brush, float thickness, float cornerRadius = 0f)
    {
        if (thickness <= 0f)
            return;

        string name = _renderer.AddPattern(BuildPatternDict(brush, rect), _page);
        bool alpha = BeginAlpha(CommonAlpha(brush));
        _sb.Append("q\n");
        _sb.Append("/Pattern CS\n");
        _sb.Append('/').Append(name).Append(" SCN\n");
        _sb.Append(PdfNum.F(thickness)).Append(" w\n");
        AppendRectPath(rect, cornerRadius);
        _sb.Append("S\n");
        _sb.Append("Q\n");
        EndAlpha(alpha);
    }

    static byte CommonAlpha(GradientBrush brush)
    {
        byte alpha = brush.Stops[0].Color.A;
        for (int i = 1; i < brush.Stops.Count; i++)
        {
            if (brush.Stops[i].Color.A != alpha)
                return 255;
        }
        return alpha;
    }

    /// <summary>Monta o dicionário completo de um shading pattern (coordenadas em espaço PDF, y-para-cima).</summary>
    string BuildPatternDict(GradientBrush brush, PdfRect rect)
    {
        var sb = new StringBuilder();
        sb.Append("<< /Type /Pattern /PatternType 2 /Shading << /ColorSpace /DeviceRGB ");

        if (brush.Kind == GradientKind.Radial)
        {
            float cx = rect.Left + rect.Width / 2f;
            float cy = rect.Top + rect.Height / 2f;
            float radius = MathF.Sqrt(rect.Width * rect.Width + rect.Height * rect.Height) / 2f;
            sb.Append("/ShadingType 3 /Coords [")
              .Append(PdfNum.F(cx)).Append(' ').Append(PdfNum.F(FlipY(cy))).Append(" 0 ")
              .Append(PdfNum.F(cx)).Append(' ').Append(PdfNum.F(FlipY(cy))).Append(' ').Append(PdfNum.F(radius))
              .Append("] ");
        }
        else
        {
            // Eixo do gradiente centrado no retângulo, estendido para cobri-lo por completo no ângulo dado.
            float rad = brush.AngleDegrees * MathF.PI / 180f;
            float dx = MathF.Cos(rad);
            float dy = MathF.Sin(rad);
            float cx = rect.Left + rect.Width / 2f;
            float cy = rect.Top + rect.Height / 2f;
            float extent = (MathF.Abs(dx) * rect.Width + MathF.Abs(dy) * rect.Height) / 2f;

            float x0 = cx - dx * extent, y0 = cy - dy * extent;
            float x1 = cx + dx * extent, y1 = cy + dy * extent;

            sb.Append("/ShadingType 2 /Coords [")
              .Append(PdfNum.F(x0)).Append(' ').Append(PdfNum.F(FlipY(y0))).Append(' ')
              .Append(PdfNum.F(x1)).Append(' ').Append(PdfNum.F(FlipY(y1)))
              .Append("] ");
        }

        sb.Append("/Extend [true true] /Function ");
        AppendFunction(sb, brush);
        sb.Append(">> >>");
        return sb.ToString();
    }

    /// <summary>Escreve a função de cor: Type 2 (2 paradas) ou Type 3 stitching (N paradas).</summary>
    static void AppendFunction(StringBuilder sb, GradientBrush brush)
    {
        var stops = brush.Stops;
        if (stops.Count == 2)
        {
            AppendType2(sb, stops[0].Color, stops[1].Color);
            return;
        }

        sb.Append("<< /FunctionType 3 /Domain [0 1] /Functions [");
        for (int i = 0; i < stops.Count - 1; i++)
        {
            AppendType2(sb, stops[i].Color, stops[i + 1].Color);
            sb.Append(' ');
        }
        sb.Append("] /Bounds [");
        for (int i = 1; i < stops.Count - 1; i++)
            sb.Append(PdfNum.F(stops[i].Offset)).Append(' ');
        sb.Append("] /Encode [");
        for (int i = 0; i < stops.Count - 1; i++)
            sb.Append("0 1 ");
        sb.Append("] >>");
    }

    static void AppendType2(StringBuilder sb, PdfColor c0, PdfColor c1)
    {
        sb.Append("<< /FunctionType 2 /Domain [0 1] /C0 [")
          .Append(PdfNum.F(c0.RedF)).Append(' ').Append(PdfNum.F(c0.GreenF)).Append(' ').Append(PdfNum.F(c0.BlueF))
          .Append("] /C1 [")
          .Append(PdfNum.F(c1.RedF)).Append(' ').Append(PdfNum.F(c1.GreenF)).Append(' ').Append(PdfNum.F(c1.BlueF))
          .Append("] /N 1 >>");
    }

    public void SaveState() => _sb.Append("q\n");

    public void RestoreState() => _sb.Append("Q\n");

    public void ClipRectangle(PdfRect rect)
    {
        AppendRectPath(rect, 0f);
        _sb.Append("W n\n");
    }

    void AppendRectPath(PdfRect rect, float radius)
    {
        float x = rect.Left;
        float yTop = FlipY(rect.Top);        // borda superior no espaço PDF
        float yBottom = FlipY(rect.Bottom);  // borda inferior no espaço PDF
        float right = rect.Right;

        if (radius <= 0f)
        {
            _sb.Append(PdfNum.F(x)).Append(' ').Append(PdfNum.F(yBottom)).Append(' ')
               .Append(PdfNum.F(rect.Width)).Append(' ').Append(PdfNum.F(rect.Height)).Append(" re\n");
            return;
        }

        float r = MathF.Min(radius, MathF.Min(rect.Width, rect.Height) / 2f);
        float k = r * Kappa;

        // Percorre o retângulo arredondado (sentido anti-horário no espaço PDF).
        Move(x + r, yBottom);
        Line(right - r, yBottom);
        Curve(right - r + k, yBottom, right, yBottom + r - k, right, yBottom + r);
        Line(right, yTop - r);
        Curve(right, yTop - r + k, right - r + k, yTop, right - r, yTop);
        Line(x + r, yTop);
        Curve(x + r - k, yTop, x, yTop - r + k, x, yTop - r);
        Line(x, yBottom + r);
        Curve(x, yBottom + r - k, x + r - k, yBottom, x + r, yBottom);
        _sb.Append("h\n");
    }

    void Move(float x, float y) => _sb.Append(PdfNum.F(x)).Append(' ').Append(PdfNum.F(y)).Append(" m\n");
    void Line(float x, float y) => _sb.Append(PdfNum.F(x)).Append(' ').Append(PdfNum.F(y)).Append(" l\n");
    void Curve(float x1, float y1, float x2, float y2, float x3, float y3) =>
        _sb.Append(PdfNum.F(x1)).Append(' ').Append(PdfNum.F(y1)).Append(' ')
           .Append(PdfNum.F(x2)).Append(' ').Append(PdfNum.F(y2)).Append(' ')
           .Append(PdfNum.F(x3)).Append(' ').Append(PdfNum.F(y3)).Append(" c\n");
}

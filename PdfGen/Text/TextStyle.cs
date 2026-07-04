using Agile.Maui.PdfGen.Primitives;

namespace Agile.Maui.PdfGen.Text;

/// <summary>Estilo de texto imutável. Use os métodos With* para derivar variações.</summary>
public sealed class TextStyle
{
    public PdfFontFamily Family { get; }
    public float FontSize { get; }
    public FontWeight Weight { get; }
    public FontStyle Style { get; }
    public PdfColor Color { get; }
    public float LineHeight { get; }

    /// <summary>Fonte TrueType/OTF embutida; quando definida, substitui a família base-14.</summary>
    public EmbeddedFont? Embedded { get; }

    public TextStyle(
        PdfFontFamily family = PdfFontFamily.Helvetica,
        float fontSize = 11f,
        FontWeight weight = FontWeight.Normal,
        FontStyle style = FontStyle.Normal,
        PdfColor? color = null,
        float lineHeight = 1.2f,
        EmbeddedFont? embedded = null)
    {
        Family = family;
        FontSize = fontSize;
        Weight = weight;
        Style = style;
        Color = color ?? Colors.Black;
        LineHeight = lineHeight;
        Embedded = embedded;
    }

    public static TextStyle Default { get; } = new();

    public bool IsBold => Weight == FontWeight.Bold;
    public bool IsItalic => Style == FontStyle.Italic;

    public StandardFont Font => StandardFont.Get(Family, IsBold, IsItalic);

    /// <summary>Ascensão efetiva (fração do em), respeitando a fonte embutida se houver.</summary>
    public float Ascent => Embedded?.Ascent ?? Font.Ascent;

    /// <summary>Largura do texto em pontos, respeitando a fonte embutida se houver.</summary>
    public float MeasureWidth(ReadOnlySpan<char> text) =>
        Embedded is not null ? Embedded.MeasureWidth(text, FontSize) : Font.MeasureWidth(text, FontSize);

    /// <summary>Largura de um caractere em unidades/1000, respeitando a fonte embutida se houver.</summary>
    public int GlyphWidth(char c) =>
        Embedded is not null ? Embedded.GlyphWidth(c) : Font.GlyphWidth(c);

    /// <summary>Largura de um codepoint Unicode em unidades/1000, respeitando a fonte embutida se houver.</summary>
    public int GlyphWidth(int codepoint) =>
        Embedded is not null ? Embedded.GlyphWidth(codepoint) : Font.GlyphWidth(codepoint);

    /// <summary>Altura de uma linha em pontos.</summary>
    public float LineSpacing => FontSize * LineHeight;

    public TextStyle WithFamily(PdfFontFamily family) =>
        new(family, FontSize, Weight, Style, Color, LineHeight, Embedded);
    public TextStyle WithFontSize(float size) =>
        new(Family, size, Weight, Style, Color, LineHeight, Embedded);
    public TextStyle WithWeight(FontWeight weight) =>
        new(Family, FontSize, weight, Style, Color, LineHeight, Embedded);
    public TextStyle WithStyle(FontStyle style) =>
        new(Family, FontSize, Weight, style, Color, LineHeight, Embedded);
    public TextStyle WithColor(PdfColor color) =>
        new(Family, FontSize, Weight, Style, color, LineHeight, Embedded);
    public TextStyle WithLineHeight(float lineHeight) =>
        new(Family, FontSize, Weight, Style, Color, lineHeight, Embedded);
    public TextStyle WithFont(EmbeddedFont font) =>
        new(Family, FontSize, Weight, Style, Color, LineHeight, font);
}

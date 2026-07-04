using Agile.Maui.PdfGen.Primitives;

namespace Agile.Maui.PdfGen.Text;

/// <summary>
/// Uma das 14 fontes padrão do PDF, com larguras AFM (por 1000 unidades de em) para
/// medição precisa de texto sem depender da plataforma.
/// </summary>
public sealed class StandardFont
{
    readonly ushort[] _widths;   // largura por caractere ASCII 32..126, em unidades/1000
    readonly ushort _defaultWidth;

    private StandardFont(string baseName, PdfFontFamily family, bool bold, bool italic, ushort[] widths, ushort defaultWidth)
    {
        BaseName = baseName;
        Family = family;
        Bold = bold;
        Italic = italic;
        _widths = widths;
        _defaultWidth = defaultWidth;
    }

    /// <summary>Nome base-14 do PDF (ex.: "Helvetica-Bold").</summary>
    public string BaseName { get; }
    public PdfFontFamily Family { get; }
    public bool Bold { get; }
    public bool Italic { get; }

    /// <summary>Ascensão típica em unidades/1000 (para posicionar a baseline).</summary>
    public float Ascent => 0.718f;
    public float Descent => 0.207f;

    /// <summary>Largura do texto no tamanho de fonte informado, em pontos.</summary>
    public float MeasureWidth(ReadOnlySpan<char> text, float fontSize)
    {
        int total = 0;
        for (int i = 0; i < text.Length; i++)
            total += GlyphWidth(text[i]);
        return total / 1000f * fontSize;
    }

    /// <summary>Largura de um único caractere, em unidades/1000.</summary>
    public int GlyphWidth(char c)
    {
        int idx = c - 32;
        if (idx >= 0 && idx < _widths.Length)
            return _widths[idx];

        // Faixa WinAnsi acentuada (0xA0..0xFF): nas fontes base-14 a letra acentuada tem o
        // mesmo avanço da letra base, então reusamos a largura ASCII correspondente.
        char baseChar = WinAnsiBase(c);
        if (baseChar != '\0')
        {
            int bidx = baseChar - 32;
            if (bidx >= 0 && bidx < _widths.Length)
                return _widths[bidx];
        }

        return _defaultWidth;
    }

    /// <summary>
    /// Letra ASCII cuja largura equivale à do caractere acentuado WinAnsi informado, ou '\0'
    /// quando não há equivalência direta (símbolos como ©, °, × usam a largura padrão).
    /// </summary>
    static char WinAnsiBase(char c) => c switch
    {
        >= 'À' and <= 'Å' => 'A',   // À Á Â Ã Ä Å
        'Ç' => 'C',                        // Ç
        >= 'È' and <= 'Ë' => 'E',    // È É Ê Ë
        >= 'Ì' and <= 'Ï' => 'I',    // Ì Í Î Ï
        'Ñ' => 'N',                        // Ñ
        (>= 'Ò' and <= 'Ö') or 'Ø' => 'O',   // Ò Ó Ô Õ Ö Ø
        >= 'Ù' and <= 'Ü' => 'U',    // Ù Ú Û Ü
        'Ý' => 'Y',                        // Ý
        'ß' => 's',                        // ß (aproximação)
        >= 'à' and <= 'å' => 'a',    // à á â ã ä å
        'ç' => 'c',                        // ç
        >= 'è' and <= 'ë' => 'e',    // è é ê ë
        >= 'ì' and <= 'ï' => 'i',    // ì í î ï
        'ñ' => 'n',                        // ñ
        (>= 'ò' and <= 'ö') or 'ø' => 'o',   // ò ó ô õ ö ø
        >= 'ù' and <= 'ü' => 'u',    // ù ú û ü
        'ý' or 'ÿ' => 'y',           // ý ÿ
        _ => '\0',
    };

    // ---- Fontes base-14 (larguras AFM WinAnsi para ASCII 32..126) ----

    public static StandardFont Get(PdfFontFamily family, bool bold, bool italic) => family switch
    {
        PdfFontFamily.Times => TimesFor(bold, italic),
        PdfFontFamily.Courier => CourierFor(bold, italic),
        _ => HelveticaFor(bold, italic),
    };

    static StandardFont HelveticaFor(bool bold, bool italic)
    {
        string name = (bold, italic) switch
        {
            (true, true) => "Helvetica-BoldOblique",
            (true, false) => "Helvetica-Bold",
            (false, true) => "Helvetica-Oblique",
            _ => "Helvetica",
        };
        ushort[] w = bold ? HelveticaBoldWidths : HelveticaWidths;
        return new StandardFont(name, PdfFontFamily.Helvetica, bold, italic, w, bold ? (ushort)556 : (ushort)556);
    }

    static StandardFont TimesFor(bool bold, bool italic)
    {
        string name = (bold, italic) switch
        {
            (true, true) => "Times-BoldItalic",
            (true, false) => "Times-Bold",
            (false, true) => "Times-Italic",
            _ => "Times-Roman",
        };
        ushort[] w = bold ? TimesBoldWidths : TimesRomanWidths;
        return new StandardFont(name, PdfFontFamily.Times, bold, italic, w, (ushort)500);
    }

    static StandardFont CourierFor(bool bold, bool italic)
    {
        string name = (bold, italic) switch
        {
            (true, true) => "Courier-BoldOblique",
            (true, false) => "Courier-Bold",
            (false, true) => "Courier-Oblique",
            _ => "Courier",
        };
        return new StandardFont(name, PdfFontFamily.Courier, bold, italic, CourierWidths, 600);
    }

    // Larguras AFM (índice = code - 32), ASCII 32..126.
    static readonly ushort[] HelveticaWidths =
    {
        278,278,355,556,556,889,667,191,333,333,389,584,278,333,278,278,
        556,556,556,556,556,556,556,556,556,556,278,278,584,584,584,556,
        1015,667,667,722,722,667,611,778,722,278,500,667,556,833,722,778,
        667,778,722,667,611,722,667,944,667,667,611,278,278,278,469,556,
        333,556,556,500,556,556,278,556,556,222,222,500,222,833,556,556,
        556,556,333,500,278,556,500,722,500,500,500,334,260,334,584
    };

    static readonly ushort[] HelveticaBoldWidths =
    {
        278,333,474,556,556,889,722,238,333,333,389,584,278,333,278,278,
        556,556,556,556,556,556,556,556,556,556,333,333,584,584,584,611,
        975,722,722,722,722,667,611,778,722,278,556,722,611,833,722,778,
        667,778,722,667,611,722,667,944,667,667,611,333,278,333,584,556,
        333,556,611,556,611,556,333,611,611,278,278,556,278,889,611,611,
        611,611,389,556,333,611,556,778,556,556,500,389,280,389,584
    };

    static readonly ushort[] TimesRomanWidths =
    {
        250,333,408,500,500,833,778,180,333,333,500,564,250,333,250,278,
        500,500,500,500,500,500,500,500,500,500,278,278,564,564,564,444,
        921,722,667,667,722,611,556,722,722,333,389,722,611,889,722,722,
        556,722,667,556,611,722,722,944,722,722,611,333,278,333,469,500,
        333,444,500,444,500,444,333,500,500,278,278,500,278,778,500,500,
        500,500,333,389,278,500,500,722,500,500,444,480,200,480,541
    };

    static readonly ushort[] TimesBoldWidths =
    {
        250,333,555,500,500,1000,833,278,333,333,500,570,250,333,250,278,
        500,500,500,500,500,500,500,500,500,500,333,333,570,570,570,500,
        930,722,667,722,722,667,611,778,778,389,500,778,667,944,722,778,
        611,778,722,556,667,722,722,1000,722,722,667,333,278,333,581,500,
        333,500,556,444,556,444,333,500,556,278,333,556,278,833,556,500,
        556,556,444,389,333,556,500,722,500,500,444,394,220,394,520
    };

    static readonly ushort[] CourierWidths = MakeUniform(600);

    static ushort[] MakeUniform(ushort value)
    {
        var arr = new ushort[95];
        for (int i = 0; i < arr.Length; i++)
            arr[i] = value;
        return arr;
    }
}

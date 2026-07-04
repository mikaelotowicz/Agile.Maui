using System.Collections.Generic;
using System.IO;

namespace Agile.Maui.PdfGen.Text;

/// <summary>
/// Fonte TrueType/OpenType (contornos glyf) carregada de bytes, com métricas para medição
/// independente de plataforma e dados prontos para embutir no PDF como Type0/CIDFontType2 (Identity-H).
/// Suporta Unicode completo (qualquer glifo presente na fonte). Fontes CFF ('OTTO') não são suportadas.
/// </summary>
public sealed class EmbeddedFont
{
    readonly ushort[] _advances;          // avanço por glyphId, em unidades da fonte
    readonly Dictionary<int, ushort> _cmap;   // codepoint Unicode → glyphId
    readonly int _unitsPerEm;

    /// <summary>Bytes originais do arquivo de fonte (embutidos como FontFile2).</summary>
    public byte[] FontData { get; }
    /// <summary>Nome PostScript sanitizado, usado como BaseFont no PDF.</summary>
    public string PostScriptName { get; }
    public int NumGlyphs { get; }

    /// <summary>Ascensão como fração do em (para posicionar a baseline).</summary>
    public float Ascent { get; }
    /// <summary>Profundidade como fração do em (valor positivo).</summary>
    public float Descent { get; }

    // FontBBox em unidades/1000 (para o FontDescriptor).
    internal int BBoxXMin { get; }
    internal int BBoxYMin { get; }
    internal int BBoxXMax { get; }
    internal int BBoxYMax { get; }
    internal int CapHeight { get; }

    EmbeddedFont(byte[] data, string psName, int unitsPerEm, int numGlyphs, ushort[] advances,
        Dictionary<int, ushort> cmap, float ascent, float descent,
        int bxMin, int byMin, int bxMax, int byMax, int capHeight)
    {
        FontData = data;
        PostScriptName = psName;
        _unitsPerEm = unitsPerEm;
        NumGlyphs = numGlyphs;
        _advances = advances;
        _cmap = cmap;
        Ascent = ascent;
        Descent = descent;
        BBoxXMin = bxMin;
        BBoxYMin = byMin;
        BBoxXMax = bxMax;
        BBoxYMax = byMax;
        CapHeight = capHeight;
    }

    public static EmbeddedFont FromFile(string path) => Load(File.ReadAllBytes(path));

    /// <summary>Carrega e analisa uma fonte TrueType/OTF a partir dos bytes do arquivo.</summary>
    public static EmbeddedFont Load(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var r = new BeReader(data);

        uint sfnt = r.U32(0);
        if (sfnt == 0x4F54544F) // 'OTTO'
            throw new NotSupportedException("Fontes OpenType/CFF ('OTTO') não são suportadas; use uma fonte TrueType (glyf).");

        int numTables = r.U16(4);
        var tables = new Dictionary<string, (int offset, int length)>();
        int dir = 12;
        for (int i = 0; i < numTables; i++)
        {
            int rec = dir + i * 16;
            string tag = System.Text.Encoding.ASCII.GetString(data, rec, 4);
            int off = (int)r.U32(rec + 8);
            int len = (int)r.U32(rec + 12);
            tables[tag] = (off, len);
        }

        if (!tables.TryGetValue("head", out var head) || !tables.TryGetValue("hhea", out var hhea)
            || !tables.TryGetValue("maxp", out var maxp) || !tables.TryGetValue("hmtx", out var hmtx)
            || !tables.TryGetValue("cmap", out var cmap) || !tables.ContainsKey("glyf"))
            throw new NotSupportedException("Fonte sem as tabelas TrueType necessárias (head/hhea/maxp/hmtx/cmap/glyf).");

        int unitsPerEm = r.U16(head.offset + 18);
        if (unitsPerEm == 0) unitsPerEm = 1000;
        int xMin = r.I16(head.offset + 36);
        int yMin = r.I16(head.offset + 38);
        int xMax = r.I16(head.offset + 40);
        int yMax = r.I16(head.offset + 42);

        int ascender = r.I16(hhea.offset + 4);
        int descender = r.I16(hhea.offset + 6);
        int numberOfHMetrics = r.U16(hhea.offset + 34);
        int numGlyphs = r.U16(maxp.offset + 4);

        var advances = new ushort[numGlyphs];
        ushort last = 0;
        for (int g = 0; g < numGlyphs; g++)
        {
            if (g < numberOfHMetrics)
                last = (ushort)r.U16(hmtx.offset + g * 4);
            advances[g] = last;
        }

        Dictionary<int, ushort> charMap = ParseCmap(r, cmap.offset);

        int capHeight = 0;
        if (tables.TryGetValue("OS/2", out var os2) && os2.length >= 90)
            capHeight = r.I16(os2.offset + 88);
        int scale = 1000;
        float toEm(int v) => (float)v / unitsPerEm;
        int to1000(int v) => (int)MathF.Round((float)v * scale / unitsPerEm);
        if (capHeight <= 0)
            capHeight = to1000((int)(ascender * 0.7f));
        else
            capHeight = to1000(capHeight);

        string psName = ReadPostScriptName(r, tables, data);

        return new EmbeddedFont(
            data, psName, unitsPerEm, numGlyphs, advances, charMap,
            toEm(ascender), MathF.Abs(toEm(descender)),
            to1000(xMin), to1000(yMin), to1000(xMax), to1000(yMax), capHeight);
    }

    /// <summary>GlyphId de um codepoint (0 = .notdef quando ausente).</summary>
    public ushort GlyphId(int codepoint) => _cmap.TryGetValue(codepoint, out ushort g) ? g : (ushort)0;

    /// <summary>Avanço de um glifo em unidades/1000.</summary>
    public int AdvanceUnits1000(ushort gid)
    {
        int adv = gid < _advances.Length ? _advances[gid] : 0;
        return (int)MathF.Round((float)adv * 1000f / _unitsPerEm);
    }

    /// <summary>Largura de um caractere em unidades/1000 (usada na quebra de linha).</summary>
    public int GlyphWidth(char c) => AdvanceUnits1000(GlyphId(c));

    /// <summary>Largura do texto no tamanho informado, em pontos. Trata pares substitutos Unicode.</summary>
    public float MeasureWidth(ReadOnlySpan<char> text, float fontSize)
    {
        int total = 0;
        for (int i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            total += AdvanceUnits1000(GlyphId(cp));
        }
        return total / 1000f * fontSize;
    }

    static Dictionary<int, ushort> ParseCmap(BeReader r, int cmapOffset)
    {
        int numSub = r.U16(cmapOffset + 2);
        int bestOffset = -1;
        int bestScore = -1;

        for (int i = 0; i < numSub; i++)
        {
            int rec = cmapOffset + 4 + i * 8;
            int platform = r.U16(rec);
            int encoding = r.U16(rec + 2);
            int subOffset = (int)r.U32(rec + 4);

            // Preferência: Windows UCS-4 (3,10) > Windows BMP (3,1) > Unicode (0,*).
            int score = (platform, encoding) switch
            {
                (3, 10) => 5,
                (3, 1) => 4,
                (0, _) => 3,
                (3, 0) => 1,
                _ => 0,
            };
            if (score > bestScore)
            {
                bestScore = score;
                bestOffset = cmapOffset + subOffset;
            }
        }

        var map = new Dictionary<int, ushort>();
        if (bestOffset < 0)
            return map;

        int format = r.U16(bestOffset);
        if (format == 4)
            ParseFormat4(r, bestOffset, map);
        else if (format == 12)
            ParseFormat12(r, bestOffset, map);
        else if (format == 6)
            ParseFormat6(r, bestOffset, map);
        else if (format == 0)
            ParseFormat0(r, bestOffset, map);

        return map;
    }

    static void ParseFormat0(BeReader r, int off, Dictionary<int, ushort> map)
    {
        for (int c = 0; c < 256; c++)
            map[c] = r.U8(off + 6 + c);
    }

    static void ParseFormat6(BeReader r, int off, Dictionary<int, ushort> map)
    {
        int first = r.U16(off + 6);
        int count = r.U16(off + 8);
        for (int i = 0; i < count; i++)
            map[first + i] = (ushort)r.U16(off + 10 + i * 2);
    }

    static void ParseFormat4(BeReader r, int off, Dictionary<int, ushort> map)
    {
        int segX2 = r.U16(off + 6);
        int segCount = segX2 / 2;
        int endOff = off + 14;
        int startOff = endOff + segX2 + 2;   // + reservedPad
        int deltaOff = startOff + segX2;
        int rangeOff = deltaOff + segX2;

        for (int s = 0; s < segCount; s++)
        {
            int end = r.U16(endOff + s * 2);
            int start = r.U16(startOff + s * 2);
            int delta = r.I16(deltaOff + s * 2);
            int rangeOffset = r.U16(rangeOff + s * 2);

            for (int c = start; c <= end && c != 0xFFFF; c++)
            {
                ushort gid;
                if (rangeOffset == 0)
                {
                    gid = (ushort)((c + delta) & 0xFFFF);
                }
                else
                {
                    int glyphAddr = rangeOff + s * 2 + rangeOffset + (c - start) * 2;
                    int g = r.U16(glyphAddr);
                    gid = g == 0 ? (ushort)0 : (ushort)((g + delta) & 0xFFFF);
                }
                if (gid != 0)
                    map[c] = gid;
            }
        }
    }

    static void ParseFormat12(BeReader r, int off, Dictionary<int, ushort> map)
    {
        int nGroups = (int)r.U32(off + 12);
        for (int i = 0; i < nGroups; i++)
        {
            int g = off + 16 + i * 12;
            uint startChar = r.U32(g);
            uint endChar = r.U32(g + 4);
            uint startGid = r.U32(g + 8);
            for (uint c = startChar; c <= endChar; c++)
                map[(int)c] = (ushort)(startGid + (c - startChar));
        }
    }

    static string ReadPostScriptName(BeReader r, Dictionary<string, (int offset, int length)> tables, byte[] data)
    {
        if (!tables.TryGetValue("name", out var name))
            return "EmbeddedFont";

        int count = r.U16(name.offset + 2);
        int stringOffset = name.offset + r.U16(name.offset + 4);
        for (int i = 0; i < count; i++)
        {
            int rec = name.offset + 6 + i * 12;
            int platform = r.U16(rec);
            int nameId = r.U16(rec + 6);
            int len = r.U16(rec + 8);
            int strOff = r.U16(rec + 10);
            if (nameId != 6)
                continue;

            string value = platform == 3
                ? System.Text.Encoding.BigEndianUnicode.GetString(data, stringOffset + strOff, len)
                : System.Text.Encoding.ASCII.GetString(data, stringOffset + strOff, len);
            return Sanitize(value);
        }
        return "EmbeddedFont";
    }

    static string Sanitize(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (c > 32 && c < 127 && c != '(' && c != ')' && c != '<' && c != '>'
                && c != '[' && c != ']' && c != '{' && c != '}' && c != '/' && c != '%')
                sb.Append(c);
        }
        return sb.Length == 0 ? "EmbeddedFont" : sb.ToString();
    }

    /// <summary>Leitor big-endian sobre o buffer da fonte.</summary>
    readonly struct BeReader
    {
        readonly byte[] _d;
        public BeReader(byte[] d) => _d = d;

        public byte U8(int o) => _d[o];
        public int U16(int o) => (_d[o] << 8) | _d[o + 1];
        public int I16(int o) => (short)((_d[o] << 8) | _d[o + 1]);
        public uint U32(int o) => ((uint)_d[o] << 24) | ((uint)_d[o + 1] << 16) | ((uint)_d[o + 2] << 8) | _d[o + 3];
    }
}

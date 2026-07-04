using System.Collections.Generic;
using System.IO;
using System.Text;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;
using Agile.Maui.PdfGen.Text;

namespace Agile.Maui.PdfGen.Pdf;

/// <summary>Página acumulada pelo escritor gerenciado.</summary>
internal sealed class ManagedPage
{
    public PdfSize Size { get; }
    public StringBuilder Content { get; } = new();
    public HashSet<string> Fonts { get; } = new();
    public HashSet<string> Images { get; } = new();
    public HashSet<string> Patterns { get; } = new();

    public int ContentObjId;
    public int PageObjId;

    public ManagedPage(PdfSize size)
    {
        Size = size;
    }
}

/// <summary>
/// Escritor PDF 100% gerenciado (C# puro), sem dependências nativas. É o backend padrão e o
/// backend do Windows. Suporta fontes base-14, fontes TrueType/OTF embutidas (Type0/Identity-H),
/// imagens JPEG (DCTDecode) e PNG (FlateDecode + SMask), gradientes e cor sólida.
/// </summary>
public sealed class ManagedPdfRenderer : IPdfRenderer
{
    readonly List<ManagedPage> _pages = new();
    readonly Dictionary<string, FontEntry> _fonts = new();
    readonly List<FontEntry> _fontOrder = new();
    readonly Dictionary<EmbeddedFont, EmbeddedFontEntry> _embedded = new();
    readonly List<EmbeddedFontEntry> _embeddedOrder = new();
    readonly Dictionary<PdfImage, ImageEntry> _images = new();
    readonly List<ImageEntry> _imageOrder = new();
    readonly List<PatternEntry> _patternOrder = new();

    sealed class FontEntry
    {
        public required string BaseName;
        public required string ResourceName;
        public int ObjId;
    }

    internal sealed class EmbeddedFontEntry
    {
        public required EmbeddedFont Font;
        public required string ResourceName;
        public int Type0ObjId;
        public int CidFontObjId;
        public int DescriptorObjId;
        public int FontFileObjId;
        public int ToUnicodeObjId;

        // Glifos efetivamente usados (para montar /W e o CMap ToUnicode).
        public readonly SortedDictionary<ushort, int> UsedGlyphs = new(); // gid → codepoint

        public void Use(ushort gid, int codepoint) => UsedGlyphs[gid] = codepoint;
    }

    internal EmbeddedFontEntry GetEmbeddedFontEntry(EmbeddedFont font, ManagedPage page)
    {
        if (!_embedded.TryGetValue(font, out EmbeddedFontEntry? entry))
        {
            entry = new EmbeddedFontEntry { Font = font, ResourceName = "TT" + _embeddedOrder.Count };
            _embedded[font] = entry;
            _embeddedOrder.Add(entry);
        }
        page.Fonts.Add(entry.ResourceName);
        return entry;
    }

    sealed class ImageEntry
    {
        public required PdfImage Image;
        public required string ResourceName;
        public int ObjId;

        // Dados já preparados para o stream do XObject de imagem.
        public byte[] Stream = System.Array.Empty<byte>();
        public int PixelWidth;
        public int PixelHeight;
        public bool IsJpeg;              // true = DCTDecode (bytes crus); false = FlateDecode (RGB)
        public byte[]? SMaskStream;      // alfa deflacionado (cinza) ou null quando opaco
        public int SMaskObjId;           // id do objeto SMask (0 = sem máscara)
    }

    sealed class PatternEntry
    {
        public required string ResourceName;
        public required string DictBody;   // corpo completo do dicionário do pattern (shading/função inline)
        public int ObjId;
    }

    /// <summary>Registra um shading pattern e devolve seu nome de recurso na página.</summary>
    internal string AddPattern(string dictBody, ManagedPage page)
    {
        var entry = new PatternEntry { ResourceName = "P" + _patternOrder.Count, DictBody = dictBody };
        _patternOrder.Add(entry);
        page.Patterns.Add(entry.ResourceName);
        return entry.ResourceName;
    }

    public void BeginDocument() { }

    public IRenderContext BeginPage(PdfSize size)
    {
        var page = new ManagedPage(size);
        _pages.Add(page);
        return new ManagedRenderContext(this, page, size.Height);
    }

    public void EndPage() { }

    internal string GetFontResource(StandardFont font, ManagedPage page)
    {
        if (!_fonts.TryGetValue(font.BaseName, out FontEntry? entry))
        {
            entry = new FontEntry { BaseName = font.BaseName, ResourceName = "F" + _fontOrder.Count };
            _fonts[font.BaseName] = entry;
            _fontOrder.Add(entry);
        }

        page.Fonts.Add(entry.ResourceName);
        return entry.ResourceName;
    }

    internal string GetImageResource(PdfImage image, ManagedPage page)
    {
        if (!_images.TryGetValue(image, out ImageEntry? entry))
        {
            entry = new ImageEntry { Image = image, ResourceName = "Im" + _imageOrder.Count };
            PrepareImage(image, entry);
            _images[image] = entry;
            _imageOrder.Add(entry);
        }

        page.Images.Add(entry.ResourceName);
        return entry.ResourceName;
    }

    static void PrepareImage(PdfImage image, ImageEntry entry)
    {
        if (image.Format == ImageFormat.Jpeg)
        {
            // JPEG embarca cru via DCTDecode — o PDF fala esse filtro nativamente.
            entry.IsJpeg = true;
            entry.Stream = image.Data;
            entry.PixelWidth = image.PixelWidth;
            entry.PixelHeight = image.PixelHeight;
            return;
        }

        // PNG: decodifica para RGB (+ alfa) e re-embute com FlateDecode; o alfa vira SMask.
        DecodedPng png = PngDecoder.Decode(image.Data);
        entry.IsJpeg = false;
        entry.PixelWidth = png.Width;
        entry.PixelHeight = png.Height;
        entry.Stream = Deflate(png.Rgb);
        if (png.Alpha is not null)
            entry.SMaskStream = Deflate(png.Alpha);
    }

    /// <summary>Comprime em zlib (RFC 1950), o formato esperado pelo filtro /FlateDecode do PDF.</summary>
    static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data, 0, data.Length);
        return output.ToArray();
    }

    public byte[] EndDocument() => Serialize();

    byte[] Serialize()
    {
        // Atribui números de objeto.
        int nextId = 3;   // 1=Catalog, 2=Pages
        foreach (FontEntry f in _fontOrder)
            f.ObjId = nextId++;
        foreach (EmbeddedFontEntry ef in _embeddedOrder)
        {
            ef.Type0ObjId = nextId++;
            ef.CidFontObjId = nextId++;
            ef.DescriptorObjId = nextId++;
            ef.FontFileObjId = nextId++;
            ef.ToUnicodeObjId = nextId++;
        }
        foreach (ImageEntry im in _imageOrder)
        {
            im.ObjId = nextId++;
            if (im.SMaskStream is not null)
                im.SMaskObjId = nextId++;
        }
        foreach (PatternEntry p in _patternOrder)
            p.ObjId = nextId++;
        foreach (ManagedPage p in _pages)
        {
            p.ContentObjId = nextId++;
            p.PageObjId = nextId++;
        }
        int maxId = nextId - 1;

        var ms = new MemoryStream();
        var offsets = new long[maxId + 1];

        WriteAscii(ms, "%PDF-1.7\n");
        WriteBytes(ms, new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });

        // 1: Catalog
        // /ViewerPreferences /PrintScaling /None → o diálogo de impressão abre em "Tamanho real"
        // (sem "ajustar à página"), evitando que o visualizador reescale a folha A4 e desloque o
        // rodapé para cima. Faz o rodapé imprimir no fundo de forma consistente entre navegadores.
        offsets[1] = ms.Length;
        WriteAscii(ms, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R /ViewerPreferences << /PrintScaling /None >> >>\nendobj\n");

        // 2: Pages
        offsets[2] = ms.Length;
        var kids = new StringBuilder();
        foreach (ManagedPage p in _pages)
            kids.Append(p.PageObjId).Append(" 0 R ");
        WriteAscii(ms, $"2 0 obj\n<< /Type /Pages /Kids [ {kids}] /Count {_pages.Count} >>\nendobj\n");

        // Fontes
        foreach (FontEntry f in _fontOrder)
        {
            offsets[f.ObjId] = ms.Length;
            WriteAscii(ms,
                $"{f.ObjId} 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /{f.BaseName} /Encoding /WinAnsiEncoding >>\nendobj\n");
        }

        WriteEmbeddedFonts(ms, offsets);

        // Imagens: JPEG via DCTDecode (cru); PNG via FlateDecode (RGB) + SMask opcional para o alfa.
        foreach (ImageEntry im in _imageOrder)
        {
            offsets[im.ObjId] = ms.Length;
            string filter = im.IsJpeg ? "/DCTDecode" : "/FlateDecode";
            string smask = im.SMaskObjId > 0 ? $" /SMask {im.SMaskObjId} 0 R" : "";
            WriteAscii(ms,
                $"{im.ObjId} 0 obj\n<< /Type /XObject /Subtype /Image /Width {im.PixelWidth} /Height {im.PixelHeight} " +
                $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter {filter}{smask} /Length {im.Stream.Length} >>\nstream\n");
            WriteBytes(ms, im.Stream);
            WriteAscii(ms, "\nendstream\nendobj\n");

            if (im.SMaskStream is not null)
            {
                offsets[im.SMaskObjId] = ms.Length;
                WriteAscii(ms,
                    $"{im.SMaskObjId} 0 obj\n<< /Type /XObject /Subtype /Image /Width {im.PixelWidth} /Height {im.PixelHeight} " +
                    $"/ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode /Length {im.SMaskStream.Length} >>\nstream\n");
                WriteBytes(ms, im.SMaskStream);
                WriteAscii(ms, "\nendstream\nendobj\n");
            }
        }

        // Shading patterns (gradientes)
        foreach (PatternEntry pat in _patternOrder)
        {
            offsets[pat.ObjId] = ms.Length;
            WriteAscii(ms, $"{pat.ObjId} 0 obj\n{pat.DictBody}\nendobj\n");
        }

        // Páginas: content + dict
        foreach (ManagedPage p in _pages)
        {
            byte[] content = Encoding.Latin1.GetBytes(p.Content.ToString());
            offsets[p.ContentObjId] = ms.Length;
            WriteAscii(ms, $"{p.ContentObjId} 0 obj\n<< /Length {content.Length} >>\nstream\n");
            WriteBytes(ms, content);
            WriteAscii(ms, "\nendstream\nendobj\n");

            offsets[p.PageObjId] = ms.Length;
            WriteAscii(ms, $"{p.PageObjId} 0 obj\n<< /Type /Page /Parent 2 0 R ");
            WriteAscii(ms, $"/MediaBox [0 0 {PdfNum.F(p.Size.Width)} {PdfNum.F(p.Size.Height)}] ");
            WriteAscii(ms, "/Resources << ");
            WriteResources(ms, p);
            WriteAscii(ms, $">> /Contents {p.ContentObjId} 0 R >>\nendobj\n");
        }

        // xref
        long xrefOffset = ms.Length;
        WriteAscii(ms, $"xref\n0 {maxId + 1}\n");
        WriteAscii(ms, "0000000000 65535 f\r\n");
        for (int id = 1; id <= maxId; id++)
            WriteAscii(ms, offsets[id].ToString("D10") + " 00000 n\r\n");

        WriteAscii(ms, $"trailer\n<< /Size {maxId + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    void WriteEmbeddedFonts(MemoryStream ms, long[] offsets)
    {
        foreach (EmbeddedFontEntry ef in _embeddedOrder)
        {
            EmbeddedFont f = ef.Font;

            // FontFile2: fonte reduzida aos glifos usados, comprimida (FlateDecode), /Length1 = tamanho do subset.
            byte[] subset = TrueTypeSubsetter.Subset(f.FontData, ef.UsedGlyphs.Keys);
            byte[] compressed = Deflate(subset);
            offsets[ef.FontFileObjId] = ms.Length;
            WriteAscii(ms, $"{ef.FontFileObjId} 0 obj\n<< /Length {compressed.Length} /Length1 {subset.Length} /Filter /FlateDecode >>\nstream\n");
            WriteBytes(ms, compressed);
            WriteAscii(ms, "\nendstream\nendobj\n");

            // FontDescriptor
            int ascent = (int)MathF.Round(f.Ascent * 1000f);
            int descent = -(int)MathF.Round(f.Descent * 1000f);
            offsets[ef.DescriptorObjId] = ms.Length;
            WriteAscii(ms,
                $"{ef.DescriptorObjId} 0 obj\n<< /Type /FontDescriptor /FontName /{f.PostScriptName} /Flags 32 " +
                $"/FontBBox [{f.BBoxXMin} {f.BBoxYMin} {f.BBoxXMax} {f.BBoxYMax}] /ItalicAngle 0 " +
                $"/Ascent {ascent} /Descent {descent} /CapHeight {f.CapHeight} /StemV 80 " +
                $"/FontFile2 {ef.FontFileObjId} 0 R >>\nendobj\n");

            // CIDFontType2 (descendente) com /W dos glifos usados.
            offsets[ef.CidFontObjId] = ms.Length;
            WriteAscii(ms,
                $"{ef.CidFontObjId} 0 obj\n<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{f.PostScriptName} " +
                "/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
                $"/FontDescriptor {ef.DescriptorObjId} 0 R /CIDToGIDMap /Identity /DW 1000 /W [{BuildWidths(ef)}] >>\nendobj\n");

            // CMap ToUnicode (para copiar/colar e busca).
            byte[] toUni = Encoding.ASCII.GetBytes(BuildToUnicode(ef));
            offsets[ef.ToUnicodeObjId] = ms.Length;
            WriteAscii(ms, $"{ef.ToUnicodeObjId} 0 obj\n<< /Length {toUni.Length} >>\nstream\n");
            WriteBytes(ms, toUni);
            WriteAscii(ms, "\nendstream\nendobj\n");

            // Type0 (fonte referenciada pelas páginas).
            offsets[ef.Type0ObjId] = ms.Length;
            WriteAscii(ms,
                $"{ef.Type0ObjId} 0 obj\n<< /Type /Font /Subtype /Type0 /BaseFont /{f.PostScriptName} " +
                $"/Encoding /Identity-H /DescendantFonts [{ef.CidFontObjId} 0 R] /ToUnicode {ef.ToUnicodeObjId} 0 R >>\nendobj\n");
        }
    }

    static string BuildWidths(EmbeddedFontEntry ef)
    {
        var sb = new StringBuilder();
        foreach (System.Collections.Generic.KeyValuePair<ushort, int> kv in ef.UsedGlyphs)
            sb.Append(kv.Key).Append(" [").Append(ef.Font.AdvanceUnits1000(kv.Key)).Append("] ");
        return sb.ToString();
    }

    static string BuildToUnicode(EmbeddedFontEntry ef)
    {
        var entries = new List<(ushort gid, int cp)>();
        foreach (System.Collections.Generic.KeyValuePair<ushort, int> kv in ef.UsedGlyphs)
            if (kv.Key != 0)
                entries.Add((kv.Key, kv.Value));

        var sb = new StringBuilder();
        sb.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
        sb.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        sb.Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
        sb.Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");

        for (int i = 0; i < entries.Count; i += 100)
        {
            int n = System.Math.Min(100, entries.Count - i);
            sb.Append(n).Append(" beginbfchar\n");
            for (int j = i; j < i + n; j++)
            {
                (ushort gid, int cp) = entries[j];
                sb.Append('<').Append(gid.ToString("X4")).Append("> <").Append(Utf16Hex(cp)).Append(">\n");
            }
            sb.Append("endbfchar\n");
        }

        sb.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return sb.ToString();
    }

    static string Utf16Hex(int codepoint)
    {
        string s = char.ConvertFromUtf32(codepoint);
        var sb = new StringBuilder(s.Length * 4);
        foreach (char ch in s)
            sb.Append(((int)ch).ToString("X4"));
        return sb.ToString();
    }

    void WriteResources(MemoryStream ms, ManagedPage p)
    {
        if (p.Fonts.Count > 0)
        {
            WriteAscii(ms, "/Font << ");
            foreach (FontEntry f in _fontOrder)
            {
                if (p.Fonts.Contains(f.ResourceName))
                    WriteAscii(ms, $"/{f.ResourceName} {f.ObjId} 0 R ");
            }
            foreach (EmbeddedFontEntry ef in _embeddedOrder)
            {
                if (p.Fonts.Contains(ef.ResourceName))
                    WriteAscii(ms, $"/{ef.ResourceName} {ef.Type0ObjId} 0 R ");
            }
            WriteAscii(ms, ">> ");
        }

        if (p.Images.Count > 0)
        {
            WriteAscii(ms, "/XObject << ");
            foreach (ImageEntry im in _imageOrder)
            {
                if (p.Images.Contains(im.ResourceName))
                    WriteAscii(ms, $"/{im.ResourceName} {im.ObjId} 0 R ");
            }
            WriteAscii(ms, ">> ");
        }

        if (p.Patterns.Count > 0)
        {
            WriteAscii(ms, "/Pattern << ");
            foreach (PatternEntry pat in _patternOrder)
            {
                if (p.Patterns.Contains(pat.ResourceName))
                    WriteAscii(ms, $"/{pat.ResourceName} {pat.ObjId} 0 R ");
            }
            WriteAscii(ms, ">> ");
        }
    }

    static void WriteAscii(MemoryStream ms, string s)
    {
        byte[] b = Encoding.ASCII.GetBytes(s);
        ms.Write(b, 0, b.Length);
    }

    static void WriteBytes(MemoryStream ms, byte[] b) => ms.Write(b, 0, b.Length);
}

using System.IO;
using System.IO.Compression;
using System.Text;
using Agile.Maui.PdfGen.Api;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;
using Agile.Maui.PdfGen.Text;
using Xunit;

namespace Agile.Maui.PdfGen.Tests;

public class NewFeaturesTests
{
    static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);
    const string ArialPath = @"C:\Windows\Fonts\arial.ttf";

    // ---- 1. Larguras WinAnsi acentuadas ----

    [Fact]
    public void Accented_letters_share_base_letter_width()
    {
        StandardFont f = StandardFont.Get(PdfFontFamily.Helvetica, bold: false, italic: false);
        Assert.Equal(f.GlyphWidth('a'), f.GlyphWidth('á'));
        Assert.Equal(f.GlyphWidth('c'), f.GlyphWidth('ç'));
        Assert.Equal(f.GlyphWidth('o'), f.GlyphWidth('õ'));
        // "ação" deve medir o mesmo que "acao" (mesmos avanços base).
        var accented = new TextStyle(PdfFontFamily.Helvetica, 12f);
        Assert.Equal(accented.MeasureWidth("acao"), accented.MeasureWidth("ação"), 3);
    }

    // ---- 2. PNG embutido ----

    [Fact]
    public void Png_with_alpha_embeds_with_flate_and_smask()
    {
        byte[] png = MakeRgbaPng(2, 2, new byte[]
        {
            255, 0, 0, 255,   0, 255, 0, 128,
            0, 0, 255, 64,    255, 255, 0, 255,
        });

        // Sanidade do decodificador.
        DecodedPng decoded = PngDecoder.Decode(png);
        Assert.Equal(2, decoded.Width);
        Assert.NotNull(decoded.Alpha);

        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page => page.Content().Image(png));
        }).GeneratePdf();

        string content = AsLatin1(pdf);
        Assert.StartsWith("%PDF-1.", content);
        Assert.Contains("/Subtype /Image", content);
        Assert.Contains("/Filter /FlateDecode", content);
        Assert.Contains("/SMask", content);
        Assert.EndsWith("%%EOF\n", content);
    }

    // ---- 3. Gradiente ----

    [Fact]
    public void Linear_gradient_emits_axial_shading_pattern()
    {
        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page => page.Content()
                .Background(GradientBrush.Linear(Colors.Blue, Colors.White, 90f))
                .Padding(10f).Text("gradiente"));
        }).GeneratePdf();

        string content = AsLatin1(pdf);
        Assert.Contains("/PatternType 2", content);
        Assert.Contains("/ShadingType 2", content);
        Assert.Contains("/Pattern cs", content);
        Assert.Contains("/FunctionType 2", content);
    }

    [Fact]
    public void Radial_gradient_with_multiple_stops_emits_stitching_function()
    {
        var brush = GradientBrush.Radial(
            new GradientStop(0f, Colors.White),
            new GradientStop(0.5f, Colors.Blue),
            new GradientStop(1f, Colors.Black));

        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page => page.Content().Background(brush).Padding(10f).Text("radial"));
        }).GeneratePdf();

        string content = AsLatin1(pdf);
        Assert.Contains("/ShadingType 3", content);
        Assert.Contains("/FunctionType 3", content);   // stitching para 3 paradas
    }

    // ---- 4. Fonte TrueType embutida + Unicode ----

    [Fact]
    public void Embedded_font_loads_and_maps_glyphs()
    {
        Assert.True(File.Exists(ArialPath), "arial.ttf não encontrado.");
        EmbeddedFont font = EmbeddedFont.FromFile(ArialPath);

        Assert.True(font.NumGlyphs > 0);
        Assert.True(font.Ascent > 0f);
        Assert.NotEqual(0, font.GlyphId('A'));
        Assert.NotEqual(0, font.GlyphId('€'));      // Unicode fora do Latin-1 básico
        Assert.True(font.MeasureWidth("Olá", 12f) > 0f);
    }

    [Fact]
    public void Embedded_font_generates_type0_with_unicode()
    {
        EmbeddedFont font = EmbeddedFont.FromFile(ArialPath);

        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page => page.Content().Text("Unção € ✓").Font(font).FontSize(18f));
        }).GeneratePdf();

        string content = AsLatin1(pdf);
        Assert.Contains("/Subtype /Type0", content);
        Assert.Contains("/Encoding /Identity-H", content);
        Assert.Contains("/Subtype /CIDFontType2", content);
        Assert.Contains("/FontFile2", content);
        Assert.Contains("/ToUnicode", content);
        Assert.Contains("> Tj", content);          // string de glifos em hex
        Assert.Contains("beginbfchar", content);   // CMap ToUnicode
    }

    [Fact]
    public void Embedded_font_is_subset_and_stays_small()
    {
        EmbeddedFont font = EmbeddedFont.FromFile(ArialPath);
        long fullFontSize = new FileInfo(ArialPath).Length;

        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page => page.Content().Text("Olá mundo").Font(font));
        }).GeneratePdf();

        // Poucos glifos: o PDF deve ser uma fração do arquivo de fonte inteiro.
        Assert.True(pdf.Length < fullFontSize / 3,
            $"Subset ineficaz: pdf={pdf.Length} vs fonte={fullFontSize}");
        Assert.Contains("/FontFile2", AsLatin1(pdf));
    }

    // ---- 5. Export SVG ----

    [Fact]
    public void Svg_export_produces_valid_document()
    {
        byte[] svg = PdfDocument.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Margin(20f);
                page.Content().Column(col =>
                {
                    col.Item().Background(GradientBrush.Linear(Colors.Blue, Colors.White, 0f)).Padding(8f).Text("Título");
                    col.Item().Text("Endereço acentuado");
                });
            });
        }).GenerateSvg();

        string s = Encoding.UTF8.GetString(svg);
        Assert.StartsWith("<?xml", s);
        Assert.Contains("<svg", s);
        Assert.Contains("<text", s);
        Assert.Contains("linearGradient", s);
        Assert.Contains("Endereço acentuado", s);   // UTF-8 preserva acentos
        Assert.Contains("</svg>", s);
    }

    // ---- Auxiliar: constrói um PNG RGBA de 8 bits ----

    static byte[] MakeRgbaPng(int width, int height, byte[] rgba)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        // IHDR
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, width);
        WriteBE(ihdr, 4, height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // color type RGBA
        WriteChunk(ms, "IHDR", ihdr);

        // IDAT: scanlines com byte de filtro 0 + RGBA
        using var rawMs = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            rawMs.WriteByte(0);
            rawMs.Write(rgba, y * width * 4, width * 4);
        }
        byte[] raw = rawMs.ToArray();
        using var comp = new MemoryStream();
        using (var z = new ZLibStream(comp, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw, 0, raw.Length);
        WriteChunk(ms, "IDAT", comp.ToArray());

        WriteChunk(ms, "IEND", System.Array.Empty<byte>());
        return ms.ToArray();
    }

    static void WriteBE(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    static void WriteChunk(MemoryStream ms, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, data.Length);
        ms.Write(len);

        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        ms.Write(typeBytes);
        ms.Write(data);

        uint crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        WriteBE(crcBytes, 0, (int)crc);
        ms.Write(crcBytes);
    }

    static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        crc = UpdateCrc(crc, type);
        crc = UpdateCrc(crc, data);
        return crc ^ 0xFFFFFFFF;
    }

    static uint UpdateCrc(uint crc, byte[] bytes)
    {
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return crc;
    }
}

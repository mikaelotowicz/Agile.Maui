using System.IO;
using System.IO.Compression;
using System.Text;
using Agile.Maui.PdfGen.Api;
using Agile.Maui.PdfGen.Rendering;
using Agile.Maui.PdfGen.Text;
using Xunit;

namespace Agile.Maui.PdfGen.Tests;

/// <summary>
/// Robustez a entradas malformadas (PNG/fonte) e correção do colorspace de JPEG não-RGB.
/// Entrada corrompida deve gerar sempre exceção controlada (InvalidDataException /
/// NotSupportedException), nunca IndexOutOfRange/Overflow/OOM.
/// </summary>
public class RobustnessTests
{
    static string? FindTestFontPath()
    {
        string[] candidates =
        {
            @"C:\Windows\Fonts\arial.ttf",
            @"C:\Windows\Fonts\segoeui.ttf",
            "/System/Library/Fonts/Supplemental/Arial.ttf",
            "/Library/Fonts/Arial.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf",
        };

        foreach (string path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    // ---- 1. JPEG: número de componentes → ColorSpace correto ----

    /// <summary>JPEG sintético: SOI + (APP14 Adobe opcional) + SOF0 16x16 com Nf componentes + EOI.</summary>
    static byte[] MakeJpeg(int components, bool adobe)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0xFF, 0xD8 }); // SOI

        if (adobe)
        {
            // APP14 "Adobe": len=14 ("Adobe" + versão + flags0 + flags1 + transform)
            ms.Write(new byte[] { 0xFF, 0xEE, 0x00, 0x0E });
            ms.Write(Encoding.ASCII.GetBytes("Adobe"));
            ms.Write(new byte[] { 0x00, 0x64, 0, 0, 0, 0, 0 });
        }

        // SOF0: len = 8 + 3*Nf, precisão 8, 16x16, Nf componentes
        int sofLen = 8 + 3 * components;
        ms.Write(new byte[] { 0xFF, 0xC0, (byte)(sofLen >> 8), (byte)sofLen, 8, 0, 16, 0, 16, (byte)components });
        for (int i = 0; i < components; i++)
            ms.Write(new byte[] { (byte)(i + 1), 0x11, 0 });

        ms.Write(new byte[] { 0xFF, 0xD9 }); // EOI
        return ms.ToArray();
    }

    [Fact]
    public void TryReadJpegInfo_parses_components_and_adobe_marker()
    {
        Assert.True(ImageDecoder.TryReadJpegInfo(MakeJpeg(1, adobe: false), out int w, out int h, out int comp, out bool adobe));
        Assert.Equal(16, w);
        Assert.Equal(16, h);
        Assert.Equal(1, comp);
        Assert.False(adobe);

        Assert.True(ImageDecoder.TryReadJpegInfo(MakeJpeg(4, adobe: true), out _, out _, out comp, out adobe));
        Assert.Equal(4, comp);
        Assert.True(adobe);
    }

    [Fact]
    public void Jpeg_grayscale_embeds_as_devicegray()
    {
        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page => page.Content().Image(MakeJpeg(1, adobe: false)));
        }).GeneratePdf();

        string content = PdfTestHelpers.AsLatin1(pdf);
        Assert.Contains("/DCTDecode", content);
        Assert.Contains("/ColorSpace /DeviceGray", content);
        Assert.DoesNotContain("/ColorSpace /DeviceRGB", content);
    }

    [Fact]
    public void Jpeg_cmyk_adobe_embeds_as_devicecmyk_with_inverted_decode()
    {
        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page => page.Content().Image(MakeJpeg(4, adobe: true)));
        }).GeneratePdf();

        string content = PdfTestHelpers.AsLatin1(pdf);
        Assert.Contains("/ColorSpace /DeviceCMYK", content);
        Assert.Contains("/Decode [1 0 1 0 1 0 1 0]", content);
    }

    [Fact]
    public void Jpeg_rgb_stays_devicergb_without_decode()
    {
        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page => page.Content().Image(MakeJpeg(3, adobe: false)));
        }).GeneratePdf();

        string content = PdfTestHelpers.AsLatin1(pdf);
        Assert.Contains("/ColorSpace /DeviceRGB", content);
        Assert.DoesNotContain("/Decode [", content);
    }

    // ---- 2. PNG malformado → exceção controlada ----

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
        ms.Write(Encoding.ASCII.GetBytes(type));
        ms.Write(data);
        ms.Write(new byte[4]); // CRC não é verificado pelo decodificador
    }

    static byte[] Ihdr(int width, int height, byte bitDepth, byte colorType)
    {
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, width);
        WriteBE(ihdr, 4, height);
        ihdr[8] = bitDepth;
        ihdr[9] = colorType;
        return ihdr;
    }

    static byte[] Zlib(byte[] raw)
    {
        using var comp = new MemoryStream();
        using (var z = new ZLibStream(comp, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw, 0, raw.Length);
        return comp.ToArray();
    }

    static byte[] PngSignature() => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    [Fact]
    public void Png_with_huge_declared_dimensions_throws_invaliddata()
    {
        using var ms = new MemoryStream();
        ms.Write(PngSignature());
        WriteChunk(ms, "IHDR", Ihdr(100_000, 100_000, 8, 2));
        WriteChunk(ms, "IDAT", Zlib(new byte[300]));
        WriteChunk(ms, "IEND", System.Array.Empty<byte>());

        // Antes estourava int (height*stride) → OverflowException.
        Assert.Throws<InvalidDataException>(() => PngDecoder.Decode(ms.ToArray()));
    }

    [Fact]
    public void Png_with_truncated_idat_throws_invaliddata()
    {
        using var ms = new MemoryStream();
        ms.Write(PngSignature());
        WriteChunk(ms, "IHDR", Ihdr(4, 4, 8, 2));       // espera 4*(12+1) = 52 bytes crus
        WriteChunk(ms, "IDAT", Zlib(new byte[10]));     // só 10
        WriteChunk(ms, "IEND", System.Array.Empty<byte>());

        Assert.Throws<InvalidDataException>(() => PngDecoder.Decode(ms.ToArray()));
    }

    [Fact]
    public void Png_with_oversized_idat_throws_invaliddata()
    {
        using var ms = new MemoryStream();
        ms.Write(PngSignature());
        WriteChunk(ms, "IHDR", Ihdr(1, 1, 8, 2));       // espera 1*(3+1) = 4 bytes crus
        WriteChunk(ms, "IDAT", Zlib(new byte[1000]));   // descomprime para muito mais

        // Antes o inflate era ilimitado (expansão de zlib sem teto).
        Assert.Throws<InvalidDataException>(() => PngDecoder.Decode(ms.ToArray()));
    }

    [Fact]
    public void Png_with_negative_chunk_length_throws_invaliddata()
    {
        using var ms = new MemoryStream();
        ms.Write(PngSignature());
        WriteChunk(ms, "IHDR", Ihdr(1, 1, 8, 2));
        // Chunk desconhecido com comprimento 0xFFFFFFF4 (-12): antes o cursor não avançava (loop infinito).
        ms.Write(new byte[] { 0xFF, 0xFF, 0xFF, 0xF4 });
        ms.Write(Encoding.ASCII.GetBytes("fOOb"));
        ms.Write(new byte[4]);

        Assert.Throws<InvalidDataException>(() => PngDecoder.Decode(ms.ToArray()));
    }

    // ---- 3. Fonte TrueType malformada → exceção controlada ----

    [Fact]
    public void Empty_font_throws_invaliddata()
    {
        Assert.Throws<InvalidDataException>(() => EmbeddedFont.Load(System.Array.Empty<byte>()));
    }

    [Fact]
    public void Font_with_table_beyond_eof_throws_invaliddata()
    {
        // Diretório sintético: 1 tabela "glyf" com offset 0x40000000 (bem além do arquivo).
        var fake = new byte[28];
        fake[5] = 1; // numTables = 1
        fake[12] = (byte)'g'; fake[13] = (byte)'l'; fake[14] = (byte)'y'; fake[15] = (byte)'f';
        fake[20] = 0x40;  // offset big-endian = 0x40000000
        fake[27] = 16;    // length = 16

        Assert.Throws<InvalidDataException>(() => EmbeddedFont.Load(fake));
    }

    [Fact]
    public void Truncated_real_font_throws_controlled_exception()
    {
        string? path = FindTestFontPath();
        if (path is null)
            return;

        byte[] full = File.ReadAllBytes(path);

        // Antes: IndexOutOfRangeException / ArgumentOutOfRangeException sem controle.
        Assert.Throws<InvalidDataException>(() => EmbeddedFont.Load(full[..12]));
        Assert.Throws<InvalidDataException>(() => EmbeddedFont.Load(full[..2048]));
        Assert.Throws<InvalidDataException>(() => EmbeddedFont.Load(full[..(full.Length / 2)]));
    }

    // ---- 4. Tag de subset e FontDescriptor ----

    [Fact]
    public void Embedded_font_basefont_carries_subset_tag()
    {
        string? path = FindTestFontPath();
        if (path is null)
            return;

        EmbeddedFont font = EmbeddedFont.FromFile(path);
        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page => page.Content().Text("Olá subset").Font(font));
        }).GeneratePdf();

        string content = PdfTestHelpers.AsLatin1(pdf);
        // ISO 32000-1 §9.6.4: subset = 6 maiúsculas + '+' no BaseFont e no FontName.
        Assert.Matches(@"/BaseFont /[A-Z]{6}\+", content);
        Assert.Matches(@"/FontName /[A-Z]{6}\+", content);
    }

    [Fact]
    public void Italic_embedded_font_reports_italic_angle_and_flag()
    {
        string path = @"C:\Windows\Fonts\ariali.ttf";
        if (!File.Exists(path))
            return;

        EmbeddedFont font = EmbeddedFont.FromFile(path);
        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page => page.Content().Text("itálico").Font(font));
        }).GeneratePdf();

        string content = PdfTestHelpers.AsLatin1(pdf);
        Assert.Matches(@"/ItalicAngle -\d", content);   // Arial Italic ≈ -12 (post.italicAngle)
        Assert.Contains("/Flags 96", content);          // 32 (nonsymbolic) | 64 (itálico)
    }
}

using System.IO;
using System.Text;
using Agile.Maui.PdfGen.Rendering;
using Xunit;

namespace Agile.Maui.PdfGen.Tests;

/// <summary>Parser de cabeçalho JPEG: SOF baseline e progressivo, Nf, APP14, truncamentos.</summary>
public class JpegParserTests
{
    static byte[] MakeJpeg(byte sofMarker, int components, bool adobe, int width = 16, int height = 16)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0xFF, 0xD8 }); // SOI

        if (adobe)
        {
            ms.Write(new byte[] { 0xFF, 0xEE, 0x00, 0x0E });
            ms.Write(Encoding.ASCII.GetBytes("Adobe"));
            ms.Write(new byte[] { 0x00, 0x64, 0, 0, 0, 0, 0 });
        }

        int sofLen = 8 + 3 * components;
        ms.Write(new byte[]
        {
            0xFF, sofMarker, (byte)(sofLen >> 8), (byte)sofLen, 8,
            (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, (byte)components,
        });
        for (int i = 0; i < components; i++)
            ms.Write(new byte[] { (byte)(i + 1), 0x11, 0 });

        ms.Write(new byte[] { 0xFF, 0xD9 }); // EOI
        return ms.ToArray();
    }

    [Theory]
    [InlineData(0xC0)]   // SOF0 baseline
    [InlineData(0xC1)]   // SOF1 sequencial estendido
    [InlineData(0xC2)]   // SOF2 progressivo
    public void Sof_baseline_e_progressivo_parseiam_dimensoes(int marker)
    {
        byte[] jpeg = MakeJpeg((byte)marker, 3, adobe: false, width: 640, height: 480);

        Assert.True(ImageDecoder.TryReadJpeg(jpeg, out int w, out int h));
        Assert.Equal(640, w);
        Assert.Equal(480, h);
    }

    [Fact]
    public void Dht_antes_do_sof_e_pulado()
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0xFF, 0xD8 });
        // DHT (0xC4) com payload de 3 bytes — não pode ser confundido com SOF.
        ms.Write(new byte[] { 0xFF, 0xC4, 0x00, 0x05, 1, 2, 3 });
        byte[] rest = MakeJpeg(0xC0, 3, adobe: false);
        ms.Write(rest, 2, rest.Length - 2);   // sem o SOI duplicado

        Assert.True(ImageDecoder.TryReadJpeg(ms.ToArray(), out int w, out int h));
        Assert.Equal(16, w);
        Assert.Equal(16, h);
    }

    [Fact]
    public void Componentes_e_app14_propagados()
    {
        Assert.True(ImageDecoder.TryReadJpegInfo(MakeJpeg(0xC0, 3, adobe: false), out _, out _, out int c3, out bool a3));
        Assert.Equal(3, c3);
        Assert.False(a3);

        Assert.True(ImageDecoder.TryReadJpegInfo(MakeJpeg(0xC2, 4, adobe: true), out _, out _, out int c4, out bool a4));
        Assert.Equal(4, c4);
        Assert.True(a4);
    }

    [Fact]
    public void Jpeg_truncado_retorna_false_sem_excecao()
    {
        byte[] full = MakeJpeg(0xC0, 3, adobe: false);

        Assert.False(ImageDecoder.TryReadJpeg(full[..2], out _, out _));    // só SOI
        Assert.False(ImageDecoder.TryReadJpeg(full[..4], out _, out _));    // SOI + início do SOF
        Assert.False(ImageDecoder.TryReadJpeg(full[..9], out _, out _));    // curto demais para um SOF completo
    }

    [Fact]
    public void Entrada_que_nao_e_jpeg_retorna_false()
    {
        Assert.False(ImageDecoder.TryReadJpeg(System.Array.Empty<byte>(), out _, out _));
        Assert.False(ImageDecoder.TryReadJpeg(new byte[] { 1, 2, 3, 4 }, out _, out _));
        Assert.False(ImageDecoder.TryReadJpeg(TestImages.Rgba2x2(), out _, out _));   // é PNG
    }

    [Fact]
    public void Png_e_jpeg_sao_distinguidos_pelo_pdfimage()
    {
        Assert.Equal(ImageFormat.Png, PdfImage.FromBytes(TestImages.Rgba2x2()).Format);
        Assert.Equal(ImageFormat.Jpeg, PdfImage.FromBytes(MakeJpeg(0xC0, 3, adobe: false)).Format);
        Assert.Throws<System.NotSupportedException>(() => PdfImage.FromBytes(new byte[] { 1, 2, 3, 4, 5 }));
    }
}

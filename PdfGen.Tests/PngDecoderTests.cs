using System.IO;
using Agile.Maui.PdfGen.Rendering;
using Xunit;

namespace Agile.Maui.PdfGen.Tests;

/// <summary>
/// Decodificador PNG: matriz completa de filtros (0–4), tipos de cor suportados, paleta em
/// 1/2/4/8 bits, tRNS e rejeições explícitas (16 bits, Adam7, cinza sub-byte).
/// </summary>
public class PngDecoderTests
{
    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = System.Math.Abs(p - a);
        int pb = System.Math.Abs(p - b);
        int pc = System.Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        if (pb <= pc) return b;
        return c;
    }

    /// <summary>Aplica os filtros PNG (codificação) linha a linha: [filtro][bytes filtrados].</summary>
    static byte[] EncodeRows(byte[][] rows, int[] filters, int bpp)
    {
        using var ms = new MemoryStream();
        byte[]? prev = null;
        for (int y = 0; y < rows.Length; y++)
        {
            int f = filters[y];
            ms.WriteByte((byte)f);
            byte[] row = rows[y];
            for (int i = 0; i < row.Length; i++)
            {
                int a = i >= bpp ? row[i - bpp] : 0;
                int b = prev is not null ? prev[i] : 0;
                int c = prev is not null && i >= bpp ? prev[i - bpp] : 0;
                int pred = f switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => 0,
                };
                ms.WriteByte((byte)(row[i] - pred));
            }
            prev = row;
        }
        return ms.ToArray();
    }

    // Pixels RGB 3x3 fixos usados nos testes de filtro.
    static byte[][] RgbRows() => new[]
    {
        new byte[] { 10, 200, 30, 40, 50, 255, 70, 80, 90 },
        new byte[] { 15, 25, 35, 45, 55, 65, 75, 85, 95 },
        new byte[] { 0, 128, 255, 17, 34, 51, 68, 85, 102 },
    };

    static byte[] Flatten(byte[][] rows)
    {
        using var ms = new MemoryStream();
        foreach (byte[] r in rows)
            ms.Write(r, 0, r.Length);
        return ms.ToArray();
    }

    [Theory]
    [InlineData(0)]   // None
    [InlineData(1)]   // Sub
    [InlineData(2)]   // Up
    [InlineData(3)]   // Average
    [InlineData(4)]   // Paeth
    public void Filtro_roundtrip_recupera_os_pixels(int filter)
    {
        byte[][] rows = RgbRows();
        byte[] png = PngBuilder.Build(
            PngBuilder.Ihdr(3, 3, 8, 2),
            ("IDAT", PngBuilder.Zlib(EncodeRows(rows, new[] { filter, filter, filter }, bpp: 3))));

        DecodedPng decoded = PngDecoder.Decode(png);
        Assert.Equal(3, decoded.Width);
        Assert.Equal(3, decoded.Height);
        Assert.Equal(Flatten(rows), decoded.Rgb);
        Assert.Null(decoded.Alpha);
    }

    [Fact]
    public void Filtros_diferentes_por_scanline_roundtrip()
    {
        byte[][] rows = RgbRows();
        byte[] png = PngBuilder.Build(
            PngBuilder.Ihdr(3, 3, 8, 2),
            ("IDAT", PngBuilder.Zlib(EncodeRows(rows, new[] { 1, 4, 3 }, bpp: 3))));

        Assert.Equal(Flatten(rows), PngDecoder.Decode(png).Rgb);
    }

    [Fact]
    public void Cinza_8_bits_triplica_para_rgb_sem_alfa()
    {
        byte[] png = PngBuilder.Build(
            PngBuilder.Ihdr(3, 1, 8, 0),
            ("IDAT", PngBuilder.Zlib(new byte[] { 0, 5, 128, 250 })));

        DecodedPng decoded = PngDecoder.Decode(png);
        Assert.Equal(new byte[] { 5, 5, 5, 128, 128, 128, 250, 250, 250 }, decoded.Rgb);
        Assert.Null(decoded.Alpha);
    }

    [Fact]
    public void Cinza_com_alfa_separa_os_canais()
    {
        byte[] png = PngBuilder.Build(
            PngBuilder.Ihdr(2, 1, 8, 4),
            ("IDAT", PngBuilder.Zlib(new byte[] { 0, 100, 200, 50, 7 })));

        DecodedPng decoded = PngDecoder.Decode(png);
        Assert.Equal(new byte[] { 100, 100, 100, 50, 50, 50 }, decoded.Rgb);
        Assert.Equal(new byte[] { 200, 7 }, decoded.Alpha);
    }

    [Fact]
    public void Rgba_separa_alfa_com_valores_exatos()
    {
        DecodedPng decoded = PngDecoder.Decode(TestImages.Rgba2x2());

        Assert.Equal(new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 0 }, decoded.Rgb);
        Assert.Equal(new byte[] { 255, 128, 64, 255 }, decoded.Alpha);
    }

    static readonly byte[] Plte4 =
    {
        10, 20, 30,   40, 50, 60,   70, 80, 90,   200, 210, 220,
    };

    [Theory]
    [InlineData(8, 4, new byte[] { 0, 1, 2, 3 }, new[] { 0, 1, 2, 3 })]
    [InlineData(4, 2, new byte[] { 0x01 }, new[] { 0, 1 })]
    [InlineData(2, 4, new byte[] { 0x1B }, new[] { 0, 1, 2, 3 })]
    [InlineData(1, 8, new byte[] { 0x55 }, new[] { 0, 1, 0, 1, 0, 1, 0, 1 })]
    public void Paleta_indexa_corretamente_em_cada_bitdepth(int depth, int width, byte[] packed, int[] expected)
    {
        var raw = new byte[1 + packed.Length];
        packed.CopyTo(raw, 1);   // filtro 0 + índices empacotados

        byte[] png = PngBuilder.Build(
            PngBuilder.Ihdr(width, 1, (byte)depth, 3),
            ("PLTE", Plte4),
            ("IDAT", PngBuilder.Zlib(raw)));

        DecodedPng decoded = PngDecoder.Decode(png);
        for (int x = 0; x < width; x++)
        {
            int idx = expected[x];
            Assert.Equal(Plte4[idx * 3], decoded.Rgb[x * 3]);
            Assert.Equal(Plte4[idx * 3 + 1], decoded.Rgb[x * 3 + 1]);
            Assert.Equal(Plte4[idx * 3 + 2], decoded.Rgb[x * 3 + 2]);
        }
    }

    [Fact]
    public void Paleta_com_trns_aplica_alfa_por_indice()
    {
        byte[] png = PngBuilder.Build(
            PngBuilder.Ihdr(4, 1, 8, 3),
            ("PLTE", Plte4),
            ("tRNS", new byte[] { 255, 128, 64, 0 }),
            ("IDAT", PngBuilder.Zlib(new byte[] { 0, 0, 1, 2, 3 })));

        DecodedPng decoded = PngDecoder.Decode(png);
        Assert.Equal(new byte[] { 255, 128, 64, 0 }, decoded.Alpha);
    }

    [Fact]
    public void Trns_mais_curto_que_a_paleta_fica_opaco()
    {
        byte[] png = PngBuilder.Build(
            PngBuilder.Ihdr(4, 1, 8, 3),
            ("PLTE", Plte4),
            ("tRNS", new byte[] { 9 }),
            ("IDAT", PngBuilder.Zlib(new byte[] { 0, 0, 1, 2, 3 })));

        DecodedPng decoded = PngDecoder.Decode(png);
        Assert.Equal(new byte[] { 9, 255, 255, 255 }, decoded.Alpha);
    }

    // ---- rejeições explícitas ----

    [Fact]
    public void Png_16_bits_e_rejeitado_explicitamente()
    {
        byte[] png = PngBuilder.Build(PngBuilder.Ihdr(1, 1, 16, 2), ("IDAT", PngBuilder.Zlib(new byte[8])));
        Assert.Throws<System.NotSupportedException>(() => PngDecoder.Decode(png));
    }

    [Fact]
    public void Adam7_e_rejeitado_explicitamente()
    {
        byte[] png = PngBuilder.Build(PngBuilder.Ihdr(2, 2, 8, 2, interlace: 1), ("IDAT", PngBuilder.Zlib(new byte[16])));
        Assert.Throws<System.NotSupportedException>(() => PngDecoder.Decode(png));
    }

    [Fact]
    public void Cinza_sub_byte_e_rejeitado_explicitamente()
    {
        byte[] png = PngBuilder.Build(PngBuilder.Ihdr(2, 1, 4, 0), ("IDAT", PngBuilder.Zlib(new byte[2])));
        Assert.Throws<System.NotSupportedException>(() => PngDecoder.Decode(png));
    }
}

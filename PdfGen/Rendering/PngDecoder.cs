using System.IO;
using System.IO.Compression;

namespace Agile.Maui.PdfGen.Rendering;

/// <summary>Imagem PNG decodificada em amostras cruas RGB (+ alfa opcional), pronta para embutir no PDF.</summary>
internal readonly struct DecodedPng
{
    public readonly int Width;
    public readonly int Height;
    /// <summary>Amostras RGB entrelaçadas (Width*Height*3 bytes).</summary>
    public readonly byte[] Rgb;
    /// <summary>Amostras de alfa (Width*Height bytes) ou null quando a imagem é totalmente opaca.</summary>
    public readonly byte[]? Alpha;

    public DecodedPng(int width, int height, byte[] rgb, byte[]? alpha)
    {
        Width = width;
        Height = height;
        Rgb = rgb;
        Alpha = alpha;
    }
}

/// <summary>
/// Decodificador PNG mínimo e sem dependências: suporta profundidade de 8 bits para tons de cinza,
/// RGB, cinza+alfa e RGBA, e paleta em 1/2/4/8 bits. Não suporta 16 bits nem entrelaçamento Adam7.
/// </summary>
internal static class PngDecoder
{
    static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static DecodedPng Decode(byte[] data)
    {
        if (data.Length < 8)
            throw new InvalidDataException("PNG truncado.");
        for (int i = 0; i < 8; i++)
            if (data[i] != Signature[i])
                throw new InvalidDataException("Assinatura PNG inválida.");

        int width = 0, height = 0;
        int bitDepth = 0, colorType = 0, interlace = 0;
        byte[]? palette = null;      // RGB por índice
        byte[]? paletteAlpha = null; // alfa por índice (tRNS)
        using var idat = new MemoryStream();

        int pos = 8;
        while (pos + 8 <= data.Length)
        {
            int length = ReadBE32(data, pos);
            string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            int dataStart = pos + 8;
            if (dataStart + length > data.Length)
                break;

            switch (type)
            {
                case "IHDR":
                    width = ReadBE32(data, dataStart);
                    height = ReadBE32(data, dataStart + 4);
                    bitDepth = data[dataStart + 8];
                    colorType = data[dataStart + 9];
                    interlace = data[dataStart + 12];
                    break;
                case "PLTE":
                    palette = new byte[length];
                    System.Array.Copy(data, dataStart, palette, 0, length);
                    break;
                case "tRNS":
                    paletteAlpha = new byte[length];
                    System.Array.Copy(data, dataStart, paletteAlpha, 0, length);
                    break;
                case "IDAT":
                    idat.Write(data, dataStart, length);
                    break;
                case "IEND":
                    pos = data.Length;
                    break;
            }

            pos = dataStart + length + 4; // + CRC
        }

        if (width <= 0 || height <= 0)
            throw new InvalidDataException("PNG sem IHDR válido.");
        if (interlace != 0)
            throw new System.NotSupportedException("PNG entrelaçado (Adam7) não é suportado pelo escritor gerenciado.");
        if (bitDepth == 16)
            throw new System.NotSupportedException("PNG de 16 bits não é suportado pelo escritor gerenciado.");

        int channels = colorType switch
        {
            0 => 1, // cinza
            2 => 3, // RGB
            3 => 1, // paleta
            4 => 2, // cinza + alfa
            6 => 4, // RGBA
            _ => throw new System.NotSupportedException($"Tipo de cor PNG {colorType} não suportado."),
        };

        if (bitDepth != 8 && colorType != 3)
            throw new System.NotSupportedException("PNG só suporta 8 bits (exceto paleta, que aceita 1/2/4/8).");

        byte[] raw = Inflate(idat.ToArray());
        byte[] samples = Unfilter(raw, width, height, channels, bitDepth);

        return Compose(samples, width, height, colorType, bitDepth, channels, palette, paletteAlpha);
    }

    static byte[] Inflate(byte[] zlibData)
    {
        using var input = new MemoryStream(zlibData);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>Desfaz os filtros PNG por scanline, devolvendo bytes crus (ainda em bit depth original).</summary>
    static byte[] Unfilter(byte[] raw, int width, int height, int channels, int bitDepth)
    {
        int stride = (width * channels * bitDepth + 7) / 8; // bytes por scanline
        int bpp = System.Math.Max(1, channels * bitDepth / 8); // bytes por pixel (mín. 1)
        var outp = new byte[height * stride];

        int src = 0;
        for (int y = 0; y < height; y++)
        {
            byte filter = raw[src++];
            int rowStart = y * stride;
            int prevStart = rowStart - stride;

            for (int x = 0; x < stride; x++)
            {
                int rawVal = raw[src++];
                int a = x >= bpp ? outp[rowStart + x - bpp] : 0;        // esquerda
                int b = y > 0 ? outp[prevStart + x] : 0;                 // cima
                int c = (y > 0 && x >= bpp) ? outp[prevStart + x - bpp] : 0; // diagonal

                int val = filter switch
                {
                    0 => rawVal,
                    1 => rawVal + a,
                    2 => rawVal + b,
                    3 => rawVal + (a + b) / 2,
                    4 => rawVal + Paeth(a, b, c),
                    _ => throw new InvalidDataException($"Filtro PNG {filter} inválido."),
                };
                outp[rowStart + x] = (byte)(val & 0xFF);
            }
        }

        return outp;
    }

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

    static DecodedPng Compose(byte[] samples, int width, int height, int colorType, int bitDepth,
        int channels, byte[]? palette, byte[]? paletteAlpha)
    {
        int count = width * height;
        var rgb = new byte[count * 3];
        byte[]? alpha = null;
        int stride = (width * channels * bitDepth + 7) / 8;

        void EnsureAlpha() { alpha ??= new byte[count]; }

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * stride;
            for (int x = 0; x < width; x++)
            {
                int px = y * width + x;
                switch (colorType)
                {
                    case 0: // cinza
                    {
                        byte g = samples[rowStart + x];
                        rgb[px * 3] = g; rgb[px * 3 + 1] = g; rgb[px * 3 + 2] = g;
                        break;
                    }
                    case 2: // RGB
                    {
                        int o = rowStart + x * 3;
                        rgb[px * 3] = samples[o]; rgb[px * 3 + 1] = samples[o + 1]; rgb[px * 3 + 2] = samples[o + 2];
                        break;
                    }
                    case 3: // paleta
                    {
                        int index = ReadIndex(samples, rowStart, x, bitDepth);
                        int po = index * 3;
                        if (palette is not null && po + 2 < palette.Length)
                        {
                            rgb[px * 3] = palette[po]; rgb[px * 3 + 1] = palette[po + 1]; rgb[px * 3 + 2] = palette[po + 2];
                        }
                        if (paletteAlpha is not null)
                        {
                            EnsureAlpha();
                            alpha![px] = index < paletteAlpha.Length ? paletteAlpha[index] : (byte)255;
                        }
                        break;
                    }
                    case 4: // cinza + alfa
                    {
                        int o = rowStart + x * 2;
                        byte g = samples[o];
                        rgb[px * 3] = g; rgb[px * 3 + 1] = g; rgb[px * 3 + 2] = g;
                        EnsureAlpha();
                        alpha![px] = samples[o + 1];
                        break;
                    }
                    case 6: // RGBA
                    {
                        int o = rowStart + x * 4;
                        rgb[px * 3] = samples[o]; rgb[px * 3 + 1] = samples[o + 1]; rgb[px * 3 + 2] = samples[o + 2];
                        EnsureAlpha();
                        alpha![px] = samples[o + 3];
                        break;
                    }
                }
            }
        }

        return new DecodedPng(width, height, rgb, alpha);
    }

    /// <summary>Lê um índice de paleta (bit depth 1/2/4/8) do byte da scanline.</summary>
    static int ReadIndex(byte[] samples, int rowStart, int x, int bitDepth)
    {
        if (bitDepth == 8)
            return samples[rowStart + x];

        int perByte = 8 / bitDepth;
        int byteIndex = rowStart + x / perByte;
        int bitOffset = 8 - bitDepth - (x % perByte) * bitDepth;
        int mask = (1 << bitDepth) - 1;
        return (samples[byteIndex] >> bitOffset) & mask;
    }

    static int ReadBE32(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}

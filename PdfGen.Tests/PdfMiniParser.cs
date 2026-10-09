using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Agile.Maui.PdfGen.Tests;

/// <summary>
/// Mini-parser de PDF para os testes de conformidade: xref, streams com /Length exato,
/// content streams decodificados e leitura estrutural de fontes TrueType (sfnt).
/// Qualquer mudança futura no escritor que viole a spec quebra aqui, não no viewer.
/// </summary>
internal static class PdfMiniParser
{
    public static string AsLatin1(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    // ---- xref / trailer ----

    public static long StartXref(byte[] pdf)
    {
        Match m = Regex.Match(AsLatin1(pdf), @"startxref\n(\d+)\n%%EOF\n?$");
        if (!m.Success)
            throw new InvalidDataException("startxref não encontrado no fim do arquivo.");
        return long.Parse(m.Groups[1].Value);
    }

    public static int TrailerSize(byte[] pdf)
    {
        Match m = Regex.Match(AsLatin1(pdf), @"trailer\n<< /Size (\d+) /Root 1 0 R >>");
        if (!m.Success)
            throw new InvalidDataException("trailer com /Size e /Root não encontrado.");
        return int.Parse(m.Groups[1].Value);
    }

    /// <summary>
    /// Lê a tabela xref validando o formato: "xref\n0 N\n" + N entradas de exatamente 20 bytes.
    /// Devolve (offset, geração, tipo) por id.
    /// </summary>
    public static (long offset, int gen, char type)[] ReadXref(byte[] pdf)
    {
        long start = StartXref(pdf);
        string text = AsLatin1(pdf);
        string tail = text.Substring((int)start);

        Match header = Regex.Match(tail, @"^xref\n0 (\d+)\n");
        if (!header.Success)
            throw new InvalidDataException("cabeçalho da xref não encontrado no offset de startxref.");

        int size = int.Parse(header.Groups[1].Value);
        var entries = new (long, int, char)[size];
        int pos = header.Length;
        for (int i = 0; i < size; i++)
        {
            string line = tail.Substring(pos, 20);
            Match m = Regex.Match(line, @"^(\d{10}) (\d{5}) ([nf])\r\n$");
            if (!m.Success)
                throw new InvalidDataException($"entrada xref {i} fora do formato de 20 bytes: '{line}'");
            entries[i] = (long.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Value[0]);
            pos += 20;
        }

        if (!tail.Substring(pos).StartsWith("trailer\n", System.StringComparison.Ordinal))
            throw new InvalidDataException("trailer não segue imediatamente as entradas da xref.");

        return entries;
    }

    // ---- streams ----

    /// <summary>Todos os streams do arquivo: (dicionário, início dos dados, comprimento declarado).</summary>
    public static List<(string dict, int dataStart, int length)> Streams(byte[] pdf)
    {
        var result = new List<(string, int, int)>();
        string text = AsLatin1(pdf);
        int pos = 0;

        while (true)
        {
            int stream = text.IndexOf("stream\n", pos, System.StringComparison.Ordinal);
            if (stream < 0)
                break;
            // Ignora o token dentro de "endstream".
            if (stream >= 3 && text.Substring(stream - 3, 3) == "end")
            {
                pos = stream + 7;
                continue;
            }

            int dictStart = text.LastIndexOf("<<", stream, System.StringComparison.Ordinal);
            string dict = text.Substring(dictStart, stream - dictStart);
            Match m = Regex.Match(dict, @"/Length (\d+)(?![0-9])");
            if (!m.Success)
                throw new InvalidDataException($"stream sem /Length no dicionário: {dict}");

            int length = int.Parse(m.Groups[1].Value);
            result.Add((dict, stream + 7, length));
            pos = stream + 7 + length;
        }

        return result;
    }

    /// <summary>Content streams das páginas (FlateDecode puro), já descomprimidos.</summary>
    public static List<string> ContentStreams(byte[] pdf)
    {
        var result = new List<string>();
        foreach (var (dict, dataStart, length) in Streams(pdf))
        {
            if (!dict.Contains("/Filter /FlateDecode") || dict.Contains("/Subtype") || dict.Contains("/Length1"))
                continue;
            string content = AsLatin1(Inflate(pdf, dataStart, length));
            if (content.StartsWith("/CIDInit", System.StringComparison.Ordinal))
                continue;   // CMap ToUnicode (também FlateDecode), não é conteúdo de página
            result.Add(content);
        }
        return result;
    }

    /// <summary>CMap ToUnicode descomprimido, localizado pela referência /ToUnicode do Type0.</summary>
    public static string ToUnicode(byte[] pdf)
    {
        string text = AsLatin1(pdf);
        Match reference = Regex.Match(text, @"/ToUnicode (\d+) 0 R");
        if (!reference.Success)
            throw new InvalidDataException("/ToUnicode não referenciado por nenhuma fonte.");

        int objPos = text.IndexOf($"{reference.Groups[1].Value} 0 obj", System.StringComparison.Ordinal);
        int streamPos = text.IndexOf("stream\n", objPos, System.StringComparison.Ordinal);
        int dictStart = text.LastIndexOf("<<", streamPos, System.StringComparison.Ordinal);
        string dict = text.Substring(dictStart, streamPos - dictStart);
        int length = int.Parse(Regex.Match(dict, @"/Length (\d+)(?![0-9])").Groups[1].Value);

        return dict.Contains("/Filter /FlateDecode")
            ? AsLatin1(Inflate(pdf, streamPos + 7, length))
            : text.Substring(streamPos + 7, length);
    }

    public static string AllContent(byte[] pdf) => string.Join("\n", ContentStreams(pdf));

    /// <summary>Extrai o FontFile2 (subset TrueType) descomprimido e o /Length1 declarado.</summary>
    public static (byte[] subset, int length1) FontFile2(byte[] pdf)
    {
        foreach (var (dict, dataStart, length) in Streams(pdf))
        {
            Match m = Regex.Match(dict, @"/Length1 (\d+)");
            if (!m.Success)
                continue;
            return (Inflate(pdf, dataStart, length), int.Parse(m.Groups[1].Value));
        }
        throw new InvalidDataException("FontFile2 não encontrado.");
    }

    static byte[] Inflate(byte[] pdf, int dataStart, int length)
    {
        using var input = new MemoryStream(pdf, dataStart, length);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    // ---- leitura estrutural de sfnt/TrueType ----

    public static int U16(byte[] d, int o) => (d[o] << 8) | d[o + 1];
    public static uint U32(byte[] d, int o) =>
        ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];

    public static Dictionary<string, (int off, int len, uint checksum)> SfntDirectory(byte[] font)
    {
        int numTables = U16(font, 4);
        var dir = new Dictionary<string, (int, int, uint)>();
        for (int i = 0; i < numTables; i++)
        {
            int rec = 12 + i * 16;
            dir[Encoding.ASCII.GetString(font, rec, 4)] =
                ((int)U32(font, rec + 8), (int)U32(font, rec + 12), U32(font, rec + 4));
        }
        return dir;
    }

    /// <summary>Checksum sfnt (soma de u32 big-endian com padding zero).</summary>
    public static uint SfntChecksum(byte[] data, int offset, int length)
    {
        uint sum = 0;
        for (int i = 0; i < length; i += 4)
        {
            uint v = 0;
            for (int j = 0; j < 4; j++)
                v = (v << 8) | (uint)(i + j < length ? data[offset + i + j] : 0);
            unchecked { sum += v; }
        }
        return sum;
    }
}

/// <summary>Imagens sintéticas determinísticas compartilhadas pelos testes.</summary>
internal static class TestImages
{
    /// <summary>PNG RGBA 2x2 com alfa variado (255/128/64/255).</summary>
    public static byte[] Rgba2x2()
    {
        return PngBuilder.Build(
            PngBuilder.Ihdr(2, 2, 8, 6),
            ("IDAT", PngBuilder.Zlib(new byte[]
            {
                0, 255, 0, 0, 255,   0, 255, 0, 128,
                0, 0, 0, 255, 64,    255, 255, 0, 255,
            })));
    }
}

/// <summary>Monta PNGs sintéticos chunk a chunk (CRC zerado: o decodificador não o verifica).</summary>
internal static class PngBuilder
{
    public static byte[] Build(byte[] ihdr, params (string type, byte[] data)[] extraChunks)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        Chunk(ms, "IHDR", ihdr);
        foreach (var (type, data) in extraChunks)
            Chunk(ms, type, data);
        Chunk(ms, "IEND", System.Array.Empty<byte>());
        return ms.ToArray();
    }

    public static byte[] Ihdr(int width, int height, byte bitDepth, byte colorType, byte interlace = 0)
    {
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, width);
        WriteBE(ihdr, 4, height);
        ihdr[8] = bitDepth;
        ihdr[9] = colorType;
        ihdr[12] = interlace;
        return ihdr;
    }

    public static void Chunk(MemoryStream ms, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, data.Length);
        ms.Write(len);
        ms.Write(Encoding.ASCII.GetBytes(type));
        ms.Write(data);
        ms.Write(new byte[4]);
    }

    public static byte[] Zlib(byte[] raw)
    {
        using var comp = new MemoryStream();
        using (var z = new ZLibStream(comp, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw, 0, raw.Length);
        return comp.ToArray();
    }

    public static void WriteBE(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }
}

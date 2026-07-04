using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Agile.Maui.PdfGen.Tests;

internal static class PdfTestHelpers
{
    public static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    public static string AllText(byte[] pdf)
    {
        var sb = new StringBuilder(AsLatin1(pdf));
        sb.Append('\n');
        sb.Append(AsLatin1(DecodeStreams(pdf)));
        return sb.ToString();
    }

    public static byte[] DecodeStreams(byte[] pdf)
    {
        using var output = new MemoryStream();
        int pos = 0;

        while (true)
        {
            int stream = IndexOfStreamToken(pdf, pos);
            if (stream < 0)
                break;

            int dataStart = stream + "stream".Length;
            if (dataStart < pdf.Length && pdf[dataStart] == (byte)'\r')
                dataStart++;
            if (dataStart < pdf.Length && pdf[dataStart] == (byte)'\n')
                dataStart++;

            int dictStart = LastIndexOf(pdf, "<<", stream);
            if (dictStart < 0)
                dictStart = Math.Max(0, stream - 512);
            string dict = AsLatin1(pdf.AsSpan(dictStart, stream - dictStart).ToArray());
            Match lengthMatch = Regex.Match(dict, @"/Length\s+(\d+)");
            if (!lengthMatch.Success)
            {
                pos = dataStart;
                continue;
            }

            int length = int.Parse(lengthMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (dataStart + length > pdf.Length)
                break;

            byte[] streamBytes = pdf.AsSpan(dataStart, length).ToArray();
            if (dict.Contains("/Filter /FlateDecode", StringComparison.Ordinal))
                streamBytes = TryInflate(streamBytes);

            output.Write(streamBytes, 0, streamBytes.Length);
            output.WriteByte((byte)'\n');
            int endStream = IndexOf(pdf, "endstream", dataStart + length);
            pos = endStream > 0 ? endStream + "endstream".Length : dataStart + length;
        }

        return output.ToArray();
    }

    static byte[] TryInflate(byte[] bytes)
    {
        try
        {
            using var input = new MemoryStream(bytes);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }
        catch
        {
            return bytes;
        }
    }

    static int IndexOf(byte[] bytes, string value, int start)
    {
        byte[] needle = Encoding.ASCII.GetBytes(value);
        for (int i = start; i <= bytes.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (bytes[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return i;
        }
        return -1;
    }

    static int LastIndexOf(byte[] bytes, string value, int before)
    {
        byte[] needle = Encoding.ASCII.GetBytes(value);
        for (int i = before - needle.Length; i >= 0; i--)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (bytes[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return i;
        }
        return -1;
    }

    static int IndexOfStreamToken(byte[] bytes, int start)
    {
        int pos = start;
        while (true)
        {
            int stream = IndexOf(bytes, "stream", pos);
            if (stream < 0)
                return -1;

            bool precededByEnd = stream >= 3
                && bytes[stream - 3] == (byte)'e'
                && bytes[stream - 2] == (byte)'n'
                && bytes[stream - 1] == (byte)'d';
            int after = stream + "stream".Length;
            bool followedByEol = after < bytes.Length && (bytes[after] == (byte)'\n' || bytes[after] == (byte)'\r');

            if (!precededByEnd && followedByEol)
                return stream;

            pos = stream + 1;
        }
    }
}

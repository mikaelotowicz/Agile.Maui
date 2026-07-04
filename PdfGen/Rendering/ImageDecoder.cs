namespace Agile.Maui.PdfGen.Rendering;

/// <summary>Leitura de cabeçalhos de imagem (dimensões) sem dependências externas.</summary>
internal static class ImageDecoder
{
    public static bool TryReadPng(byte[] data, out int width, out int height)
    {
        width = 0;
        height = 0;

        // Assinatura PNG (8 bytes) + IHDR: largura/altura em big-endian nos offsets 16 e 20.
        if (data.Length < 24)
            return false;
        if (data[0] != 0x89 || data[1] != 0x50 || data[2] != 0x4E || data[3] != 0x47)
            return false;

        width = ReadBigEndianInt32(data, 16);
        height = ReadBigEndianInt32(data, 20);
        return width > 0 && height > 0;
    }

    public static bool TryReadJpeg(byte[] data, out int width, out int height)
    {
        width = 0;
        height = 0;

        // SOI
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            return false;

        int pos = 2;
        while (pos + 9 < data.Length)
        {
            // Marcadores começam com 0xFF.
            if (data[pos] != 0xFF)
            {
                pos++;
                continue;
            }

            byte marker = data[pos + 1];
            pos += 2;

            // Marcadores sem payload.
            if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7))
                continue;

            if (pos + 1 >= data.Length)
                return false;

            int segmentLength = (data[pos] << 8) | data[pos + 1];

            // SOF0..SOF15 (exceto DHT/DAC): altura/largura vêm logo após o comprimento + precisão.
            bool isStartOfFrame = marker >= 0xC0 && marker <= 0xCF
                && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;

            if (isStartOfFrame)
            {
                if (pos + 7 >= data.Length)
                    return false;
                height = (data[pos + 3] << 8) | data[pos + 4];
                width = (data[pos + 5] << 8) | data[pos + 6];
                return width > 0 && height > 0;
            }

            pos += segmentLength;
        }

        return false;
    }

    static int ReadBigEndianInt32(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}

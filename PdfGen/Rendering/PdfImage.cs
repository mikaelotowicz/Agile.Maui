namespace Agile.Maui.PdfGen.Rendering;

public enum ImageFormat
{
    Jpeg,
    Png
}

/// <summary>
/// Imagem já codificada (bytes originais do arquivo) mais dimensões em pixels.
/// O renderer decide como incorporá-la (JPEG embarca direto no PDF; PNG é decodificado quando necessário).
/// </summary>
public sealed class PdfImage
{
    public byte[] Data { get; }
    public ImageFormat Format { get; }
    public int PixelWidth { get; }
    public int PixelHeight { get; }

    public PdfImage(byte[] data, ImageFormat format, int pixelWidth, int pixelHeight)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        Format = format;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
    }

    /// <summary>Proporção largura/altura em pixels.</summary>
    public float AspectRatio => PixelHeight == 0 ? 1f : (float)PixelWidth / PixelHeight;

    /// <summary>Carrega e detecta formato/dimensões a partir dos bytes do arquivo.</summary>
    public static PdfImage FromBytes(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (ImageDecoder.TryReadJpeg(data, out int jw, out int jh))
            return new PdfImage(data, ImageFormat.Jpeg, jw, jh);

        if (ImageDecoder.TryReadPng(data, out int pw, out int ph))
            return new PdfImage(data, ImageFormat.Png, pw, ph);

        throw new NotSupportedException("Formato de imagem não suportado (use JPEG ou PNG).");
    }

    public static PdfImage FromFile(string path) => FromBytes(File.ReadAllBytes(path));
}

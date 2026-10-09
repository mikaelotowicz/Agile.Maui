namespace Agile.Maui.DeviceTests;

/// <summary>
/// Decodificação de PNG + asserts de pixels por plataforma
/// (Android: BitmapFactory; Windows: BitmapDecoder).
/// </summary>
public static class PixelAssert
{
    public sealed record DecodedImage(int Width, int Height, int[] Argb)
    {
        public (byte R, byte G, byte B) Pixel(int x, int y)
        {
            var c = Argb[y * Width + x];
            return ((byte)((c >> 16) & 0xFF), (byte)((c >> 8) & 0xFF), (byte)(c & 0xFF));
        }
    }

#if ANDROID
    public static Task<DecodedImage> DecodeAsync(Stream pngStream)
    {
        using var bmp = global::Android.Graphics.BitmapFactory.DecodeStream(pngStream)
            ?? throw new InvalidOperationException("BitmapFactory não decodificou o PNG exportado.");
        var pixels = new int[bmp.Width * bmp.Height];
        bmp.GetPixels(pixels, 0, bmp.Width, 0, 0, bmp.Width, bmp.Height);
        return Task.FromResult(new DecodedImage(bmp.Width, bmp.Height, pixels));
    }
#elif WINDOWS
    public static async Task<DecodedImage> DecodeAsync(Stream pngStream)
    {
        byte[] png;
        using (var ms = new MemoryStream())
        {
            await pngStream.CopyToAsync(ms);
            png = ms.ToArray();
        }

        using var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await ras.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(png));
        await ras.FlushAsync();
        ras.Seek(0);

        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras);
        var data = await decoder.GetPixelDataAsync(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Straight,
            new Windows.Graphics.Imaging.BitmapTransform(),
            Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
            Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
        var bytes = data.DetachPixelData();

        int w = (int)decoder.PixelWidth, h = (int)decoder.PixelHeight;
        var pixels = new int[w * h];
        for (var i = 0; i < pixels.Length; i++)
        {
            var o = i * 4; // BGRA
            pixels[i] = (bytes[o + 3] << 24) | (bytes[o + 2] << 16) | (bytes[o + 1] << 8) | bytes[o];
        }
        return new DecodedImage(w, h, pixels);
    }
#else
    public static Task<DecodedImage> DecodeAsync(Stream pngStream) =>
        throw new PlatformNotSupportedException();
#endif

    /// <summary>Conta pixels "de tinta" (escuros sobre fundo branco) num retângulo.</summary>
    public static int CountInk(DecodedImage img, int x0, int y0, int x1, int y1)
    {
        var count = 0;
        for (var y = Math.Max(0, y0); y < Math.Min(img.Height, y1); y++)
        {
            for (var x = Math.Max(0, x0); x < Math.Min(img.Width, x1); x++)
            {
                var (r, g, b) = img.Pixel(x, y);
                if (r < 128 && g < 128 && b < 128)
                    count++;
            }
        }
        return count;
    }
}

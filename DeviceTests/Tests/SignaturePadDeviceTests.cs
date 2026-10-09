using Xunit;

namespace Agile.Maui.DeviceTests;

/// <summary>
/// Regressão do bug crítico do <c>canvas.Scale</c> no export do SignaturePad:
/// antes do fix, o PlatformBitmapExportContext não aplicava a escala e todo o conteúdo
/// caía no quadrante superior-esquerdo do bitmap exportado.
/// </summary>
public static class SignaturePadDeviceTests
{
    [DeviceFact(Platforms.All, TimeoutSeconds = 90)]
    public static async Task SignaturePad_Exporta_Tinta_Nos_Quatro_Quadrantes_Com_Escala_2x(TestHost host)
    {
        var pad = new SignaturePad
        {
            WidthRequest = 300,
            HeightRequest = 200,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        await host.MountAsync(pad);

        var w = (float)pad.Width;
        var h = (float)pad.Height;
        Assert.True(w > 100 && h > 100, $"pad sem tamanho real após layout: {w}x{h}");

        // Um traço em cada canto do canvas, injetado pelas APIs internas de toque
        // (mesmo caminho dos interops nativos).
        double t = 0;
        DrawLine(pad, 10, 10, 50, 40, ref t);                     // superior-esquerdo
        DrawLine(pad, w - 50, 10, w - 10, 40, ref t);             // superior-direito
        DrawLine(pad, 10, h - 40, 50, h - 10, ref t);             // inferior-esquerdo
        DrawLine(pad, w - 50, h - 40, w - 10, h - 10, ref t);     // inferior-direito
        await host.PumpAsync();

        Assert.False(pad.IsEmpty, "pad deveria ter strokes após a injeção de toques");

        var options = new SignatureExportOptions
        {
            CropToContent = false,          // exporta a área toda → dimensões previsíveis
            Scale = 2.0,
            BackgroundColor = Colors.White, // fundo branco → tinta preta detectável
        };

        using var stream = await pad.GetImageStreamAsync(SignatureImageFormat.Png, options);
        var img = await PixelAssert.DecodeAsync(stream);

        // Dimensões = bounds × 2 (mesma aritmética do export: ceil(bounds * scale)).
        var expectedW = (int)Math.Ceiling(w * 2f);
        var expectedH = (int)Math.Ceiling(h * 2f);
        Assert.True(img.Width == expectedW && img.Height == expectedH,
            $"dimensões exportadas {img.Width}x{img.Height}; esperado {expectedW}x{expectedH} (bounds {w}x{h} × 2)");

        // Tinta nos 4 quadrantes — antes do fix, tudo caía no superior-esquerdo.
        var midX = img.Width / 2;
        var midY = img.Height / 2;
        var tl = PixelAssert.CountInk(img, 0, 0, midX, midY);
        var tr = PixelAssert.CountInk(img, midX, 0, img.Width, midY);
        var bl = PixelAssert.CountInk(img, 0, midY, midX, img.Height);
        var br = PixelAssert.CountInk(img, midX, midY, img.Width, img.Height);

        Assert.True(tl > 0 && tr > 0 && bl > 0 && br > 0,
            $"tinta por quadrante TL={tl} TR={tr} BL={bl} BR={br} — todos deveriam ser > 0 " +
            "(regressão do canvas.Scale: conteúdo colapsado no quadrante superior-esquerdo)");
    }

    /// <summary>Injeta um traço reto com 8 amostras intermediárias e timestamps crescentes.</summary>
    private static void DrawLine(SignaturePad pad, float x0, float y0, float x1, float y1, ref double t)
    {
        const int steps = 8;
        pad.OnTouchDown(x0, y0, pressure: 0.5f, pressureSupported: false, timestampMs: t);
        for (var i = 1; i < steps; i++)
        {
            t += 16;
            var f = i / (float)steps;
            pad.OnTouchMove(x0 + (x1 - x0) * f, y0 + (y1 - y0) * f, 0.5f, false, t);
        }
        t += 16;
        pad.OnTouchUp(x1, y1, 0.5f, false, t);
        t += 50; // pausa entre traços
    }
}

using Microsoft.Maui.Graphics;
using Xunit;

namespace Agile.Maui.SignaturePadTests;

public class ExportTests
{
    // Contrato do TFM neutro: sem backend de bitmap da plataforma, o export falha
    // com PlatformNotSupportedException em vez de quebrar de forma obscura.

    [Fact]
    public async Task GetImageStreamAsync_no_host_lanca_PlatformNotSupportedException()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10));

        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => pad.GetImageStreamAsync());
    }

    [Fact]
    public async Task GetImageStreamAsync_jpeg_tambem_lanca_no_host()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10));

        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => pad.GetImageStreamAsync(SignatureImageFormat.Jpeg));
    }

    [Fact]
    public async Task GetImageStreamAsync_sem_strokes_tambem_lanca_no_host()
    {
        var pad = new SignaturePad();

        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => pad.GetImageStreamAsync());
    }

    [Fact]
    public async Task GetImageStreamAsync_com_opcoes_customizadas_tambem_lanca_no_host()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10));

        var options = new SignatureExportOptions
        {
            CropToContent = false,
            Scale = 1.0,
            BackgroundColor = Colors.White,
            StrokeColorOverride = Colors.Black,
        };

        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => pad.GetImageStreamAsync(SignatureImageFormat.Png, options));
    }

    [Fact]
    public void SignatureExportOptions_tem_defaults_documentados()
    {
        var options = new SignatureExportOptions();

        Assert.True(options.CropToContent);
        Assert.Equal(16.0, options.Padding);
        Assert.Equal(2.0, options.Scale);
        Assert.Null(options.BackgroundColor);
        Assert.Null(options.StrokeColorOverride);
        Assert.Equal(0.9f, options.JpegQuality);
    }
}

using System.IO;
using Xunit;

namespace Agile.Maui.GalleryTests;

/// <summary>
/// Regressão (detalhe do produto no app, 21/10): trocar a miniatura pelo original recriava as páginas, e
/// no Android a foto sumia por alguns quadros até a nova decodificar — a galeria piscava. Com a mesma
/// quantidade de fotos, a troca tem de manter as páginas. O código de plataforma não roda no host, então
/// o teste inspeciona o fonte.
/// </summary>
public class GaleriaTrocaFonteSemRecriarTests
{
    static string Fonte(params string[] partes)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Agile.Maui.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, "GalleryView", "Platforms", .. partes]));
    }

    [Theory]
    [InlineData("Android")]
    [InlineData("iOS")]
    [InlineData("MacCatalyst")]
    public void Troca_das_imagens_passa_pela_troca_sem_recriar(string plataforma)
    {
        var fonte = Fonte(plataforma, "GalleryView", "GalleryViewHandler.cs");

        Assert.Contains("[nameof(GalleryView.Images)]        = (h, _) => h.ReplaceImages()", fonte);
        Assert.Contains("MainThread.BeginInvokeOnMainThread(ReplaceImages)", fonte);
    }

    [Fact]
    public void Android_rebinda_com_a_foto_anterior_segurando_a_pagina()
    {
        var handler = Fonte("Android", "GalleryView", "GalleryViewHandler.cs");
        var loader = Fonte("Android", "AndroidImageLoader.cs");

        Assert.Contains("NotifyItemChanged(i, ReplacePayload)", handler);
        Assert.Contains("currentSource: _replacedImages[position]", handler);
        Assert.Contains("request.Thumbnail(Request(imageView, currentSource, legacyIsUrl).Apply(options))", loader);
    }

    [Theory]
    [InlineData("iOS")]
    [InlineData("MacCatalyst")]
    public void Apple_nao_poe_o_placeholder_sobre_a_foto_atual(string plataforma)
    {
        var fonte = Fonte(plataforma, "GalleryView", "GalleryViewHandler.cs").Replace("\r\n", "\n");

        Assert.Contains("if (entry.ImageView.Image is null)\n                ApplyPlaceholder(entry.ImageView);", fonte);
    }
}

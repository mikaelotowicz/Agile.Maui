using Microsoft.Maui.Hosting;
using Xunit;

namespace Agile.Maui.GalleryTests;

public class GalleryViewAppBuilderExtensionsTests
{
    [Fact]
    public void UseAgileGalleryView_devolve_o_mesmo_builder_para_encadeamento()
    {
        var builder = MauiApp.CreateBuilder(useDefaults: false);

        var retorno = builder.UseAgileGalleryView();

        Assert.Same(builder, retorno);
    }

    [Fact]
    public void UseAgileGalleryView_pode_ser_chamado_mais_de_uma_vez_sem_lancar()
    {
        var builder = MauiApp.CreateBuilder(useDefaults: false);

        builder.UseAgileGalleryView().UseAgileGalleryView();
    }
}

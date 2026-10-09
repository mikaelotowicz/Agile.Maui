using Microsoft.Maui.Hosting;
using Xunit;

namespace Agile.Maui.VirtualizedCollectionTests;

public class AppBuilderExtensionsTests
{
    [Fact]
    public void UseAgileVirtualizedCollectionView_retorna_o_mesmo_builder_e_nao_lanca()
    {
        var builder = MauiApp.CreateBuilder(useDefaults: false);

        var retorno = builder.UseAgileVirtualizedCollectionView();

        Assert.Same(builder, retorno);
    }
}

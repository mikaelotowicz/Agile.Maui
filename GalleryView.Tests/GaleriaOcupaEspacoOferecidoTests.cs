using System.IO;
using Xunit;

namespace Agile.Maui.GalleryTests;

/// <summary>
/// Regressão (detalhe do produto no app, 09/10): com MinimumHeightRequest e MaximumHeightRequest e sem
/// HeightRequest, o Android ocupava o máximo e o iOS ficava no mínimo, porque a ThumbGalleryView não
/// respondia ao SizeThatFits; com AspectFit, a foto deitada ganhava faixa dos lados só no iOS. O código
/// de plataforma não roda no host, então o teste inspeciona o fonte.
/// </summary>
public class GaleriaOcupaEspacoOferecidoTests
{
    static string GalleryViewDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Agile.Maui.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "GalleryView");
    }

    [Theory]
    [InlineData("iOS")]
    [InlineData("MacCatalyst")]
    public void ThumbGalleryView_responde_ao_SizeThatFits_com_o_espaco_oferecido(string plataforma)
    {
        var fonte = File.ReadAllText(Path.Combine(GalleryViewDir(), "Platforms", plataforma, "GalleryView", "GalleryViewHandler.cs"));

        var inicio = fonte.IndexOf("class ThumbGalleryView", StringComparison.Ordinal);
        Assert.True(inicio >= 0, $"ThumbGalleryView não encontrada em {plataforma}");
        var fim = fonte.IndexOf("\ninternal sealed class", inicio, StringComparison.Ordinal);
        var classe = fim < 0 ? fonte[inicio..] : fonte[inicio..fim];

        var metodo = classe.IndexOf("public override CGSize SizeThatFits(CGSize size)", StringComparison.Ordinal);
        Assert.True(metodo >= 0, $"ThumbGalleryView sem SizeThatFits em {plataforma}");

        var corpo = classe[metodo..classe.IndexOf("\n    }", metodo, StringComparison.Ordinal)];
        Assert.Contains(": size.Width", corpo);
        Assert.Contains(": size.Height", corpo);
    }
}

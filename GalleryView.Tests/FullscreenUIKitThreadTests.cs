using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Agile.Maui.GalleryTests;

/// <summary>
/// Regressão (encontrada no E2E no simulador iOS): os fullscreens liam UIScreen.MainScreen.Scale depois
/// do ConfigureAwait(false) do download. Fora da main thread, o UIKit lança UIKitThreadAccessException em
/// Debug e o catch trocava a foto pelo placeholder. O código de plataforma não roda no host, então o teste
/// inspeciona o fonte: em LoadFromUrlAsync, nada de UIKit depois do primeiro ConfigureAwait(false).
/// </summary>
public class FullscreenUIKitThreadTests
{
    static string GalleryViewDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Agile.Maui.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "GalleryView");
    }

    public static TheoryData<string> Controladores => new()
    {
        Path.Combine("Platforms", "iOS", "ImageView", "FullscreenZoomViewController.cs"),
        Path.Combine("Platforms", "iOS", "GalleryView", "FullscreenGalleryViewController.cs"),
        Path.Combine("Platforms", "MacCatalyst", "ImageView", "FullscreenZoomViewController.cs"),
        Path.Combine("Platforms", "MacCatalyst", "GalleryView", "FullscreenGalleryViewController.cs"),
    };

    [Theory]
    [MemberData(nameof(Controladores))]
    public void LoadFromUrlAsync_nao_acessa_UIKit_depois_do_ConfigureAwait_false(string relativo)
    {
        var fonte = File.ReadAllText(Path.Combine(GalleryViewDir(), relativo));

        var inicio = fonte.IndexOf("async Task LoadFromUrlAsync(", StringComparison.Ordinal);
        Assert.True(inicio >= 0, $"LoadFromUrlAsync não encontrado em {relativo}");
        // Corpo do método: até a próxima declaração de membro com o mesmo recuo.
        var fim = Regex.Match(fonte[(inicio + 1)..], @"\n    (private|public|internal|protected) ").Index + inicio + 1;
        var corpo = fonte[inicio..fim];

        // A chamada (com o ponto), não menções em comentários.
        var await = corpo.IndexOf(".ConfigureAwait(false)", StringComparison.Ordinal);
        Assert.True(await >= 0, $"ConfigureAwait(false) não encontrado em {relativo}");

        var depois = corpo[await..];
        Assert.DoesNotContain("UIScreen.", depois);
        Assert.DoesNotContain(".Bounds", depois);
    }
}

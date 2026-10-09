using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Agile.Maui.VirtualizedCollectionTests;

/// <summary>
/// Regressão (E2E no app, 09/10, Android): trocar ItemTemplate + Span na mesma ação (catálogo lista→grade)
/// fechava o app com IllegalStateException ("child already has a parent"). O reload é adiado no dispatcher;
/// a troca de Span recria o LayoutManager com o adapter antigo, que descarta o holder do Header com a view
/// nativa do consumidor ainda presa ao host dele, e o holder seguinte tentava adicioná-la de novo.
/// O código de plataforma não roda no host, então o teste inspeciona o fonte: CreateStructuralViewHolder
/// solta a view nativa do pai anterior antes de pendurá-la no novo host.
/// </summary>
public class HeaderReaproveitadoAndroidTests
{
    static string HandlerAndroid()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Agile.Maui.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "VirtualizedCollectionView", "Platforms", "Android",
            "VirtualizedCollectionView", "VirtualizedCollectionViewHandler.cs");
    }

    [Fact]
    public void CreateStructuralViewHolder_solta_a_view_nativa_do_host_anterior_antes_de_reaproveitar()
    {
        var fonte = File.ReadAllText(HandlerAndroid());

        var inicio = fonte.IndexOf("RecyclerView.ViewHolder CreateStructuralViewHolder(", StringComparison.Ordinal);
        Assert.True(inicio >= 0, "CreateStructuralViewHolder não encontrado");
        // Corpo do método: até a próxima declaração de membro com o mesmo recuo.
        var fim = Regex.Match(fonte[(inicio + 1)..], @"\n    (private|public|internal|protected) ").Index + inicio + 1;
        var corpo = fonte[inicio..fim];

        var toPlatform = corpo.IndexOf(".ToPlatform(", StringComparison.Ordinal);
        var remove = Regex.Match(corpo, @"\.RemoveView\(nativeView\)");
        var addView = corpo.IndexOf(".AddView(nativeView", StringComparison.Ordinal);
        var raizDireta = corpo.IndexOf("itemRoot = nativeView", StringComparison.Ordinal);

        Assert.True(toPlatform >= 0, "ToPlatform não encontrado");
        Assert.True(remove.Success, "a view nativa do Header/Footer não é solta do pai anterior");
        Assert.True(remove.Index > toPlatform, "RemoveView precisa vir depois do ToPlatform");
        Assert.True(addView > remove.Index, "RemoveView precisa vir antes do AddView no host");
        Assert.True(raizDireta > remove.Index, "RemoveView precisa vir antes de a view virar a raiz do holder");
    }
}

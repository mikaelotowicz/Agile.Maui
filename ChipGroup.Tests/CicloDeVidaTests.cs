using System.Collections.ObjectModel;
using Xunit;
using static Agile.Maui.ChipGroupTests.ChipTestHelpers;

namespace Agile.Maui.ChipGroupTests;

public class TeardownHandlerTests
{
    [Fact]
    public void Perder_o_handler_para_de_reagir_a_colecao_e_aos_ChipItems()
    {
        var chipA = new ChipItem { Text = "A", Value = "a" };
        var items = new ObservableCollection<ChipItem> { chipA };
        var group = new ChipGroup { ItemsSource = items };
        group.Handler = new StubViewHandler();
        Assert.Single(GetChips(group));

        group.Handler = null; // página saiu de cena

        items.Add(new ChipItem { Text = "B", Value = "b" });
        Assert.Single(GetChips(group)); // coleção dessinscrita: nenhum rebuild fantasma

        chipA.IsSelected = true;
        Assert.Null(group.SelectedItem); // PropertyChanged do ChipItem dessinscrito
    }

    [Fact]
    public void Reatachar_o_handler_reassina_rebuilda_e_recaptura_o_estado()
    {
        var chipA = new ChipItem { Text = "A", Value = "a" };
        var items = new ObservableCollection<ChipItem> { chipA };
        var group = new ChipGroup { ItemsSource = items };
        group.Handler = new StubViewHandler();
        group.Handler = null;

        // Mudanças feitas enquanto o controle estava fora de cena:
        items.Add(new ChipItem { Text = "B", Value = "b" });
        chipA.IsSelected = true;

        group.Handler = new StubViewHandler(); // re-attach

        Assert.Equal(2, GetChips(group).Count);                 // rebuild com o estado atual
        Assert.Equal("a", group.SelectedItem);                  // sync recaptura o IsSelected mudado offline
        Assert.Equal("A", GetLabel(GetChips(group)[0]).Text);

        items.Add(new ChipItem { Text = "C", Value = "c" });    // coleção reassinada volta a reagir
        Assert.Equal(3, GetChips(group).Count);
    }

    [Fact]
    public void Attach_e_detach_repetidos_mantem_o_controle_funcional_e_coalescido()
    {
        var items = new ObservableCollection<ChipItem> { new() { Text = "A", Value = "a" } };
        var group = new ChipGroup { ItemsSource = items };

        for (var i = 0; i < 3; i++)
        {
            group.Handler = new StubViewHandler();
            group.Handler = null;
        }

        group.Handler = new StubViewHandler();

        using var scope = Enqueue();
        items.Add(new ChipItem { Text = "B", Value = "b" });

        // Após vários ciclos attach/detach o controle continua reagindo e coalescindo num único callback.
        Assert.Equal(1, scope.Dispatcher.PendingCount);
        scope.Dispatcher.ProcessQueue();
        Assert.Equal(2, GetChips(group).Count);
    }
}

public class TrocaDeItemsSourceTests
{
    [Fact]
    public void Trocar_ItemsSource_dessinscreve_a_colecao_antiga()
    {
        var antiga = new ObservableCollection<ChipItem>
        {
            new() { Text = "A", Value = "a" },
            new() { Text = "B", Value = "b" },
        };
        var nova = new ObservableCollection<ChipItem> { new() { Text = "X", Value = "x" } };
        var group = new ChipGroup { ItemsSource = antiga };
        Assert.Equal(2, GetChips(group).Count);

        group.ItemsSource = nova;
        Assert.Single(GetChips(group));

        var excecao = Record.Exception(() => antiga.Add(new ChipItem { Text = "C", Value = "c" }));

        Assert.Null(excecao);
        Assert.Single(GetChips(group)); // a coleção antiga não afeta mais o controle
        Assert.Equal("X", GetLabel(GetChips(group)[0]).Text);
    }

    [Fact]
    public void Trocar_ItemsSource_dessinscreve_os_ChipItems_antigos()
    {
        var chipAntigo = new ChipItem { Text = "A", Value = "a" };
        var group = new ChipGroup { ItemsSource = new ObservableCollection<ChipItem> { chipAntigo } };

        group.ItemsSource = new ObservableCollection<ChipItem> { new() { Text = "X", Value = "x" } };

        chipAntigo.IsSelected = true; // não pertence mais ao grupo

        Assert.Null(group.SelectedItem);
    }

    [Fact]
    public void ItemsSource_nulo_limpa_os_chips()
    {
        var group = new ChipGroup { ItemsSource = new[] { "um", "dois" } };
        Assert.Equal(2, GetChips(group).Count);

        group.ItemsSource = null;

        Assert.Empty(GetLayout(group).Children);
    }

    [Fact]
    public void A_nova_colecao_passa_a_ser_observada()
    {
        var nova = new ObservableCollection<ChipItem> { new() { Text = "X", Value = "x" } };
        var group = new ChipGroup { ItemsSource = new ObservableCollection<ChipItem>() };

        group.ItemsSource = nova;
        nova.Add(new ChipItem { Text = "Y", Value = "y" });

        Assert.Equal(2, GetChips(group).Count);
    }
}

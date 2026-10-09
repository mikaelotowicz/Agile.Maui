using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Xunit;
using static Agile.Maui.ChipGroupTests.ChipTestHelpers;

namespace Agile.Maui.ChipGroupTests;

public class SelecaoSingleTests
{
    private static ObservableCollection<ChipItem> TresItens() =>
    [
        new() { Text = "A", Value = "a" },
        new() { Text = "B", Value = "b" },
        new() { Text = "C", Value = "c" },
    ];

    [Fact]
    public void Tap_seleciona_o_chip_e_atualiza_SelectedItem_e_SelectedItems()
    {
        var items = TresItens();
        var group = new ChipGroup { ItemsSource = items };

        Tap(GetChips(group)[1]);

        Assert.Equal("b", group.SelectedItem);
        Assert.Equal(new object?[] { "b" }, group.SelectedItems!.Cast<object?>().ToArray());
        Assert.True(items[1].IsSelected);
        Assert.False(items[0].IsSelected);
        Assert.False(items[2].IsSelected);
    }

    [Fact]
    public void Tap_em_outro_chip_troca_a_selecao_de_forma_exclusiva()
    {
        var items = TresItens();
        var group = new ChipGroup { ItemsSource = items };

        Tap(GetChips(group)[0]);
        Tap(GetChips(group)[2]);

        Assert.Equal("c", group.SelectedItem);
        Assert.False(items[0].IsSelected);
        Assert.True(items[2].IsSelected);
        Assert.Single(group.SelectedItems!.Cast<object?>());
    }

    [Fact]
    public void Retap_no_chip_ja_selecionado_nao_redispara_evento_nem_command()
    {
        var items = TresItens();
        var group = new ChipGroup { ItemsSource = items };
        var eventos = new List<ChipSelectionChangedEventArgs>();
        var execucoesCommand = 0;
        group.SelectionChanged += (_, e) => eventos.Add(e);
        group.SelectionChangedCommand = new Command(_ => execucoesCommand++);

        Tap(GetChips(group)[1]);
        Tap(GetChips(group)[1]); // re-tap no mesmo chip (árvore já rebuildada)

        Assert.Single(eventos);
        Assert.Equal(1, execucoesCommand);
        Assert.Equal("b", group.SelectedItem);
        Assert.True(items[1].IsSelected);
    }

    [Fact]
    public void SelectionChanged_entrega_args_com_SelectedItem_e_SelectedItems_corretos()
    {
        var group = new ChipGroup { ItemsSource = TresItens() };
        ChipSelectionChangedEventArgs? args = null;
        group.SelectionChanged += (_, e) => args = e;

        Tap(GetChips(group)[0]);

        Assert.NotNull(args);
        Assert.Equal("a", args!.SelectedItem);
        Assert.Equal(new object?[] { "a" }, args.SelectedItems);
    }

    [Fact]
    public void SelectionChangedCommand_recebe_os_mesmos_args_do_evento()
    {
        var group = new ChipGroup { ItemsSource = TresItens() };
        object? parametro = null;
        group.SelectionChangedCommand = new Command(p => parametro = p);

        Tap(GetChips(group)[2]);

        var args = Assert.IsType<ChipSelectionChangedEventArgs>(parametro);
        Assert.Equal("c", args.SelectedItem);
    }

    [Fact]
    public void Tap_com_itens_string_sem_ChipItem_seleciona_por_valor()
    {
        var group = new ChipGroup { ItemsSource = new[] { "um", "dois", "tres" } };

        Tap(GetChips(group)[1]);

        Assert.Equal("dois", group.SelectedItem);

        Tap(GetChips(group)[0]);

        Assert.Equal("um", group.SelectedItem);
        Assert.Equal(new object?[] { "um" }, group.SelectedItems!.Cast<object?>().ToArray());
    }
}

public class SelecaoMultipleTests
{
    private static ObservableCollection<ChipItem> TresItens() =>
    [
        new() { Text = "A", Value = "a" },
        new() { Text = "B", Value = "b" },
        new() { Text = "C", Value = "c" },
    ];

    [Fact]
    public void Taps_acumulam_selecao_em_Multiple()
    {
        var items = TresItens();
        var group = new ChipGroup { SelectionMode = ChipSelectionMode.Multiple, ItemsSource = items };

        Tap(GetChips(group)[0]);
        Tap(GetChips(group)[2]);

        Assert.Equal(new object?[] { "a", "c" }, group.SelectedItems!.Cast<object?>().ToArray());
        Assert.True(items[0].IsSelected);
        Assert.True(items[2].IsSelected);
        Assert.False(items[1].IsSelected);
    }

    [Fact]
    public void Tap_em_chip_selecionado_desseleciona_em_Multiple()
    {
        var items = TresItens();
        var group = new ChipGroup { SelectionMode = ChipSelectionMode.Multiple, ItemsSource = items };

        Tap(GetChips(group)[0]);
        Tap(GetChips(group)[2]);
        Tap(GetChips(group)[0]); // toggle off

        Assert.Equal(new object?[] { "c" }, group.SelectedItems!.Cast<object?>().ToArray());
        Assert.Equal("c", group.SelectedItem);
        Assert.False(items[0].IsSelected);
    }

    [Fact]
    public void Desselecionar_tudo_deixa_SelectedItem_nulo_e_SelectedItems_vazio()
    {
        var group = new ChipGroup { SelectionMode = ChipSelectionMode.Multiple, ItemsSource = TresItens() };

        Tap(GetChips(group)[1]);
        Tap(GetChips(group)[1]);

        Assert.Null(group.SelectedItem);
        Assert.Empty(group.SelectedItems!.Cast<object?>());
    }

    [Fact]
    public void Transicao_Multiple_para_Single_normaliza_mantendo_so_o_primeiro_selecionado()
    {
        var items = TresItens();
        var group = new ChipGroup { SelectionMode = ChipSelectionMode.Multiple, ItemsSource = items };
        Tap(GetChips(group)[0]);
        Tap(GetChips(group)[2]);

        group.SelectionMode = ChipSelectionMode.Single;

        Assert.True(items[0].IsSelected);
        Assert.False(items[2].IsSelected);
        Assert.Equal("a", group.SelectedItem);
        Assert.Equal(new object?[] { "a" }, group.SelectedItems!.Cast<object?>().ToArray());
    }

    [Fact]
    public void Transicao_Multiple_para_Single_sem_selecao_fica_sem_selecao()
    {
        var items = TresItens();
        var group = new ChipGroup { SelectionMode = ChipSelectionMode.Multiple, ItemsSource = items };

        group.SelectionMode = ChipSelectionMode.Single;

        Assert.Null(group.SelectedItem);
        Assert.All(items, item => Assert.False(item.IsSelected));
    }

    [Fact]
    public void Taps_em_Multiple_com_itens_string_alternam_valores_sem_ChipItem()
    {
        var group = new ChipGroup
        {
            SelectionMode = ChipSelectionMode.Multiple,
            ItemsSource = new[] { "um", "dois" },
        };

        Tap(GetChips(group)[0]);
        Tap(GetChips(group)[1]);

        Assert.Equal(new object?[] { "um", "dois" }, group.SelectedItems!.Cast<object?>().ToArray());

        Tap(GetChips(group)[0]);

        Assert.Equal(new object?[] { "dois" }, group.SelectedItems!.Cast<object?>().ToArray());
    }
}

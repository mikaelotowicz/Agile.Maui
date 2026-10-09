using System.Collections;
using System.Collections.ObjectModel;
using Xunit;
using static Agile.Maui.ChipGroupTests.ChipTestHelpers;

namespace Agile.Maui.ChipGroupTests;

public class SelecaoInicialTests
{
    [Fact]
    public void ChipItem_pre_selecionado_popula_SelectedItem_e_SelectedItems_sem_toque()
    {
        var items = new ObservableCollection<ChipItem>
        {
            new() { Text = "Segunda", Value = "seg" },
            new() { Text = "Quinta", Value = "qui", IsSelected = true },
            new() { Text = "Sexta", Value = "sex" },
        };

        var group = new ChipGroup { ItemsSource = items };

        Assert.Equal("qui", group.SelectedItem);
        Assert.Equal(new object?[] { "qui" }, group.SelectedItems!.Cast<object?>().ToArray());
    }

    [Fact]
    public void Varios_ChipItem_pre_selecionados_em_Multiple_populam_SelectedItems()
    {
        var items = new ObservableCollection<ChipItem>
        {
            new() { Text = "Foto", Value = "foto", IsSelected = true },
            new() { Text = "Video", Value = "video" },
            new() { Text = "Musica", Value = "musica", IsSelected = true },
        };

        var group = new ChipGroup { SelectionMode = ChipSelectionMode.Multiple, ItemsSource = items };

        Assert.Equal(new object?[] { "foto", "musica" }, group.SelectedItems!.Cast<object?>().ToArray());
        Assert.Equal("musica", group.SelectedItem); // Multiple: último selecionado na ordem da fonte
    }

    [Fact]
    public void Sem_pre_selecao_SelectedItem_continua_nulo()
    {
        var items = new ObservableCollection<ChipItem> { new() { Text = "A", Value = "a" } };

        var group = new ChipGroup { ItemsSource = items };

        Assert.Null(group.SelectedItem);
    }
}

public class TwoWayVmParaControleTests
{
    private static ObservableCollection<ChipItem> TresItens() =>
    [
        new() { Text = "A", Value = "a" },
        new() { Text = "B", Value = "b" },
        new() { Text = "C", Value = "c" },
    ];

    [Fact]
    public void Setar_SelectedItem_em_Single_propaga_para_ChipItem_IsSelected()
    {
        var items = TresItens();
        var group = new ChipGroup { ItemsSource = items };

        group.SelectedItem = "b";

        Assert.True(items[1].IsSelected);
        Assert.False(items[0].IsSelected);
        Assert.False(items[2].IsSelected);
    }

    [Fact]
    public void Trocar_SelectedItem_pela_VM_move_a_selecao_entre_ChipItems()
    {
        var items = TresItens();
        var group = new ChipGroup { ItemsSource = items };

        group.SelectedItem = "a";
        group.SelectedItem = "c";

        Assert.False(items[0].IsSelected);
        Assert.True(items[2].IsSelected);
    }

    [Fact]
    public void Setar_SelectedItems_em_Multiple_propaga_para_ChipItem_IsSelected()
    {
        var items = TresItens();
        var group = new ChipGroup { SelectionMode = ChipSelectionMode.Multiple, ItemsSource = items };

        group.SelectedItems = new List<object?> { "a", "c" };

        Assert.True(items[0].IsSelected);
        Assert.False(items[1].IsSelected);
        Assert.True(items[2].IsSelected);
    }

    [Fact]
    public void Setar_selecao_pela_VM_nao_entra_em_loop_de_reentrancia()
    {
        var items = TresItens();
        var group = new ChipGroup { SelectionMode = ChipSelectionMode.Multiple, ItemsSource = items };

        // Se houvesse loop VM→ChipItem→sync→VM, estas chamadas estourariam a pilha.
        group.SelectedItems = new List<object?> { "a" };
        group.SelectedItems = new List<object?> { "b", "c" };
        group.SelectedItem = "b";

        Assert.True(items[1].IsSelected);
        Assert.True(items[2].IsSelected);
    }

    [Fact]
    public void IsSelected_mudado_na_VM_reflete_em_SelectedItem()
    {
        var items = TresItens();
        var group = new ChipGroup { ItemsSource = items };

        items[2].IsSelected = true;

        Assert.Equal("c", group.SelectedItem);
        Assert.Equal(new object?[] { "c" }, group.SelectedItems!.Cast<object?>().ToArray());
    }
}

public class SelectedItemsListaDoConsumidorTests
{
    private static ObservableCollection<ChipItem> DoisItens() =>
    [
        new() { Text = "A", Value = "a" },
        new() { Text = "B", Value = "b" },
    ];

    [Fact]
    public void Lista_gravavel_do_consumidor_e_mutada_e_nao_substituida()
    {
        var group = new ChipGroup { SelectionMode = ChipSelectionMode.Multiple, ItemsSource = DoisItens() };
        var listaDoConsumidor = new List<object?>();
        group.SelectedItems = listaDoConsumidor;

        Tap(GetChips(group)[0]);

        Assert.Same(listaDoConsumidor, group.SelectedItems);
        Assert.Equal(new object?[] { "a" }, listaDoConsumidor.ToArray());

        Tap(GetChips(group)[1]);

        Assert.Same(listaDoConsumidor, group.SelectedItems);
        Assert.Equal(new object?[] { "a", "b" }, listaDoConsumidor.ToArray());
    }

    [Fact]
    public void ObservableCollection_do_consumidor_e_mutada_preservando_assinaturas()
    {
        var group = new ChipGroup { SelectionMode = ChipSelectionMode.Multiple, ItemsSource = DoisItens() };
        var listaDoConsumidor = new ObservableCollection<object?>();
        var notificacoes = 0;
        listaDoConsumidor.CollectionChanged += (_, _) => notificacoes++;
        group.SelectedItems = listaDoConsumidor;

        Tap(GetChips(group)[0]);

        Assert.Same(listaDoConsumidor, group.SelectedItems);
        Assert.True(notificacoes > 0); // a VM continua ouvindo a própria coleção
        Assert.Contains("a", listaDoConsumidor);
    }

    [Fact]
    public void Lista_de_tamanho_fixo_e_substituida_sem_lancar()
    {
        var group = new ChipGroup { SelectionMode = ChipSelectionMode.Multiple, ItemsSource = DoisItens() };
        var arrayFixo = new object?[] { };
        group.SelectedItems = arrayFixo;

        var excecao = Record.Exception(() => Tap(GetChips(group)[0]));

        Assert.Null(excecao);
        Assert.NotSame(arrayFixo, group.SelectedItems);
        Assert.Equal(new object?[] { "a" }, group.SelectedItems!.Cast<object?>().ToArray());
    }

    [Fact]
    public void Lista_somente_leitura_e_substituida_sem_lancar()
    {
        var group = new ChipGroup { SelectionMode = ChipSelectionMode.Multiple, ItemsSource = DoisItens() };
        IList somenteLeitura = new ReadOnlyCollection<object?>(new List<object?>());
        group.SelectedItems = somenteLeitura;

        var excecao = Record.Exception(() => Tap(GetChips(group)[1]));

        Assert.Null(excecao);
        Assert.NotSame(somenteLeitura, group.SelectedItems);
        Assert.Equal(new object?[] { "b" }, group.SelectedItems!.Cast<object?>().ToArray());
    }
}

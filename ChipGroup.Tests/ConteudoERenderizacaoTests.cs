using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Layouts;
using Xunit;
using static Agile.Maui.ChipGroupTests.ChipTestHelpers;

namespace Agile.Maui.ChipGroupTests;

public class RebuildConteudoTests
{
    [Fact]
    public void Rebuild_gera_um_chip_por_item_com_os_textos_na_ordem()
    {
        var group = new ChipGroup
        {
            ItemsSource = new ObservableCollection<ChipItem>
            {
                new() { Text = "Aberto", Value = "open" },
                new() { Text = "Em andamento", Value = "progress" },
                new() { Text = "Fechado", Value = "closed" },
            },
        };

        var chips = GetChips(group);

        Assert.Equal(3, chips.Count);
        Assert.Equal(["Aberto", "Em andamento", "Fechado"], chips.Select(c => GetLabel(c).Text));
    }

    [Fact]
    public void Add_e_remove_na_colecao_refletem_nos_chips()
    {
        var items = new ObservableCollection<ChipItem> { new() { Text = "A", Value = "a" } };
        var group = new ChipGroup { ItemsSource = items };

        items.Add(new ChipItem { Text = "B", Value = "b" });
        Assert.Equal(2, GetChips(group).Count);

        items.RemoveAt(0);
        Assert.Single(GetChips(group));
        Assert.Equal("B", GetLabel(GetChips(group)[0]).Text);
    }

    [Fact]
    public void Indicador_aparece_so_em_Multiple_com_ShowCheckmark()
    {
        var items = new ObservableCollection<ChipItem>
        {
            new() { Text = "A", Value = "a", IsSelected = true },
            new() { Text = "B", Value = "b" },
        };

        var multiple = new ChipGroup { SelectionMode = ChipSelectionMode.Multiple, ItemsSource = items };
        var chipSelecionado = GetChips(multiple)[0];
        var chipNaoSelecionado = GetChips(multiple)[1];

        Assert.NotNull(GetIndicator(chipSelecionado));
        Assert.Equal("✓", ((Label)GetIndicator(chipSelecionado)!.Content!).Text);
        Assert.Equal(string.Empty, ((Label)GetIndicator(chipNaoSelecionado)!.Content!).Text);

        var single = new ChipGroup { SelectionMode = ChipSelectionMode.Single, ItemsSource = items };
        Assert.Null(GetIndicator(GetChips(single)[0]));

        var semCheckmark = new ChipGroup
        {
            SelectionMode = ChipSelectionMode.Multiple,
            ShowCheckmark = false,
            ItemsSource = items,
        };
        Assert.Null(GetIndicator(GetChips(semCheckmark)[0]));
    }

    [Fact]
    public void Chips_tem_alvo_de_toque_minimo_de_44()
    {
        var group = new ChipGroup { ItemsSource = new[] { "um", "dois" } };

        Assert.All(GetChips(group), chip => Assert.Equal(44, chip.MinimumHeightRequest));
    }

    [Fact]
    public void Chip_desabilitado_fica_translucido_e_sem_gesto_de_toque()
    {
        var group = new ChipGroup
        {
            ItemsSource = new ObservableCollection<ChipItem>
            {
                new() { Text = "Ativo", Value = 1 },
                new() { Text = "Inativo", Value = 2, IsEnabled = false },
            },
        };

        var chips = GetChips(group);

        Assert.Equal(1, chips[0].Opacity);
        Assert.NotEmpty(chips[0].GestureRecognizers);
        Assert.Equal(0.45, chips[1].Opacity);
        Assert.Empty(chips[1].GestureRecognizers);
    }

    [Fact]
    public void Acessibilidade_descricao_e_estado_sao_expostos_nos_chips()
    {
        var items = new ObservableCollection<ChipItem>
        {
            new() { Text = "Favoritos", Value = "fav", IsSelected = true },
            new() { Text = "Recentes", Value = "rec" },
        };
        var group = new ChipGroup { ItemsSource = items };

        var chips = GetChips(group);

        Assert.Equal("Favoritos", SemanticProperties.GetDescription(chips[0]));
        Assert.Equal("Selecionado", SemanticProperties.GetHint(chips[0]));
        Assert.Equal("Recentes", SemanticProperties.GetDescription(chips[1]));
        Assert.Equal("Não selecionado", SemanticProperties.GetHint(chips[1]));
    }

    [Fact]
    public void LayoutMode_Horizontal_envolve_o_layout_em_ScrollView_e_Wrap_devolve_o_FlexLayout()
    {
        var group = new ChipGroup { ItemsSource = new[] { "um", "dois" } };

        Assert.IsType<FlexLayout>(group.Content);
        Assert.Equal(FlexWrap.Wrap, ((FlexLayout)group.Content).Wrap);

        group.LayoutMode = ChipGroupLayoutMode.Horizontal;

        var scroll = Assert.IsType<ScrollView>(group.Content);
        Assert.Equal(ScrollOrientation.Horizontal, scroll.Orientation);
        var layout = Assert.IsType<FlexLayout>(scroll.Content);
        Assert.Equal(FlexWrap.NoWrap, layout.Wrap);
        Assert.Equal(2, GetChips(group).Count); // chips preservados na troca de modo

        group.LayoutMode = ChipGroupLayoutMode.Wrap;

        Assert.IsType<FlexLayout>(group.Content);
        Assert.Equal(2, GetChips(group).Count);
    }

    [Fact]
    public void LayoutMode_Vertical_usa_coluna_sem_ScrollView()
    {
        var group = new ChipGroup
        {
            LayoutMode = ChipGroupLayoutMode.Vertical,
            ItemsSource = new[] { "um", "dois" },
        };

        var layout = Assert.IsType<FlexLayout>(group.Content);
        Assert.Equal(FlexDirection.Column, layout.Direction);
    }
}

public class MemberPathTests
{
    private static Categoria[] Categorias() =>
    [
        new() { Nome = "Design", Id = 1 },
        new() { Nome = "Dev", Id = 2 },
    ];

    [Fact]
    public void DisplayMemberPath_usa_a_propriedade_do_POCO_como_texto()
    {
        var group = new ChipGroup { ItemsSource = Categorias(), DisplayMemberPath = "Nome" };

        Assert.Equal(["Design", "Dev"], GetChips(group).Select(c => GetLabel(c).Text));
    }

    [Fact]
    public void ValueMemberPath_usa_a_propriedade_do_POCO_como_valor_selecionado()
    {
        var group = new ChipGroup
        {
            ItemsSource = Categorias(),
            DisplayMemberPath = "Nome",
            ValueMemberPath = "Id",
        };

        Tap(GetChips(group)[1]);

        Assert.Equal(2, group.SelectedItem);
    }

    [Fact]
    public void Sem_ValueMemberPath_o_valor_selecionado_e_o_proprio_item()
    {
        var categorias = Categorias();
        var group = new ChipGroup { ItemsSource = categorias, DisplayMemberPath = "Nome" };

        Tap(GetChips(group)[0]);

        Assert.Same(categorias[0], group.SelectedItem);
    }

    [Fact]
    public void DisplayMemberPath_inexistente_cai_no_ToString_do_item()
    {
        var group = new ChipGroup { ItemsSource = Categorias(), DisplayMemberPath = "NaoExiste" };

        Assert.Equal("Categoria:Design", GetLabel(GetChips(group)[0]).Text);
    }
}

public class ChipItemTests
{
    [Fact]
    public void PropertyChanged_dispara_com_o_nome_da_propriedade_alterada()
    {
        var chip = new ChipItem();
        var nomes = new List<string?>();
        chip.PropertyChanged += (_, e) => nomes.Add(e.PropertyName);

        chip.Text = "Novo";
        chip.Value = 42;
        chip.IsSelected = true;
        chip.IsEnabled = false;

        Assert.Equal(
            [nameof(ChipItem.Text), nameof(ChipItem.Value), nameof(ChipItem.IsSelected), nameof(ChipItem.IsEnabled)],
            nomes);
    }

    [Fact]
    public void Atribuir_o_mesmo_valor_nao_dispara_PropertyChanged()
    {
        var chip = new ChipItem { Text = "Fixo", IsSelected = true };
        var disparos = 0;
        chip.PropertyChanged += (_, _) => disparos++;

        chip.Text = "Fixo";
        chip.IsSelected = true;

        Assert.Equal(0, disparos);
    }

    [Fact]
    public void Mudar_Text_de_um_ChipItem_observado_atualiza_o_rotulo_do_chip()
    {
        var item = new ChipItem { Text = "Antes", Value = "v" };
        var group = new ChipGroup { ItemsSource = new ObservableCollection<ChipItem> { item } };

        item.Text = "Depois";

        Assert.Equal("Depois", GetLabel(GetChips(group)[0]).Text);
    }

    [Fact]
    public void Mudar_IsEnabled_reflete_no_chip_renderizado()
    {
        var item = new ChipItem { Text = "A", Value = "a" };
        var group = new ChipGroup { ItemsSource = new ObservableCollection<ChipItem> { item } };
        Assert.NotEmpty(GetChips(group)[0].GestureRecognizers);

        item.IsEnabled = false;

        Assert.Empty(GetChips(group)[0].GestureRecognizers);
        Assert.Equal(0.45, GetChips(group)[0].Opacity);
    }

    [Fact]
    public void Valor_nulo_usa_o_Text_como_valor_do_chip()
    {
        var group = new ChipGroup
        {
            ItemsSource = new ObservableCollection<ChipItem> { new() { Text = "SoTexto" } },
        };

        Tap(GetChips(group)[0]);

        Assert.Equal("SoTexto", group.SelectedItem);
    }
}

using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Xunit;
using static Agile.Maui.ChipGroupTests.ChipTestHelpers;

namespace Agile.Maui.ChipGroupTests;

public class CoalescingTests
{
    [Fact]
    public void Cem_adds_na_colecao_geram_um_unico_rebuild_coalescido()
    {
        var items = new ObservableCollection<ChipItem>();
        // SelectedItems gravável desde o início: o sync pós-rebuild muta a lista em vez de substituí-la,
        // então o único callback esperado é o rebuild coalescido.
        var group = new ChipGroup { SelectedItems = new List<object?>(), ItemsSource = items };

        using var scope = Enqueue();

        for (var i = 0; i < 100; i++)
            items.Add(new ChipItem { Text = $"Chip {i}", Value = i });

        // Nada rebuildou ainda e só há UM callback pendente (coalescing, sem O(N²)).
        Assert.Empty(GetLayout(group).Children);
        Assert.Equal(1, scope.Dispatcher.PendingCount);

        var executados = scope.Dispatcher.ProcessQueue();

        Assert.Equal(1, executados);
        Assert.Equal(100, GetChips(group).Count);
        Assert.Equal("Chip 0", GetLabel(GetChips(group)[0]).Text);
        Assert.Equal("Chip 99", GetLabel(GetChips(group)[99]).Text);
    }

    [Fact]
    public void Um_toque_agenda_um_unico_rebuild_mesmo_mudando_SelectedItem_e_SelectedItems()
    {
        var items = new ObservableCollection<ChipItem>
        {
            new() { Text = "A", Value = "a" },
            new() { Text = "B", Value = "b" },
        };
        var group = new ChipGroup { ItemsSource = items };
        var chipB = GetChips(group)[1];
        var eventos = 0;
        group.SelectionChanged += (_, _) => eventos++;

        using var scope = Enqueue();

        Tap(chipB);

        // Evento dispara na hora; o rebuild visual fica coalescido num único callback.
        Assert.Equal(1, eventos);
        Assert.Equal(1, scope.Dispatcher.PendingCount);
        Assert.Equal("Não selecionado", SemanticProperties.GetHint(GetChips(group)[1]));

        var executados = scope.Dispatcher.ProcessQueue();

        Assert.Equal(1, executados);
        Assert.Equal("Selecionado", SemanticProperties.GetHint(GetChips(group)[1]));
        Assert.Equal("b", group.SelectedItem);
    }

    [Fact]
    public void Mudancas_de_estilo_em_rajada_geram_um_unico_rebuild()
    {
        var group = new ChipGroup { ItemsSource = new[] { "um", "dois" } };

        using var scope = Enqueue();

        group.FontSize = 16;
        group.CornerRadius = 10;
        group.ChipSpacing = 4;
        group.RowSpacing = 4;

        Assert.Equal(1, scope.Dispatcher.PendingCount);

        var executados = scope.Dispatcher.ProcessQueue();

        Assert.Equal(1, executados);
        Assert.Equal(16, GetLabel(GetChips(group)[0]).FontSize);
    }
}

public class ThreadingTests
{
    [Fact]
    public void CollectionChanged_de_outra_thread_nao_lanca_e_rebuilda_via_dispatcher()
    {
        var items = new ObservableCollection<ChipItem> { new() { Text = "A", Value = "a" } };
        var group = new ChipGroup { ItemsSource = items };

        using var scope = Enqueue();

        Exception? excecao = null;
        var thread = new Thread(() =>
        {
            try
            {
                items.Add(new ChipItem { Text = "B", Value = "b" });
            }
            catch (Exception ex)
            {
                excecao = ex;
            }
        });
        thread.Start();
        thread.Join();

        Assert.Null(excecao);
        // O add em outra thread NÃO mexeu na árvore visual diretamente: foi para o dispatcher do controle.
        Assert.Single(GetChips(group));
        Assert.True(scope.Dispatcher.PendingCount >= 1);

        scope.Dispatcher.ProcessQueue();

        Assert.Equal(2, GetChips(group).Count);
        Assert.Equal("B", GetLabel(GetChips(group)[1]).Text);
    }

    [Fact]
    public void Varios_adds_de_outra_thread_coalescem_em_um_rebuild()
    {
        var items = new ObservableCollection<ChipItem>();
        var group = new ChipGroup { SelectedItems = new List<object?>(), ItemsSource = items };

        using var scope = Enqueue();

        var thread = new Thread(() =>
        {
            for (var i = 0; i < 10; i++)
                items.Add(new ChipItem { Text = $"{i}", Value = i });
        });
        thread.Start();
        thread.Join();

        Assert.Equal(1, scope.Dispatcher.PendingCount);

        var executados = scope.Dispatcher.ProcessQueue();

        Assert.Equal(1, executados);
        Assert.Equal(10, GetChips(group).Count);
    }
}

using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Xunit;

namespace Agile.Maui.VirtualizedCollectionTests;

// Contrato público do controle: defaults, round-trip de set/get, validações das
// bindable properties e notificações de PropertyChanged que os handlers mapeiam.
public class VirtualizedCollectionViewContratoTests
{
    // ── Defaults ─────────────────────────────────────────────────────────────

    [Fact]
    public void Defaults_de_todas_as_bindable_properties()
    {
        var view = new VirtualizedCollectionView();

        Assert.Null(view.ItemsSource);
        Assert.Null(view.ItemTemplate);
        Assert.Null(view.Header);
        Assert.Null(view.HeaderTemplate);
        Assert.Null(view.Footer);
        Assert.Null(view.FooterTemplate);
        Assert.Null(view.EmptyView);
        Assert.Null(view.EmptyViewTemplate);
        Assert.Null(view.RemainingItemsThresholdReachedCommand);
        Assert.Null(view.ScrolledCommand);

        Assert.Equal(-1.0, view.ItemHeight);
        Assert.Equal(1, view.Span);
        Assert.Equal(VirtualizedOrientation.Vertical, view.Orientation);
        Assert.Equal(-1, view.RemainingItemsThreshold);
        Assert.Equal(0.0, view.ItemSpacing);
        Assert.Equal(ItemSizingStrategy.Fixed, view.ItemSizingStrategy);
        Assert.Equal(350.0, view.ItemHeightRequest);
        Assert.Equal(-1.0, view.ItemWidthRequest);
        Assert.Equal(ScrollBarVisibility.Default, view.VerticalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Default, view.HorizontalScrollBarVisibility);
    }

    // ── Round-trip ───────────────────────────────────────────────────────────

    [Fact]
    public void ItemsSource_aceita_varios_tipos_de_fonte_com_roundtrip()
    {
        var view = new VirtualizedCollectionView();

        var lista = new List<int> { 1, 2 };
        view.ItemsSource = lista;
        Assert.Same(lista, view.ItemsSource);

        var array = new[] { "a", "b" };
        view.ItemsSource = array;
        Assert.Same(array, view.ItemsSource);

        var observable = new ObservableCollection<int> { 1 };
        view.ItemsSource = observable;
        Assert.Same(observable, view.ItemsSource);

        var range = new ObservableRangeCollection<int> { 1 };
        view.ItemsSource = range;
        Assert.Same(range, view.ItemsSource);

        IEnumerable preguicosa = Gerar();
        view.ItemsSource = preguicosa;
        Assert.Same(preguicosa, view.ItemsSource);

        static IEnumerable<int> Gerar() { yield return 1; }
    }

    [Fact]
    public void ItemsSource_pode_voltar_a_nulo()
    {
        var view = new VirtualizedCollectionView { ItemsSource = new List<int> { 1 } };

        view.ItemsSource = null;

        Assert.Null(view.ItemsSource);
    }

    [Fact]
    public void Roundtrip_das_propriedades_numericas()
    {
        var view = new VirtualizedCollectionView
        {
            ItemHeight = 120.5,
            ItemSpacing = 8.0,
            ItemHeightRequest = 200.0,
            ItemWidthRequest = 90.0,
            Span = 3,
            RemainingItemsThreshold = 5,
        };

        Assert.Equal(120.5, view.ItemHeight);
        Assert.Equal(8.0, view.ItemSpacing);
        Assert.Equal(200.0, view.ItemHeightRequest);
        Assert.Equal(90.0, view.ItemWidthRequest);
        Assert.Equal(3, view.Span);
        Assert.Equal(5, view.RemainingItemsThreshold);
    }

    [Fact]
    public void Roundtrip_das_propriedades_de_objeto()
    {
        var template = new DataTemplate(typeof(Label));
        var headerTemplate = new DataTemplate(typeof(Label));
        var footerTemplate = new DataTemplate(typeof(Label));
        var emptyTemplate = new DataTemplate(typeof(Label));
        var header = new Label();
        var footer = "rodapé";
        var empty = "sem itens";
        var comando1 = new ComandoDeTeste();
        var comando2 = new ComandoDeTeste();

        var view = new VirtualizedCollectionView
        {
            ItemTemplate = template,
            Header = header,
            HeaderTemplate = headerTemplate,
            Footer = footer,
            FooterTemplate = footerTemplate,
            EmptyView = empty,
            EmptyViewTemplate = emptyTemplate,
            RemainingItemsThresholdReachedCommand = comando1,
            ScrolledCommand = comando2,
        };

        Assert.Same(template, view.ItemTemplate);
        Assert.Same(header, view.Header);
        Assert.Same(headerTemplate, view.HeaderTemplate);
        Assert.Same(footer, view.Footer);
        Assert.Same(footerTemplate, view.FooterTemplate);
        Assert.Same(empty, view.EmptyView);
        Assert.Same(emptyTemplate, view.EmptyViewTemplate);
        Assert.Same(comando1, view.RemainingItemsThresholdReachedCommand);
        Assert.Same(comando2, view.ScrolledCommand);
    }

    [Fact]
    public void Roundtrip_dos_enums()
    {
        var view = new VirtualizedCollectionView
        {
            Orientation = VirtualizedOrientation.Horizontal,
            ItemSizingStrategy = ItemSizingStrategy.Dynamic,
            VerticalScrollBarVisibility = ScrollBarVisibility.Always,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
        };

        Assert.Equal(VirtualizedOrientation.Horizontal, view.Orientation);
        Assert.Equal(ItemSizingStrategy.Dynamic, view.ItemSizingStrategy);
        Assert.Equal(ScrollBarVisibility.Always, view.VerticalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Never, view.HorizontalScrollBarVisibility);

        view.ItemSizingStrategy = ItemSizingStrategy.MeasureFirst;
        Assert.Equal(ItemSizingStrategy.MeasureFirst, view.ItemSizingStrategy);
    }

    // ── Validações ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public void Span_invalido_e_ignorado_sem_lancar_e_preserva_o_valor(int span)
    {
        // Contrato do MAUI 10: validateValue reprovado torna o SetValue um no-op
        // silencioso (não lança) — o valor anterior permanece e nada é notificado.
        var view = new VirtualizedCollectionView { Span = 3 };
        var nomes = new List<string?>();
        ((INotifyPropertyChanged)view).PropertyChanged += (_, e) => nomes.Add(e.PropertyName);

        view.Span = span;

        Assert.Equal(3, view.Span);
        Assert.DoesNotContain(nameof(VirtualizedCollectionView.Span), nomes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(12)]
    public void Span_valido_aceita(int span)
    {
        var view = new VirtualizedCollectionView { Span = span };

        Assert.Equal(span, view.Span);
    }

    [Fact]
    public void ItemSpacing_negativo_e_ignorado_sem_lancar_e_preserva_o_valor()
    {
        var view = new VirtualizedCollectionView { ItemSpacing = 6.0 };

        view.ItemSpacing = -0.5;

        Assert.Equal(6.0, view.ItemSpacing);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(4.0)]
    [InlineData(24.5)]
    public void ItemSpacing_zero_ou_positivo_aceita(double spacing)
    {
        var view = new VirtualizedCollectionView { ItemSpacing = spacing };

        Assert.Equal(spacing, view.ItemSpacing);
    }

    // ── PropertyChanged ──────────────────────────────────────────────────────

    [Fact]
    public void Trocas_de_template_e_fonte_disparam_PropertyChanged_com_o_nome_certo()
    {
        // Os mappers dos handlers são chaveados por esses nomes — um rename silencioso
        // quebraria o registro sem erro de compilação.
        var casos = new (string Nome, Action<VirtualizedCollectionView> Setter)[]
        {
            (nameof(VirtualizedCollectionView.ItemsSource),       v => v.ItemsSource = new List<int>()),
            (nameof(VirtualizedCollectionView.ItemTemplate),      v => v.ItemTemplate = new DataTemplate(typeof(Label))),
            (nameof(VirtualizedCollectionView.Header),            v => v.Header = "h"),
            (nameof(VirtualizedCollectionView.HeaderTemplate),    v => v.HeaderTemplate = new DataTemplate(typeof(Label))),
            (nameof(VirtualizedCollectionView.Footer),            v => v.Footer = "f"),
            (nameof(VirtualizedCollectionView.FooterTemplate),    v => v.FooterTemplate = new DataTemplate(typeof(Label))),
            (nameof(VirtualizedCollectionView.EmptyView),         v => v.EmptyView = "vazio"),
            (nameof(VirtualizedCollectionView.EmptyViewTemplate), v => v.EmptyViewTemplate = new DataTemplate(typeof(Label))),
            (nameof(VirtualizedCollectionView.Span),              v => v.Span = 2),
            (nameof(VirtualizedCollectionView.Orientation),       v => v.Orientation = VirtualizedOrientation.Horizontal),
            (nameof(VirtualizedCollectionView.ItemSizingStrategy), v => v.ItemSizingStrategy = ItemSizingStrategy.Dynamic),
            (nameof(VirtualizedCollectionView.ItemHeightRequest), v => v.ItemHeightRequest = 100),
        };

        foreach (var (nome, setter) in casos)
        {
            var view = new VirtualizedCollectionView();
            var nomes = new List<string?>();
            ((INotifyPropertyChanged)view).PropertyChanged += (_, e) => nomes.Add(e.PropertyName);

            setter(view);

            Assert.True(nomes.Contains(nome), $"esperava PropertyChanged de '{nome}', veio: [{string.Join(", ", nomes)}]");
        }
    }

    [Fact]
    public void Setar_o_mesmo_valor_nao_renotifica()
    {
        var view = new VirtualizedCollectionView { Span = 2 };
        var nomes = new List<string?>();
        ((INotifyPropertyChanged)view).PropertyChanged += (_, e) => nomes.Add(e.PropertyName);

        view.Span = 2;
        view.Orientation = VirtualizedOrientation.Vertical;   // já é o default

        Assert.DoesNotContain(nameof(VirtualizedCollectionView.Span), nomes);
        Assert.DoesNotContain(nameof(VirtualizedCollectionView.Orientation), nomes);
    }

    // ── ScrollTo / args ──────────────────────────────────────────────────────

    [Fact]
    public void ScrollTo_e_ScrollToStart_sem_handler_nao_lancam()
    {
        var view = new VirtualizedCollectionView();

        view.ScrollTo(5);
        view.ScrollTo(0, animated: false);
        view.ScrollToStart();
        view.ScrollToStart(animated: false);
    }

    [Fact]
    public void ScrollToRequest_guarda_indice_e_flag()
    {
        var pedido = new VirtualizedCollectionView.ScrollToRequest(7, false);

        Assert.Equal(7, pedido.Index);
        Assert.False(pedido.Animated);
        Assert.Equal(pedido, new VirtualizedCollectionView.ScrollToRequest(7, false));
    }

    [Fact]
    public void VirtualizedScrolledEventArgs_guarda_offsets()
    {
        var args = new VirtualizedScrolledEventArgs(12.5, 48.0);

        Assert.Equal(12.5, args.HorizontalOffset);
        Assert.Equal(48.0, args.VerticalOffset);
    }
}

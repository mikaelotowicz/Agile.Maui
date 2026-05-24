// Controls/VirtualizedCollectionView.cs
using System.Collections;
using System.Windows.Input;
using Microsoft.Maui.Layouts;

namespace Controls;

public enum VirtualizedOrientation { Vertical, Horizontal }

/// <summary>
/// Layout container — herdar de Layout (em vez de View) é necessário para que
/// as views de item criadas pelo DataTemplate tenham um Parent MAUI e participem
/// do ciclo Measure/Arrange top-down. Sem Parent, as views são órfãs e o
/// ContentPanel.CrossPlatformMeasure retorna valores incorretos no Windows.
/// No Android e iOS o RecyclerView/UICollectionView gerenciam o layout
/// diretamente; Children estará sempre vazio nessas plataformas.
/// </summary>
public class VirtualizedCollectionView : Layout
{
    public static readonly BindableProperty ItemsSourceProperty =
        BindableProperty.Create(nameof(ItemsSource), typeof(IEnumerable),
            typeof(VirtualizedCollectionView), null);

    public static readonly BindableProperty ItemTemplateProperty =
        BindableProperty.Create(nameof(ItemTemplate), typeof(DataTemplate),
            typeof(VirtualizedCollectionView), null);

    public static readonly BindableProperty ItemHeightProperty =
        BindableProperty.Create(nameof(ItemHeight), typeof(double),
            typeof(VirtualizedCollectionView), -1.0);

    public static readonly BindableProperty ColumnCountProperty =
        BindableProperty.Create(nameof(ColumnCount), typeof(int),
            typeof(VirtualizedCollectionView), 1,
            validateValue: (_, v) => (int)v >= 1);

    public static readonly BindableProperty OrientationProperty =
        BindableProperty.Create(nameof(Orientation), typeof(VirtualizedOrientation),
            typeof(VirtualizedCollectionView), VirtualizedOrientation.Vertical);

    public static readonly BindableProperty RemainingItemsThresholdProperty =
        BindableProperty.Create(nameof(RemainingItemsThreshold), typeof(int),
            typeof(VirtualizedCollectionView), -1);

    public static readonly BindableProperty RemainingItemsThresholdReachedCommandProperty =
        BindableProperty.Create(nameof(RemainingItemsThresholdReachedCommand), typeof(ICommand),
            typeof(VirtualizedCollectionView), null);

    /// <summary>Fonte de dados. Suporta IEnumerable e INotifyCollectionChanged.</summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>DataTemplate usado para renderizar cada item.</summary>
    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    /// <summary>Altura fixa do item em DIPs. -1 = wrap_content (mais lento).</summary>
    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    /// <summary>Número de colunas (≥1). 1 = lista linear, &gt;1 = grade.</summary>
    public int ColumnCount
    {
        get => (int)GetValue(ColumnCountProperty);
        set => SetValue(ColumnCountProperty, value);
    }

    /// <summary>Direção do scroll.</summary>
    public VirtualizedOrientation Orientation
    {
        get => (VirtualizedOrientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Quantos itens antes do fim disparam RemainingItemsThresholdReached. -1 = desabilitado.</summary>
    public int RemainingItemsThreshold
    {
        get => (int)GetValue(RemainingItemsThresholdProperty);
        set => SetValue(RemainingItemsThresholdProperty, value);
    }

    /// <summary>Comando executado quando o threshold de itens restantes é atingido.</summary>
    public ICommand? RemainingItemsThresholdReachedCommand
    {
        get => (ICommand?)GetValue(RemainingItemsThresholdReachedCommandProperty);
        set => SetValue(RemainingItemsThresholdReachedCommandProperty, value);
    }

    public event EventHandler? RemainingItemsThresholdReached;
    public event EventHandler<VirtualizedScrolledEventArgs>? Scrolled;

    internal void RaiseRemainingItemsThresholdReached()
    {
        RemainingItemsThresholdReached?.Invoke(this, EventArgs.Empty);
        if (RemainingItemsThresholdReachedCommand?.CanExecute(null) == true)
            RemainingItemsThresholdReachedCommand.Execute(null);
    }

    internal void RaiseScrolled(double scrollX, double scrollY) =>
        Scrolled?.Invoke(this, new VirtualizedScrolledEventArgs(scrollX, scrollY));

    // ── Helpers para o handler Windows ──────────────────────────────────────

    /// <summary>
    /// Adiciona uma view como filho lógico. Isso define view.Parent = this,
    /// permitindo que o ciclo de layout do MAUI alcance a view.
    /// Chamado apenas pelo handler Windows — Android/iOS usam cells nativas.
    /// </summary>
    internal void AddMauiChild(IView view)   => Add((View)view);

    /// <summary>Remove um filho lógico adicionado via AddMauiChild.</summary>
    internal void RemoveMauiChild(IView view) => Remove((View)view);

    /// <summary>
    /// Layout manager passthrough: o handler Windows posiciona os items
    /// diretamente no WinUI StackPanel; o MAUI layout manager não interfere.
    /// </summary>
    protected override ILayoutManager CreateLayoutManager() =>
        new VrPassthroughLayoutManager(this);
}

file sealed class VrPassthroughLayoutManager : ILayoutManager
{
    // Cache da última medida finita. Quando o Grid re-mede com infinity durante
    // re-layouts incrementais (causados por mudanças de Label.Text no scroll),
    // devolve o tamanho anteriormente alocado em vez de 0, evitando que o Grid
    // recalcule alturas das rows Auto acima desta view de forma incorreta.
    private Size _cache;

    public VrPassthroughLayoutManager(VirtualizedCollectionView _) { }

    public Size Measure(double widthConstraint, double heightConstraint)
    {
        var w = double.IsInfinity(widthConstraint)  ? _cache.Width  : widthConstraint;
        var h = double.IsInfinity(heightConstraint) ? _cache.Height : heightConstraint;
        if (!double.IsInfinity(widthConstraint) && !double.IsInfinity(heightConstraint))
            _cache = new(w, h);
        return new(w, h);
    }

    public Size ArrangeChildren(Rect bounds) => bounds.Size;
}

public sealed class VirtualizedScrolledEventArgs : EventArgs
{
    public double ScrollX { get; }
    public double ScrollY { get; }
    public VirtualizedScrolledEventArgs(double x, double y) { ScrollX = x; ScrollY = y; }
}

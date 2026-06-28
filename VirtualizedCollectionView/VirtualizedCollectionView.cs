// Controls/VirtualizedCollectionView.cs
using System.Collections;
using System.Windows.Input;
using Microsoft.Maui;

namespace Agile.Maui;

public enum VirtualizedOrientation { Vertical, Horizontal }

// Fixed        → altura definida em ItemHeightRequest (mais rápido; corta conteúdo maior).
// Dynamic      → cada item se ajusta ao conteúdo (WrapContent); mede item a item.
// MeasureFirst → mede o 1º item exibido e aplica essa altura a todos (rápido como Fixed,
//                sem número mágico; assume itens de altura uniforme, senão corta).
public enum ItemSizingStrategy { Fixed, Dynamic, MeasureFirst }

// ContentView como base: no Windows, Content = CollectionView nativo do MAUI e nenhum
// handler customizado é necessário. No Android/iOS os handlers criam RecyclerView /
// UICollectionView nativos e ignoram o Content.
public partial class VirtualizedCollectionView : ContentView
{
    public static readonly BindableProperty ItemsSourceProperty =
        BindableProperty.Create(nameof(ItemsSource), typeof(IEnumerable),
            typeof(VirtualizedCollectionView), null);

    public static readonly BindableProperty ItemTemplateProperty =
        BindableProperty.Create(nameof(ItemTemplate), typeof(DataTemplate),
            typeof(VirtualizedCollectionView), null);

    public static readonly BindableProperty HeaderProperty =
        BindableProperty.Create(nameof(Header), typeof(object),
            typeof(VirtualizedCollectionView), null,
            propertyChanged: OnHeaderFooterChanged);

    public static readonly BindableProperty HeaderTemplateProperty =
        BindableProperty.Create(nameof(HeaderTemplate), typeof(DataTemplate),
            typeof(VirtualizedCollectionView), null);

    public static readonly BindableProperty FooterProperty =
        BindableProperty.Create(nameof(Footer), typeof(object),
            typeof(VirtualizedCollectionView), null,
            propertyChanged: OnHeaderFooterChanged);

    public static readonly BindableProperty FooterTemplateProperty =
        BindableProperty.Create(nameof(FooterTemplate), typeof(DataTemplate),
            typeof(VirtualizedCollectionView), null);

    public static readonly BindableProperty ItemHeightProperty =
        BindableProperty.Create(nameof(ItemHeight), typeof(double),
            typeof(VirtualizedCollectionView), -1.0);

    public static readonly BindableProperty SpanProperty =
        BindableProperty.Create(nameof(Span), typeof(int),
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

    public static readonly BindableProperty ScrolledCommandProperty =
        BindableProperty.Create(nameof(ScrolledCommand), typeof(ICommand),
            typeof(VirtualizedCollectionView), null);

    public static readonly BindableProperty EmptyViewProperty =
        BindableProperty.Create(nameof(EmptyView), typeof(object),
            typeof(VirtualizedCollectionView), null);

    public static readonly BindableProperty EmptyViewTemplateProperty =
        BindableProperty.Create(nameof(EmptyViewTemplate), typeof(DataTemplate),
            typeof(VirtualizedCollectionView), null);

    public static readonly BindableProperty ItemSpacingProperty =
        BindableProperty.Create(nameof(ItemSpacing), typeof(double),
            typeof(VirtualizedCollectionView), 0.0,
            validateValue: (_, v) => (double)v >= 0);

    public static readonly BindableProperty ItemSizingStrategyProperty =
        BindableProperty.Create(nameof(ItemSizingStrategy), typeof(ItemSizingStrategy),
            typeof(VirtualizedCollectionView), ItemSizingStrategy.Fixed);

    public static readonly BindableProperty ItemHeightRequestProperty =
        BindableProperty.Create(nameof(ItemHeightRequest), typeof(double),
            typeof(VirtualizedCollectionView), 350.0);

    public static readonly BindableProperty ItemWidthRequestProperty =
        BindableProperty.Create(nameof(ItemWidthRequest), typeof(double),
            typeof(VirtualizedCollectionView), -1.0);

    public static readonly BindableProperty VerticalScrollBarVisibilityProperty =
        BindableProperty.Create(nameof(VerticalScrollBarVisibility), typeof(ScrollBarVisibility),
            typeof(VirtualizedCollectionView), ScrollBarVisibility.Default);

    public static readonly BindableProperty HorizontalScrollBarVisibilityProperty =
        BindableProperty.Create(nameof(HorizontalScrollBarVisibility), typeof(ScrollBarVisibility),
            typeof(VirtualizedCollectionView), ScrollBarVisibility.Default);

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public DataTemplate? HeaderTemplate
    {
        get => (DataTemplate?)GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public DataTemplate? FooterTemplate
    {
        get => (DataTemplate?)GetValue(FooterTemplateProperty);
        set => SetValue(FooterTemplateProperty, value);
    }

    /// <summary>Altura fixa do item em DIPs. -1 = wrap_content. Ignorado no Windows.</summary>
    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public int Span
    {
        get => (int)GetValue(SpanProperty);
        set => SetValue(SpanProperty, value);
    }

    public VirtualizedOrientation Orientation
    {
        get => (VirtualizedOrientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public int RemainingItemsThreshold
    {
        get => (int)GetValue(RemainingItemsThresholdProperty);
        set => SetValue(RemainingItemsThresholdProperty, value);
    }

    public ICommand? RemainingItemsThresholdReachedCommand
    {
        get => (ICommand?)GetValue(RemainingItemsThresholdReachedCommandProperty);
        set => SetValue(RemainingItemsThresholdReachedCommandProperty, value);
    }

    public ICommand? ScrolledCommand
    {
        get => (ICommand?)GetValue(ScrolledCommandProperty);
        set => SetValue(ScrolledCommandProperty, value);
    }

    public object? EmptyView
    {
        get => GetValue(EmptyViewProperty);
        set => SetValue(EmptyViewProperty, value);
    }

    public DataTemplate? EmptyViewTemplate
    {
        get => (DataTemplate?)GetValue(EmptyViewTemplateProperty);
        set => SetValue(EmptyViewTemplateProperty, value);
    }

    public double ItemSpacing
    {
        get => (double)GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    public ItemSizingStrategy ItemSizingStrategy
    {
        get => (ItemSizingStrategy)GetValue(ItemSizingStrategyProperty);
        set => SetValue(ItemSizingStrategyProperty, value);
    }

    public double ItemHeightRequest
    {
        get => (double)GetValue(ItemHeightRequestProperty);
        set => SetValue(ItemHeightRequestProperty, value);
    }

    public double ItemWidthRequest
    {
        get => (double)GetValue(ItemWidthRequestProperty);
        set => SetValue(ItemWidthRequestProperty, value);
    }

    /// <summary>
    /// Visibilidade da barra de rolagem vertical.
    /// <c>Always</c> = sempre visível; <c>Never</c> = oculta;
    /// <c>Default</c> = padrão da plataforma (Android: oculta; iOS: indicador nativo que some sozinho).
    /// </summary>
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty);
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }

    /// <summary>Visibilidade da barra de rolagem horizontal (ver <see cref="VerticalScrollBarVisibility"/>).</summary>
    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(HorizontalScrollBarVisibilityProperty);
        set => SetValue(HorizontalScrollBarVisibilityProperty, value);
    }

    public event EventHandler? RemainingItemsThresholdReached;
    public event EventHandler<VirtualizedScrolledEventArgs>? Scrolled;

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        ApplyInheritedContext(Header);
        ApplyInheritedContext(Footer);
    }

    private void ApplyInheritedContext(object? content)
    {
        if (content is BindableObject bindable && bindable.BindingContext is null)
            bindable.BindingContext = BindingContext;
    }

    private static void OnHeaderFooterChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((VirtualizedCollectionView)bindable).ApplyInheritedContext(newValue);

    internal bool HasScrolledObservers =>
        Scrolled is not null || ScrolledCommand is not null;

    internal bool CanRaiseRemainingItemsThresholdReached =>
        RemainingItemsThresholdReached is not null ||
        RemainingItemsThresholdReachedCommand?.CanExecute(null) == true;

    internal void RaiseRemainingItemsThresholdReached()
    {
        var command = RemainingItemsThresholdReachedCommand;
        var canExecute = command?.CanExecute(null) == true;

        if (RemainingItemsThresholdReached is null && !canExecute)
            return;

        RemainingItemsThresholdReached?.Invoke(this, EventArgs.Empty);
        if (canExecute)
            command!.Execute(null);
    }

    internal void RaiseScrolled(double scrollX, double scrollY)
    {
        var command = ScrolledCommand;
        if (Scrolled is null && command is null)
            return;

        var args = new VirtualizedScrolledEventArgs(scrollX, scrollY);
        Scrolled?.Invoke(this, args);
        if (command?.CanExecute(args) == true)
            command.Execute(args);
    }

    // ── Windows ───────────────────────────────────────────────────────────────
    // Toda a lógica do Windows (CollectionView nativo como Content, drag-to-scroll
    // com mouse e inércia) vive na partial em
    // Platforms/Windows/VirtualizedCollectionView/VirtualizedCollectionView.Windows.cs.
    // No Android/iOS os handlers sobrescrevem CreatePlatformView e ignoram Content.

#if !WINDOWS
    public void ScrollTo(int index, bool animated = true) =>
        Handler?.Invoke(nameof(ScrollTo), new ScrollToRequest(index, animated));

    public void ScrollToStart(bool animated = true) =>
        Handler?.Invoke(nameof(ScrollToStart), new ScrollToRequest(0, animated));
#endif

    public readonly record struct ScrollToRequest(int Index, bool Animated);
}

public sealed class VirtualizedScrolledEventArgs : EventArgs
{
    public double HorizontalOffset { get; }
    public double VerticalOffset { get; }
    public VirtualizedScrolledEventArgs(double x, double y) { HorizontalOffset = x; VerticalOffset = y; }
}

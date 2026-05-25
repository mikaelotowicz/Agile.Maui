// Controls/VirtualizedCollectionView.cs
using System.Collections;
using System.Windows.Input;

namespace Agile.Maui;

public enum VirtualizedOrientation { Vertical, Horizontal }

public enum ItemSizeStrategy { Fixed, Dynamic }

// ContentView como base: no Windows, Content = CollectionView nativo do MAUI e nenhum
// handler customizado é necessário. No Android/iOS os handlers criam RecyclerView /
// UICollectionView nativos e ignoram o Content.
public class VirtualizedCollectionView : ContentView
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

    public static readonly BindableProperty ItemSizeStrategyProperty =
        BindableProperty.Create(nameof(ItemSizeStrategy), typeof(ItemSizeStrategy),
            typeof(VirtualizedCollectionView), ItemSizeStrategy.Fixed);

    public static readonly BindableProperty ItemHeightRequestProperty =
        BindableProperty.Create(nameof(ItemHeightRequest), typeof(double),
            typeof(VirtualizedCollectionView), 350.0);

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

    /// <summary>Altura fixa do item em DIPs. -1 = wrap_content. Ignorado no Windows.</summary>
    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public int ColumnCount
    {
        get => (int)GetValue(ColumnCountProperty);
        set => SetValue(ColumnCountProperty, value);
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

    public ItemSizeStrategy ItemSizeStrategy
    {
        get => (ItemSizeStrategy)GetValue(ItemSizeStrategyProperty);
        set => SetValue(ItemSizeStrategyProperty, value);
    }

    public double ItemHeightRequest
    {
        get => (double)GetValue(ItemHeightRequestProperty);
        set => SetValue(ItemHeightRequestProperty, value);
    }

    public event EventHandler? RemainingItemsThresholdReached;
    public event EventHandler<VirtualizedScrolledEventArgs>? Scrolled;

    internal void RaiseRemainingItemsThresholdReached()
    {
        RemainingItemsThresholdReached?.Invoke(this, EventArgs.Empty);
        if (RemainingItemsThresholdReachedCommand?.CanExecute(null) == true)
            RemainingItemsThresholdReachedCommand.Execute(null);
    }

    internal void RaiseScrolled(double scrollX, double scrollY)
    {
        var args = new VirtualizedScrolledEventArgs(scrollX, scrollY);
        Scrolled?.Invoke(this, args);
        if (ScrolledCommand?.CanExecute(args) == true)
            ScrolledCommand.Execute(args);
    }

    // ── Windows: CollectionView nativo do MAUI como Content ───────────────────
    // No Android/iOS os handlers sobrescrevem CreatePlatformView e ignoram Content.

#if WINDOWS
    private readonly CollectionView _cv;

    public VirtualizedCollectionView()
    {
        _cv = new CollectionView();
        Content = _cv;
        _cv.RemainingItemsThresholdReached += (_, _) => RaiseRemainingItemsThresholdReached();
        _cv.Scrolled += (_, e) => RaiseScrolled(e.HorizontalOffset, e.VerticalOffset);
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        switch (propertyName)
        {
            case nameof(ItemsSource):             _cv.ItemsSource             = ItemsSource;             break;
            case nameof(ItemTemplate):            _cv.ItemTemplate            = ItemTemplate;            break;
            case nameof(EmptyView):               _cv.EmptyView               = EmptyView;               break;
            case nameof(EmptyViewTemplate):       _cv.EmptyViewTemplate       = EmptyViewTemplate;       break;
            case nameof(RemainingItemsThreshold): _cv.RemainingItemsThreshold = RemainingItemsThreshold; break;
            case nameof(ColumnCount):
            case nameof(Orientation):
            case nameof(ItemSpacing):             SyncLayout();                                          break;
        }
    }

    private void SyncLayout()
    {
        var orientation = Orientation == VirtualizedOrientation.Vertical
            ? ItemsLayoutOrientation.Vertical
            : ItemsLayoutOrientation.Horizontal;
        double spacing = ItemSpacing;
        _cv.ItemsLayout = ColumnCount > 1
            ? new GridItemsLayout(ColumnCount, orientation)
                { HorizontalItemSpacing = spacing, VerticalItemSpacing = spacing }
            : new LinearItemsLayout(orientation)
                { ItemSpacing = spacing };
    }

    public void ScrollTo(int index, bool animated = true) =>
        _cv.ScrollTo(index, animate: animated);
#else
    public void ScrollTo(int index, bool animated = true) =>
        Handler?.Invoke(nameof(ScrollTo), new ScrollToRequest(index, animated));
#endif

    public readonly record struct ScrollToRequest(int Index, bool Animated);
}

public sealed class VirtualizedScrolledEventArgs : EventArgs
{
    public double ScrollX { get; }
    public double ScrollY { get; }
    public VirtualizedScrolledEventArgs(double x, double y) { ScrollX = x; ScrollY = y; }
}

// Platforms/Windows/VirtualizedCollectionView/VirtualizedCollectionViewHandler.cs
//
// Delega para a CollectionView do MAUI, que usa ItemsRepeater + ScrollViewer
// com virtualização nativa do WinUI — sem StackPanel, sem criação manual de cells.

using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;

namespace Controls.Platforms.Windows;

internal sealed class VirtualizedCollectionViewHandler
    : ViewHandler<VirtualizedCollectionView, FrameworkElement>
{
    public static readonly PropertyMapper<VirtualizedCollectionView, VirtualizedCollectionViewHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(VirtualizedCollectionView.ItemsSource)]                           = (h, _) => h.UpdateItemsSource(),
            [nameof(VirtualizedCollectionView.ItemTemplate)]                          = (h, _) => h.UpdateItemTemplate(),
            [nameof(VirtualizedCollectionView.ItemHeight)]                            = (h, _) => { },
            [nameof(VirtualizedCollectionView.ColumnCount)]                           = (h, _) => h.UpdateLayout(),
            [nameof(VirtualizedCollectionView.Orientation)]                           = (h, _) => h.UpdateLayout(),
            [nameof(VirtualizedCollectionView.RemainingItemsThreshold)]               = (h, _) => h.UpdateThreshold(),
            [nameof(VirtualizedCollectionView.RemainingItemsThresholdReachedCommand)] = (h, _) => { },
        };

    private CollectionView _cv = null!;

    public VirtualizedCollectionViewHandler() : base(Mapper) { }

    protected override FrameworkElement CreatePlatformView()
    {
        _cv = new CollectionView();
        return (FrameworkElement)_cv.ToPlatform(MauiContext!);
    }

    protected override void ConnectHandler(FrameworkElement platformView)
    {
        base.ConnectHandler(platformView);
        _cv.RemainingItemsThresholdReached += OnThresholdReached;
        _cv.Scrolled += OnScrolled;
    }

    protected override void DisconnectHandler(FrameworkElement platformView)
    {
        _cv.RemainingItemsThresholdReached -= OnThresholdReached;
        _cv.Scrolled -= OnScrolled;
        _cv.Handler?.DisconnectHandler();
        base.DisconnectHandler(platformView);
    }

    private void UpdateItemsSource()  => _cv.ItemsSource  = VirtualView.ItemsSource;
    private void UpdateItemTemplate() => _cv.ItemTemplate = VirtualView.ItemTemplate;
    private void UpdateThreshold()    => _cv.RemainingItemsThreshold = VirtualView.RemainingItemsThreshold;

    private void UpdateLayout()
    {
        var orientation = VirtualView.Orientation == VirtualizedOrientation.Vertical
            ? ItemsLayoutOrientation.Vertical
            : ItemsLayoutOrientation.Horizontal;

        _cv.ItemsLayout = VirtualView.ColumnCount > 1
            ? new GridItemsLayout(VirtualView.ColumnCount, orientation)
            : new LinearItemsLayout(orientation);
    }

    private void OnThresholdReached(object? sender, EventArgs e) =>
        VirtualView.RaiseRemainingItemsThresholdReached();

    private void OnScrolled(object? sender, ItemsViewScrolledEventArgs e) =>
        VirtualView.RaiseScrolled(e.HorizontalOffset, e.VerticalOffset);
}

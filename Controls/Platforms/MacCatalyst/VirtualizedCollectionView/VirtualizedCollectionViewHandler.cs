// Platforms/MacCatalyst/VirtualizedCollectionView/VirtualizedCollectionViewHandler.cs
using System.Collections;
using System.Collections.Specialized;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;

namespace Controls.Platforms.iOS;

public sealed class VirtualizedCollectionViewHandler
    : ViewHandler<VirtualizedCollectionView, UICollectionView>
{
    public static readonly PropertyMapper<VirtualizedCollectionView, VirtualizedCollectionViewHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(VirtualizedCollectionView.ItemsSource)]  = (h, _) => h.ReloadItems(),
            [nameof(VirtualizedCollectionView.ItemTemplate)] = (h, _) => h.ReloadItems(),
            [nameof(VirtualizedCollectionView.ItemHeight)]   = (h, _) => h.RefreshLayout(),
            [nameof(VirtualizedCollectionView.ColumnCount)]  = (h, _) => h.RefreshLayout(),
            [nameof(VirtualizedCollectionView.Orientation)]  = (h, _) => h.RefreshLayout(),
            [nameof(VirtualizedCollectionView.RemainingItemsThreshold)]              = (h, _) => { },
            [nameof(VirtualizedCollectionView.RemainingItemsThresholdReachedCommand)] = (h, _) => { },
        };

    private static readonly NSString CellId = new("VrMauiCell");

    private VrDataSource?              _dataSource;
    private VrCollectionDelegate?      _delegate;
    private INotifyCollectionChanged?  _collectionChangedSource;

    public VirtualizedCollectionViewHandler() : base(Mapper) { }

    protected override UICollectionView CreatePlatformView()
    {
        var layout = BuildFlowLayout();
        var cv = new UICollectionView(CGRect.Empty, layout)
        {
            BackgroundColor        = UIColor.Clear,
            AlwaysBounceVertical   = true,
            AlwaysBounceHorizontal = false,
        };
        cv.RegisterClassForCell(typeof(VrMauiCell), CellId);
        return cv;
    }

    protected override void ConnectHandler(UICollectionView platformView)
    {
        base.ConnectHandler(platformView);
        _delegate = new VrCollectionDelegate(
            itemHeight:          () => VirtualView.ItemHeight,
            columnCount:         () => VirtualView.ColumnCount,
            onScrolled:          (x, y) => VirtualView?.RaiseScrolled(x, y),
            onDecelerationEnded: CheckRemainingThreshold);
        platformView.Delegate = _delegate;
        ReloadItems();
    }

    protected override void DisconnectHandler(UICollectionView platformView)
    {
        UnsubscribeCollection();
        platformView.Delegate   = null!;
        _delegate               = null;
        _dataSource?.Dispose();
        _dataSource             = null;
        platformView.DataSource = null!;
        base.DisconnectHandler(platformView);
    }

    private UICollectionViewFlowLayout BuildFlowLayout()
    {
        var horizontal = VirtualView?.Orientation == VirtualizedOrientation.Horizontal;
        return new UICollectionViewFlowLayout
        {
            ScrollDirection         = horizontal
                ? UICollectionViewScrollDirection.Horizontal
                : UICollectionViewScrollDirection.Vertical,
            MinimumInteritemSpacing = 0,
            MinimumLineSpacing      = 0,
            EstimatedItemSize       = VirtualView?.ItemHeight > 0
                ? CGSize.Empty
                : UICollectionViewFlowLayout.AutomaticSize,
        };
    }

    internal void RefreshLayout()
    {
        if (PlatformView is null) return;
        PlatformView.SetCollectionViewLayout(BuildFlowLayout(), animated: false);
        PlatformView.ReloadData();
    }

    private void ReloadItems()
    {
        if (PlatformView is null || MauiContext is null) return;

        UnsubscribeCollection();

        var items    = SnapshotItems(VirtualView.ItemsSource);
        var template = VirtualView.ItemTemplate;

        _dataSource?.Dispose();
        _dataSource = new VrDataSource(
            items:    items,
            template: template,
            mauiContext: MauiContext,
            cellId:   CellId);

        PlatformView.DataSource = _dataSource;
        PlatformView.ReloadData();

        SubscribeCollection(VirtualView.ItemsSource);
    }

    private void SubscribeCollection(IEnumerable? source)
    {
        if (source is INotifyCollectionChanged ncc)
        {
            _collectionChangedSource = ncc;
            ncc.CollectionChanged += OnCollectionChanged;
        }
    }

    private void UnsubscribeCollection()
    {
        if (_collectionChangedSource is not null)
        {
            _collectionChangedSource.CollectionChanged -= OnCollectionChanged;
            _collectionChangedSource = null;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_dataSource is null || PlatformView is null) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_dataSource is null || PlatformView is null) return;
            _dataSource.ApplyCollectionChange(e);
            PlatformView.ReloadData();
        });
    }

    private void CheckRemainingThreshold()
    {
        var threshold = VirtualView?.RemainingItemsThreshold ?? -1;
        if (threshold < 0 || _dataSource is null || PlatformView is null) return;

        var total       = _dataSource.Items.Count;
        var lastVisible = PlatformView.IndexPathsForVisibleItems
            .Select(ip => (int)ip.Row).DefaultIfEmpty(0).Max();

        if (total > 0 && total - 1 - lastVisible <= threshold)
            VirtualView?.RaiseRemainingItemsThresholdReached();
    }

    private static List<object> SnapshotItems(IEnumerable? source)
    {
        if (source is null) return [];
        var list = new List<object>();
        foreach (var item in source) list.Add(item);
        return list;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// VrMauiCell
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class VrMauiCell : UICollectionViewCell
{
    private View?   _mauiView;
    private UIView? _nativeView;

    [Export("initWithFrame:")]
    public VrMauiCell(CGRect frame) : base(frame) { }

    public void Bind(object item, DataTemplate template, IMauiContext context)
    {
        if (_mauiView is null)
        {
            _mauiView   = (View)template.CreateContent();
            _nativeView = _mauiView.ToPlatform(context);

            _nativeView.TranslatesAutoresizingMaskIntoConstraints = false;
            ContentView.AddSubview(_nativeView);
            NSLayoutConstraint.ActivateConstraints([
                _nativeView.TopAnchor.ConstraintEqualTo(ContentView.TopAnchor),
                _nativeView.BottomAnchor.ConstraintEqualTo(ContentView.BottomAnchor),
                _nativeView.LeadingAnchor.ConstraintEqualTo(ContentView.LeadingAnchor),
                _nativeView.TrailingAnchor.ConstraintEqualTo(ContentView.TrailingAnchor),
            ]);
        }

        _mauiView.BindingContext = item;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _mauiView?.Handler?.DisconnectHandler();
        base.Dispose(disposing);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// VrDataSource
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class VrDataSource : UICollectionViewDataSource
{
    private readonly DataTemplate _template;
    private readonly IMauiContext _mauiContext;
    private readonly NSString     _cellId;

    public List<object> Items { get; private set; }

    public VrDataSource(
        List<object> items,
        DataTemplate? template,
        IMauiContext mauiContext,
        NSString cellId)
    {
        Items        = items;
        _template    = template ?? new DataTemplate(typeof(Label));
        _mauiContext = mauiContext;
        _cellId      = cellId;
    }

    public override nint NumberOfSections(UICollectionView collectionView) => 1;

    public override nint GetItemsCount(UICollectionView collectionView, nint section)
        => Items.Count;

    public override UICollectionViewCell GetCell(UICollectionView collectionView, NSIndexPath indexPath)
    {
        var cell = (VrMauiCell)collectionView.DequeueReusableCell(_cellId, indexPath);
        if ((uint)indexPath.Row < (uint)Items.Count)
            cell.Bind(Items[(int)indexPath.Row], _template, _mauiContext);
        return cell;
    }

    public void ApplyCollectionChange(NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                Items.InsertRange(e.NewStartingIndex, e.NewItems.Cast<object>());
                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                Items.RemoveRange(e.OldStartingIndex, e.OldItems.Count);
                break;
            case NotifyCollectionChangedAction.Replace when e.NewItems is not null:
                for (int i = 0; i < e.NewItems.Count; i++)
                    Items[e.NewStartingIndex + i] = e.NewItems[i]!;
                break;
            case NotifyCollectionChangedAction.Move:
                var moved = Items[e.OldStartingIndex];
                Items.RemoveAt(e.OldStartingIndex);
                Items.Insert(e.NewStartingIndex, moved);
                break;
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// VrCollectionDelegate
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class VrCollectionDelegate : UICollectionViewDelegateFlowLayout
{
    private readonly Func<double>          _itemHeight;
    private readonly Func<int>             _columnCount;
    private readonly Action<double, double> _onScrolled;
    private readonly Action                _onDecelerationEnded;

    public VrCollectionDelegate(
        Func<double>           itemHeight,
        Func<int>              columnCount,
        Action<double, double> onScrolled,
        Action                 onDecelerationEnded)
    {
        _itemHeight          = itemHeight;
        _columnCount         = columnCount;
        _onScrolled          = onScrolled;
        _onDecelerationEnded = onDecelerationEnded;
    }

    public override CGSize GetSizeForItem(UICollectionView collectionView,
        UICollectionViewLayout layout, NSIndexPath indexPath)
    {
        var columns = Math.Max(1, _columnCount());
        var width   = collectionView.Bounds.Width / columns;
        var height  = _itemHeight();
        if (height <= 0) height = 44;
        return new CGSize(width, height);
    }

    public override void Scrolled(UIScrollView scrollView)
        => _onScrolled(scrollView.ContentOffset.X, scrollView.ContentOffset.Y);

    public override void DecelerationEnded(UIScrollView scrollView)
        => _onDecelerationEnded();

    public override void ScrollAnimationEnded(UIScrollView scrollView)
        => _onDecelerationEnded();
}

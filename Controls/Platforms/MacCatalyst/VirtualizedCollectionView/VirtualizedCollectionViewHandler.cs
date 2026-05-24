// Platforms/MacCatalyst/VirtualizedCollectionView/VirtualizedCollectionViewHandler.cs
//
// Arquitetura MacCatalyst:
//   UICollectionView (virtualização nativa)
//     ├── UICollectionViewCompositionalLayout  — sizing por coluna, self-sizing via Estimated
//     ├── VrDataSource                         — UICollectionViewDataSource
//     └── VrCollectionDelegate                 — scroll events + threshold
//
// Mudanças de células via PerformBatchUpdates — animações nativas, zero ReloadData incremental.

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
            [nameof(VirtualizedCollectionView.ItemsSource)]                            = (h, _) => h.ReloadItems(),
            [nameof(VirtualizedCollectionView.ItemTemplate)]                           = (h, _) => h.ReloadItems(),
            [nameof(VirtualizedCollectionView.ItemHeight)]                             = (h, _) => h.RefreshLayout(),
            [nameof(VirtualizedCollectionView.ColumnCount)]                            = (h, _) => h.RefreshLayout(),
            [nameof(VirtualizedCollectionView.Orientation)]                            = (h, _) => h.RefreshLayout(),
            [nameof(VirtualizedCollectionView.ItemSpacing)]                            = (h, _) => h.RefreshLayout(),
            [nameof(VirtualizedCollectionView.EmptyView)]                              = (h, _) => h.UpdateEmptyView(),
            [nameof(VirtualizedCollectionView.EmptyViewTemplate)]                      = (h, _) => h.UpdateEmptyView(),
            [nameof(VirtualizedCollectionView.RemainingItemsThreshold)]                = (h, _) => { },
            [nameof(VirtualizedCollectionView.RemainingItemsThresholdReachedCommand)]  = (h, _) => { },
            [nameof(VirtualizedCollectionView.ScrolledCommand)]                        = (h, _) => { },
        };

    public static readonly CommandMapper<VirtualizedCollectionView, VirtualizedCollectionViewHandler> Commands =
        new(ViewCommandMapper)
        {
            [nameof(VirtualizedCollectionView.ScrollTo)] = MapScrollTo,
        };

    private static readonly NSString CellId = new("VrMauiCell");

    private VrDataSource?             _dataSource;
    private VrCollectionDelegate?     _delegate;
    private INotifyCollectionChanged? _collectionChangedSource;
    private UIView?                   _emptyNativeView;

    public VirtualizedCollectionViewHandler() : base(Mapper, Commands) { }

    protected override UICollectionView CreatePlatformView()
    {
        var cv = new UICollectionView(CGRect.Empty, BuildCompositionalLayout())
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
            onScrolled:    (x, y) => VirtualView?.RaiseScrolled(x, y),
            onScrollEnded: CheckRemainingThreshold);
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

    // ── Layout ───────────────────────────────────────────────────────────────

    private UICollectionViewLayout BuildCompositionalLayout()
    {
        var columns    = Math.Max(1, VirtualView?.ColumnCount ?? 1);
        var itemHeight = VirtualView?.ItemHeight ?? -1;
        var horizontal = VirtualView?.Orientation == VirtualizedOrientation.Horizontal;

        // Dimensão da altura: absoluta quando fixada, estimada (self-sizing) caso contrário.
        var heightDim = itemHeight > 0
            ? NSCollectionLayoutDimension.CreateAbsolute((nfloat)itemHeight)
            : NSCollectionLayoutDimension.CreateEstimated(44);

        NSCollectionLayoutGroup group;
        if (!horizontal)
        {
            // Scroll vertical: cada grupo é uma linha com `columns` itens de largura igual.
            var itemSize  = NSCollectionLayoutSize.Create(
                NSCollectionLayoutDimension.CreateFractionalWidth((nfloat)(1.0 / columns)),
                heightDim);
            var groupSize = NSCollectionLayoutSize.Create(
                NSCollectionLayoutDimension.CreateFractionalWidth((nfloat)1),
                heightDim);
            var items = Enumerable.Range(0, columns)
                .Select(_ => NSCollectionLayoutItem.Create(itemSize))
                .ToArray();
            group = NSCollectionLayoutGroup.CreateHorizontal(groupSize, items);
        }
        else
        {
            // Scroll horizontal: cada grupo é uma coluna com `columns` itens de altura igual.
            var widthDim  = itemHeight > 0
                ? NSCollectionLayoutDimension.CreateAbsolute((nfloat)itemHeight)
                : NSCollectionLayoutDimension.CreateEstimated(44);
            var itemSize  = NSCollectionLayoutSize.Create(
                widthDim,
                NSCollectionLayoutDimension.CreateFractionalHeight((nfloat)(1.0 / columns)));
            var groupSize = NSCollectionLayoutSize.Create(
                widthDim,
                NSCollectionLayoutDimension.CreateFractionalHeight((nfloat)1));
            var items = Enumerable.Range(0, columns)
                .Select(_ => NSCollectionLayoutItem.Create(itemSize))
                .ToArray();
            group = NSCollectionLayoutGroup.CreateVertical(groupSize, items);
        }

        var spacing = (nfloat)(VirtualView?.ItemSpacing ?? 0);
        group.InterItemSpacing = NSCollectionLayoutSpacing.CreateFixed(spacing);
        var section = NSCollectionLayoutSection.Create(group);
        section.InterGroupSpacing = spacing;

        var config = new UICollectionViewCompositionalLayoutConfiguration
        {
            ScrollDirection = horizontal
                ? UICollectionViewScrollDirection.Horizontal
                : UICollectionViewScrollDirection.Vertical,
        };
        return new UICollectionViewCompositionalLayout(section, config);
    }

    internal void RefreshLayout()
    {
        if (PlatformView is null) return;
        PlatformView.SetCollectionViewLayout(BuildCompositionalLayout(), animated: false);
        PlatformView.ReloadData();
    }

    // ── Dados ────────────────────────────────────────────────────────────────

    private void ReloadItems()
    {
        if (PlatformView is null || MauiContext is null) return;

        UnsubscribeCollection();

        var items    = SnapshotItems(VirtualView.ItemsSource);
        var template = VirtualView.ItemTemplate;

        _dataSource?.Dispose();
        _dataSource = new VrDataSource(items, template, MauiContext, CellId);

        PlatformView.DataSource = _dataSource;
        PlatformView.ReloadData();

        SubscribeCollection(VirtualView.ItemsSource);
        UpdateEmptyVisibility(items.Count == 0);
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

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                    PlatformView.PerformBatchUpdates(() =>
                    {
                        _dataSource.ApplyCollectionChange(e);
                        PlatformView.InsertItems(IndexPaths(e.NewStartingIndex, e.NewItems.Count));
                    }, null);
                    break;

                case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                    PlatformView.PerformBatchUpdates(() =>
                    {
                        _dataSource.ApplyCollectionChange(e);
                        PlatformView.DeleteItems(IndexPaths(e.OldStartingIndex, e.OldItems.Count));
                    }, null);
                    break;

                case NotifyCollectionChangedAction.Replace when e.NewItems is not null:
                    PlatformView.PerformBatchUpdates(() =>
                    {
                        _dataSource.ApplyCollectionChange(e);
                        PlatformView.ReloadItems(IndexPaths(e.NewStartingIndex, e.NewItems.Count));
                    }, null);
                    break;

                case NotifyCollectionChangedAction.Move:
                    PlatformView.PerformBatchUpdates(() =>
                    {
                        _dataSource.ApplyCollectionChange(e);
                        PlatformView.MoveItem(
                            NSIndexPath.FromItemSection(e.OldStartingIndex, 0),
                            NSIndexPath.FromItemSection(e.NewStartingIndex, 0));
                    }, null);
                    break;

                default:
                    // Reset ou outros: re-snapshot completo da fonte.
                    ReloadItems();
                    return; // ReloadItems já chama UpdateEmptyVisibility
            }
            UpdateEmptyVisibility(_dataSource.Items.Count == 0);
        });
    }

    // ── Threshold / scroll ────────────────────────────────────────────────────

    private void CheckRemainingThreshold()
    {
        var threshold = VirtualView?.RemainingItemsThreshold ?? -1;
        if (threshold < 0 || _dataSource is null || PlatformView is null) return;

        var visiblePaths = PlatformView.IndexPathsForVisibleItems;
        if (visiblePaths.Length == 0) return;

        var total       = _dataSource.Items.Count;
        var lastVisible = visiblePaths.Max(ip => (int)ip.Item);

        if (total > 0 && total - 1 - lastVisible <= threshold)
            VirtualView?.RaiseRemainingItemsThresholdReached();
    }

    // ── EmptyView ─────────────────────────────────────────────────────────────

    private void UpdateEmptyView()
    {
        _emptyNativeView = BuildEmptyNativeView();
        UpdateEmptyVisibility(_dataSource is null || _dataSource.Items.Count == 0);
    }

    private void UpdateEmptyVisibility(bool isEmpty)
    {
        if (PlatformView is null) return;
        PlatformView.BackgroundView = isEmpty ? _emptyNativeView : null;
    }

    private UIView? BuildEmptyNativeView()
    {
        var emptyView     = VirtualView?.EmptyView;
        var emptyTemplate = VirtualView?.EmptyViewTemplate;

        if (emptyView is null && emptyTemplate is null) return null;

        if (emptyTemplate is not null)
        {
            var content = (View)emptyTemplate.CreateContent();
            if (emptyView is not null) content.BindingContext = emptyView;
            return content.ToPlatform(MauiContext!);
        }

        if (emptyView is View mauiView)
            return mauiView.ToPlatform(MauiContext!);

        var text = emptyView is string s ? s : emptyView?.ToString() ?? string.Empty;
        return new UILabel { Text = text, TextAlignment = UITextAlignment.Center, TextColor = UIColor.SecondaryLabel };
    }

    // ── ScrollTo ──────────────────────────────────────────────────────────────

    private static void MapScrollTo(
        VirtualizedCollectionViewHandler handler,
        VirtualizedCollectionView        view,
        object?                          arg)
    {
        if (arg is not VirtualizedCollectionView.ScrollToRequest req) return;
        if (handler.PlatformView is null || handler._dataSource is null) return;
        if ((uint)req.Index >= (uint)handler._dataSource.Items.Count) return;

        var indexPath = NSIndexPath.FromItemSection(req.Index, 0);
        handler.PlatformView.ScrollToItem(indexPath, UICollectionViewScrollPosition.Top, req.Animated);
    }

    private static NSIndexPath[] IndexPaths(int start, int count)
        => Enumerable.Range(start, count)
            .Select(i => NSIndexPath.FromItemSection(i, 0))
            .ToArray();

    private static List<object> SnapshotItems(IEnumerable? source)
    {
        if (source is null) return [];
        var list = new List<object>();
        foreach (var item in source) list.Add(item);
        return list;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// VrMauiCell — UICollectionViewCell com MAUI View lazy-criada e reutilizada
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

    public override void PrepareForReuse()
    {
        base.PrepareForReuse();
        if (_mauiView is not null)
            _mauiView.BindingContext = null;
    }

    // Garante que o self-sizing via CreateEstimated funcione: força o layout
    // da célula antes que o UIKit meça via systemLayoutSizeFitting.
    // [Export] necessário porque o binding .NET 10 não expõe este método como virtual.
    [Export("preferredLayoutAttributesFittingAttributes:")]
    public UICollectionViewLayoutAttributes PreferredLayoutAttributesFitting(
        UICollectionViewLayoutAttributes layoutAttributes)
    {
        SetNeedsLayout();
        LayoutIfNeeded();
        var size = ContentView.SystemLayoutSizeFittingSize(
            new CGSize(layoutAttributes.Frame.Width, UIView.UILayoutFittingCompressedSize.Height));
        layoutAttributes.Size = size;
        return layoutAttributes;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _mauiView?.Handler?.DisconnectHandler();
        base.Dispose(disposing);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// VrDataSource — UICollectionViewDataSource
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class VrDataSource : UICollectionViewDataSource
{
    private readonly DataTemplate _template;
    private readonly IMauiContext _mauiContext;
    private readonly NSString     _cellId;

    public List<object> Items { get; private set; }

    public VrDataSource(
        List<object>   items,
        DataTemplate?  template,
        IMauiContext   mauiContext,
        NSString       cellId)
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
        if ((uint)indexPath.Item < (uint)Items.Count)
            cell.Bind(Items[(int)indexPath.Item], _template, _mauiContext);
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
// VrCollectionDelegate — UICollectionViewDelegate
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class VrCollectionDelegate : UICollectionViewDelegate
{
    private readonly Action<double, double> _onScrolled;
    private readonly Action                 _onScrollEnded;

    public VrCollectionDelegate(
        Action<double, double> onScrolled,
        Action                 onScrollEnded)
    {
        _onScrolled    = onScrolled;
        _onScrollEnded = onScrollEnded;
    }

    public override void Scrolled(UIScrollView scrollView)
        => _onScrolled(scrollView.ContentOffset.X, scrollView.ContentOffset.Y);

    public override void DecelerationEnded(UIScrollView scrollView)
        => _onScrollEnded();

    // Cobre o caso em que o usuário arrasta devagar e solta sem decelerar.
    public override void DraggingEnded(UIScrollView scrollView, bool willDecelerate)
    {
        if (!willDecelerate) _onScrollEnded();
    }

    public override void ScrollAnimationEnded(UIScrollView scrollView)
        => _onScrollEnded();
}

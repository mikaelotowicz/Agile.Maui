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
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;
using VirtualizedOrientation = Agile.Maui.VirtualizedOrientation;
using ItemSizingStrategy     = Agile.Maui.ItemSizingStrategy;

namespace Agile.Maui.Platforms.iOS;

public sealed class VirtualizedCollectionViewHandler
    : ViewHandler<VirtualizedCollectionView, UICollectionView>
{
    public static readonly PropertyMapper<VirtualizedCollectionView, VirtualizedCollectionViewHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(VirtualizedCollectionView.ItemsSource)]                            = (h, _) => h.ScheduleReload(),
            [nameof(VirtualizedCollectionView.ItemTemplate)]                           = (h, _) => h.ScheduleReload(),
            [nameof(VirtualizedCollectionView.Header)]                                 = (h, _) => h.ScheduleReload(),
            [nameof(VirtualizedCollectionView.HeaderTemplate)]                         = (h, _) => h.ScheduleReload(),
            [nameof(VirtualizedCollectionView.Footer)]                                 = (h, _) => h.ScheduleReload(),
            [nameof(VirtualizedCollectionView.FooterTemplate)]                         = (h, _) => h.ScheduleReload(),
            [nameof(VirtualizedCollectionView.ItemHeight)]                             = (h, _) => h.RefreshLayout(),
            [nameof(VirtualizedCollectionView.ItemHeightRequest)]                      = (h, _) => h.RefreshLayout(),
            [nameof(VirtualizedCollectionView.ItemWidthRequest)]                       = (h, _) => h.RefreshLayout(),
            [nameof(VirtualizedCollectionView.ItemSizingStrategy)]                       = (h, _) => h.RefreshLayout(),
            [nameof(VirtualizedCollectionView.Span)]                            = (h, _) => h.RefreshLayout(),
            [nameof(VirtualizedCollectionView.Orientation)]                            = (h, _) => h.RefreshLayout(),
            [nameof(VirtualizedCollectionView.ItemSpacing)]                            = (h, _) => h.RefreshLayout(),
            [nameof(VirtualizedCollectionView.EmptyView)]                              = (h, _) => h.UpdateEmptyView(),
            [nameof(VirtualizedCollectionView.EmptyViewTemplate)]                      = (h, _) => h.UpdateEmptyView(),
            [nameof(VirtualizedCollectionView.RemainingItemsThreshold)]                = (h, _) => h.ResetRemainingThresholdGate(),
            [nameof(VirtualizedCollectionView.RemainingItemsThresholdReachedCommand)]  = (h, _) => h.ResetRemainingThresholdGate(),
            [nameof(VirtualizedCollectionView.ScrolledCommand)]                        = (h, _) => { },
            [nameof(VirtualizedCollectionView.VerticalScrollBarVisibility)]            = (h, _) => h.ApplyScrollIndicators(),
            [nameof(VirtualizedCollectionView.HorizontalScrollBarVisibility)]          = (h, _) => h.ApplyScrollIndicators(),
        };

    public static readonly CommandMapper<VirtualizedCollectionView, VirtualizedCollectionViewHandler> Commands =
        new(ViewCommandMapper)
        {
            [nameof(VirtualizedCollectionView.ScrollTo)] = MapScrollTo,
            [nameof(VirtualizedCollectionView.ScrollToStart)] = MapScrollToStart,
        };

    private static readonly NSString CellId = new("VrMauiCell");
    private const int HeaderSection = 0;
    private const int ItemsSection = 1;
    private const int FooterSection = 2;

    private VrDataSource?             _dataSource;
    private VrCollectionDelegate?     _delegate;
    private INotifyCollectionChanged? _collectionChangedSource;
    private UIView?                   _emptyNativeView;
    private readonly List<NotifyCollectionChangedEventArgs> _pendingChanges = [];
    private bool _flushScheduled;
    private bool _remainingThresholdInsideZone;
    private bool _remainingThresholdPending;
    private bool _hasLastScrollOffset;
    private bool _lastScrollWasTowardEnd;
    private double _lastScrollX;
    private double _lastScrollY;
    // Coalescing de ItemsSource + ItemTemplate: ambos disparam no connect via mapper —
    // sem isso ReloadData() seria chamado duas vezes antes do primeiro render.
    private bool _reloadScheduled;

    public VirtualizedCollectionViewHandler() : base(Mapper, Commands) { }

    protected override UICollectionView CreatePlatformView()
    {
        var cv = new UICollectionView(CGRect.Empty, BuildCompositionalLayout())
        {
            BackgroundColor = UIColor.Clear,
        };
        ApplyBounceDirection(cv);
        cv.RegisterClassForCell(typeof(VrMauiCell), CellId);
        return cv;
    }

    private void ApplyBounceDirection(UICollectionView cv)
    {
        var horizontal = VirtualView?.Orientation == VirtualizedOrientation.Horizontal;
        cv.AlwaysBounceVertical   = !horizontal;
        cv.AlwaysBounceHorizontal = horizontal;
    }

    protected override void ConnectHandler(UICollectionView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.PrefetchingEnabled = false;
        _delegate = new VrCollectionDelegate(
            onScrolled: OnScrolled,
            onScrollEnded: OnScrollEnded);
        platformView.Delegate = _delegate;
        ApplyScrollIndicators();
        // ReloadItems() não é necessário aqui — o mapper dispara ItemsSource e ItemTemplate
        // imediatamente após ConnectHandler, cobrindo a carga inicial sem duplicação.
    }

    // iOS/MacCatalyst: o indicador de rolagem sempre some sozinho após o gesto (não há
    // "sempre visível" nativo sem hacks). Never oculta; Default/Always exibem o indicador.
    private void ApplyScrollIndicators()
    {
        if (PlatformView is null || VirtualView is null) return;
        PlatformView.ShowsVerticalScrollIndicator =
            VirtualView.VerticalScrollBarVisibility != Microsoft.Maui.ScrollBarVisibility.Never;
        PlatformView.ShowsHorizontalScrollIndicator =
            VirtualView.HorizontalScrollBarVisibility != Microsoft.Maui.ScrollBarVisibility.Never;
    }

    protected override void DisconnectHandler(UICollectionView platformView)
    {
        UnsubscribeCollection();
        _flushScheduled  = false;
        _reloadScheduled = false;
        _pendingChanges.Clear();
        platformView.Delegate   = null!;
        _delegate               = null;
        _dataSource?.Dispose();
        _dataSource             = null;
        platformView.DataSource = null!;
        base.DisconnectHandler(platformView);
    }

    // ── Coalescing de ReloadItems ─────────────────────────────────────────────

    private void ScheduleReload()
    {
        if (_reloadScheduled) return;
        _reloadScheduled = true;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _reloadScheduled = false;
            ReloadItems();
        });
    }

    // ── Layout ───────────────────────────────────────────────────────────────

    private UICollectionViewLayout BuildCompositionalLayout()
    {
        var config = new UICollectionViewCompositionalLayoutConfiguration
        {
            ScrollDirection = VirtualView?.Orientation == VirtualizedOrientation.Horizontal
                ? UICollectionViewScrollDirection.Horizontal
                : UICollectionViewScrollDirection.Vertical,
        };

        return new UICollectionViewCompositionalLayout((sectionIndex, _) =>
            sectionIndex == HeaderSection || sectionIndex == FooterSection
                ? BuildStructuralSection()
                : BuildItemsSection(), config);
    }

    private NSCollectionLayoutSection BuildItemsSection()
    {
        var columns    = Math.Max(1, VirtualView?.Span ?? 1);
        var itemHeight = VirtualView?.ItemHeight ?? -1;
        var itemWidthRequest = VirtualView?.ItemWidthRequest ?? -1;
        var horizontal = VirtualView?.Orientation == VirtualizedOrientation.Horizontal;

        // ItemHeight > 0  →  CreateAbsolute(itemHeight): altura explícita, sobrepõe tudo.
        // Fixed            →  CreateAbsolute(ItemHeightRequest): altura fixa sem per-cell measure.
        // Dynamic          →  CreateEstimated(ItemHeightRequest): self-sizing via
        //                     PreferredLayoutAttributesFitting, suporta expanders e conteúdo variável.
        // MeasureFirst     →  Estimated até medir o 1º item; depois CreateAbsolute(altura medida)
        //                     para todos (sem per-cell measure a partir daí).
        var estimatedH  = (nfloat)Math.Max(44, VirtualView?.ItemHeightRequest ?? 350);
        var strategy    = VirtualView?.ItemSizingStrategy ?? ItemSizingStrategy.Fixed;
        var measuredFirst = strategy == ItemSizingStrategy.MeasureFirst && _measureFirstHeight > 0;
        var useAbsolute = itemHeight > 0 || strategy == ItemSizingStrategy.Fixed || measuredFirst;
        var absoluteH   = itemHeight > 0 ? (nfloat)itemHeight
                        : measuredFirst ? _measureFirstHeight
                        : estimatedH;
        var absoluteW   = itemWidthRequest > 0 ? (nfloat)itemWidthRequest : absoluteH;
        var heightDim   = useAbsolute
            ? NSCollectionLayoutDimension.CreateAbsolute(absoluteH)
            : NSCollectionLayoutDimension.CreateEstimated(estimatedH);

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
            var items = CreateLayoutItems(columns, itemSize);
            group = NSCollectionLayoutGroup.CreateHorizontal(groupSize, items);
        }
        else
        {
            // Scroll horizontal: cada grupo é uma coluna com `columns` itens de altura igual.
            var widthDim = itemWidthRequest > 0
                ? NSCollectionLayoutDimension.CreateAbsolute(absoluteW)
                : useAbsolute
                    ? NSCollectionLayoutDimension.CreateAbsolute(absoluteH)
                    : NSCollectionLayoutDimension.CreateEstimated(estimatedH);
            var itemSize  = NSCollectionLayoutSize.Create(
                widthDim,
                NSCollectionLayoutDimension.CreateFractionalHeight((nfloat)(1.0 / columns)));
            var groupSize = NSCollectionLayoutSize.Create(
                widthDim,
                NSCollectionLayoutDimension.CreateFractionalHeight((nfloat)1));
            var items = CreateLayoutItems(columns, itemSize);
            group = NSCollectionLayoutGroup.CreateVertical(groupSize, items);
        }

        var spacing = (nfloat)(VirtualView?.ItemSpacing ?? 0);
        group.InterItemSpacing = NSCollectionLayoutSpacing.CreateFixed(spacing);
        var section = NSCollectionLayoutSection.Create(group);
        section.InterGroupSpacing = spacing;
        return section;
    }

    private NSCollectionLayoutSection BuildStructuralSection()
    {
        var horizontal = VirtualView?.Orientation == VirtualizedOrientation.Horizontal;
        var estimated = (nfloat)Math.Max(
            44,
            horizontal && VirtualView?.ItemWidthRequest > 0
                ? VirtualView.ItemWidthRequest
                : VirtualView?.ItemHeightRequest ?? 350);
        var size = horizontal
            ? NSCollectionLayoutSize.Create(
                NSCollectionLayoutDimension.CreateEstimated(estimated),
                NSCollectionLayoutDimension.CreateFractionalHeight((nfloat)1))
            : NSCollectionLayoutSize.Create(
                NSCollectionLayoutDimension.CreateFractionalWidth((nfloat)1),
                NSCollectionLayoutDimension.CreateEstimated(estimated));

        var item = NSCollectionLayoutItem.Create(size);
        var group = horizontal
            ? NSCollectionLayoutGroup.CreateHorizontal(size, [item])
            : NSCollectionLayoutGroup.CreateVertical(size, [item]);

        return NSCollectionLayoutSection.Create(group);
    }

    private static NSCollectionLayoutItem[] CreateLayoutItems(int count, NSCollectionLayoutSize itemSize)
    {
        var items = new NSCollectionLayoutItem[count];
        for (var i = 0; i < count; i++)
            items[i] = NSCollectionLayoutItem.Create(itemSize);
        return items;
    }

    // Altura medida do 1º item no modo MeasureFirst (0 = ainda não medido).
    private nfloat _measureFirstHeight;

    internal void RefreshLayout()
    {
        if (PlatformView is null) return;
        _measureFirstHeight = 0;   // mudou layout/estratégia/altura → re-mede o 1º item
        PlatformView.SetCollectionViewLayout(BuildCompositionalLayout(), animated: false);
        ApplyBounceDirection(PlatformView);
        PlatformView.ReloadData();
    }

    // Chamado pela 1ª célula medida quando ItemSizingStrategy == MeasureFirst:
    // fixa a altura medida e reconstrói o layout como Absolute para todos os itens.
    internal void OnFirstCellMeasured(nfloat height)
    {
        if (PlatformView is null || height <= 0) return;
        if (VirtualView?.ItemSizingStrategy != ItemSizingStrategy.MeasureFirst) return;
        if (_measureFirstHeight > 0) return;   // já fixado

        _measureFirstHeight = height;
        PlatformView.BeginInvokeOnMainThread(() =>
        {
            if (PlatformView is null) return;
            PlatformView.SetCollectionViewLayout(BuildCompositionalLayout(), animated: false);
        });
    }

    // ── Dados ────────────────────────────────────────────────────────────────

    private void ReloadItems()
    {
        if (PlatformView is null || MauiContext is null) return;
        ResetRemainingThresholdGate();

        // Recarga total ressincroniza a partir do snapshot atual da fonte, que já reflete
        // toda mutação ocorrida até aqui. Qualquer evento incremental ainda enfileirado é
        // redundante — reaplicá-lo duplicaria itens (clássico: Reset seguido de Add no mesmo
        // ciclo, pois o evento Add é enfileirado APÓS o Reset limpar a fila). Descarta a fila
        // ao recarregar para manter a contagem em sincronia com a UICollectionView.
        _pendingChanges.Clear();
        _flushScheduled = false;

        UnsubscribeCollection();

        var items    = SnapshotItems(VirtualView.ItemsSource);
        var template = VirtualView.ItemTemplate;

        _dataSource?.Dispose();
        _dataSource = new VrDataSource(
            items,
            template,
            MauiContext,
            CellId,
            VirtualView.Header,
            VirtualView.HeaderTemplate,
            VirtualView.Footer,
            VirtualView.FooterTemplate)
        {
            ReportFirstMeasure = VirtualView.ItemSizingStrategy == ItemSizingStrategy.MeasureFirst
                ? OnFirstCellMeasured
                : null,
        };

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

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _pendingChanges.Clear();
            _flushScheduled = false;
            MainThread.BeginInvokeOnMainThread(ReloadItems);
            return;
        }

        _pendingChanges.Add(e);
        if (!_flushScheduled)
        {
            _flushScheduled = true;
            MainThread.BeginInvokeOnMainThread(FlushPendingChanges);
        }
    }

    private void FlushPendingChanges()
    {
        _flushScheduled = false;
        if (_dataSource is null || PlatformView is null || _pendingChanges.Count == 0) return;

        var pending = _pendingChanges.ToArray();
        _pendingChanges.Clear();

        var firstAction = pending[0].Action;
        var isMixed     = Array.Exists(pending, e => e.Action != firstAction);
        var hasMove     = Array.Exists(pending, e => e.Action == NotifyCollectionChangedAction.Move);

        if (pending.Length > 30 ||
            isMixed ||
            hasMove ||
            !CanApplyPendingChanges(pending, _dataSource.Items.Count))
        {
            ReloadItems();
            return;
        }

        PlatformView.PerformBatchUpdates(() =>
        {
            foreach (var e in pending)
            {
                _dataSource.ApplyCollectionChange(e);
                switch (e.Action)
                {
                    case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                        PlatformView.InsertItems(IndexPaths(e.NewStartingIndex, e.NewItems.Count, ItemsSection));
                        break;
                    case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                        PlatformView.DeleteItems(IndexPaths(e.OldStartingIndex, e.OldItems.Count, ItemsSection));
                        break;
                    case NotifyCollectionChangedAction.Replace when e.NewItems is not null:
                        PlatformView.ReloadItems(IndexPaths(e.NewStartingIndex, e.NewItems.Count, ItemsSection));
                        break;
                }
            }
        }, null);

        ResetRemainingThresholdGate();
        UpdateEmptyVisibility(_dataSource.Items.Count == 0);
    }

    private static bool CanApplyPendingChanges(NotifyCollectionChangedEventArgs[] pending, int currentCount)
    {
        foreach (var e in pending)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                    if (e.NewItems.Count <= 0 ||
                        e.NewStartingIndex < 0 ||
                        e.NewStartingIndex > currentCount)
                        return false;
                    currentCount += e.NewItems.Count;
                    break;
                case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                    if (e.OldItems.Count <= 0 ||
                        e.OldStartingIndex < 0 ||
                        e.OldStartingIndex + e.OldItems.Count > currentCount)
                        return false;
                    currentCount -= e.OldItems.Count;
                    break;
                case NotifyCollectionChangedAction.Replace when e.NewItems is not null && e.OldItems is not null:
                    if (e.NewItems.Count <= 0 ||
                        e.NewItems.Count != e.OldItems.Count ||
                        e.NewStartingIndex < 0 ||
                        e.NewStartingIndex + e.NewItems.Count > currentCount)
                        return false;
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    // ── Threshold / scroll ────────────────────────────────────────────────────

    private void OnScrolled(double x, double y)
    {
        var view = VirtualView;
        if (view is null) return;

        var dx = _hasLastScrollOffset ? x - _lastScrollX : 0;
        var dy = _hasLastScrollOffset ? y - _lastScrollY : 0;
        _lastScrollX = x;
        _lastScrollY = y;
        _hasLastScrollOffset = true;

        _lastScrollWasTowardEnd = IsScrollingTowardEnd(dx, dy);

        if (view.HasScrolledObservers)
            view.RaiseScrolled(x, y);

        if (view.RemainingItemsThreshold >= 0 &&
            view.CanRaiseRemainingItemsThresholdReached &&
            (_lastScrollWasTowardEnd || _remainingThresholdInsideZone))
        {
            _remainingThresholdPending = true;
            CheckRemainingThreshold();
        }
    }

    private void OnScrollEnded()
    {
        if (_remainingThresholdPending || _lastScrollWasTowardEnd || _remainingThresholdInsideZone)
            CheckRemainingThreshold();
    }

    private bool IsScrollingTowardEnd(double dx, double dy) =>
        VirtualView?.Orientation == VirtualizedOrientation.Horizontal ? dx > 0 : dy > 0;

    private void CheckRemainingThreshold()
    {
        var view = VirtualView;
        var threshold = view?.RemainingItemsThreshold ?? -1;
        if (threshold < 0 ||
            _dataSource is null ||
            PlatformView is null ||
            view?.CanRaiseRemainingItemsThresholdReached != true)
            return;

        _remainingThresholdPending = false;
        var total = _dataSource.Items.Count;
        if (total <= 0)
        {
            _remainingThresholdInsideZone = false;
            _lastScrollWasTowardEnd = false;
            return;
        }

        var visiblePaths = PlatformView.IndexPathsForVisibleItems;
        if (visiblePaths.Length == 0) return;

        var lastVisible = -1;
        foreach (var ip in visiblePaths)
        {
            if (ip.Section != ItemsSection)
                continue;

            var idx = (int)ip.Item;
            if (idx > lastVisible) lastVisible = idx;
        }
        if (lastVisible < 0) return;

        var insideZone = total - 1 - lastVisible <= threshold;
        if (!insideZone)
        {
            _remainingThresholdInsideZone = false;
            _lastScrollWasTowardEnd = false;
            return;
        }

        if (_remainingThresholdInsideZone || view?.CanRaiseRemainingItemsThresholdReached != true)
            return;

        _remainingThresholdInsideZone = true;
        _lastScrollWasTowardEnd = false;
        view.RaiseRemainingItemsThresholdReached();
    }

    private void ResetRemainingThresholdGate()
    {
        _remainingThresholdInsideZone = false;
        _remainingThresholdPending = false;
        _hasLastScrollOffset = false;
        _lastScrollWasTowardEnd = false;
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

        var indexPath = NSIndexPath.FromItemSection(req.Index, ItemsSection);
        var position = view.Orientation == VirtualizedOrientation.Horizontal
            ? UICollectionViewScrollPosition.Left
            : UICollectionViewScrollPosition.Top;
        handler.PlatformView.ScrollToItem(indexPath, position, req.Animated);
    }

    private static void MapScrollToStart(
        VirtualizedCollectionViewHandler handler,
        VirtualizedCollectionView        view,
        object?                          arg)
    {
        if (arg is not VirtualizedCollectionView.ScrollToRequest req) return;
        handler.PlatformView?.SetContentOffset(CGPoint.Empty, req.Animated);
    }

    private static NSIndexPath[] IndexPaths(int start, int count, int section)
    {
        var paths = new NSIndexPath[count];
        for (var i = 0; i < count; i++)
            paths[i] = NSIndexPath.FromItemSection(start + i, section);
        return paths;
    }

    private static List<object> SnapshotItems(IEnumerable? source)
    {
        if (source is null) return [];
        var capacity = source is System.Collections.ICollection c ? c.Count : 0;
        var list     = new List<object>(capacity > 0 ? capacity : 16);
        foreach (var item in source) list.Add(item);
        return list;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// VrMauiCell — UICollectionViewCell com MAUI View lazy-criada e reutilizada
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class VrMauiCell : UICollectionViewCell
{
    private View?             _mauiView;
    private UIView?           _nativeView;
    private UICollectionView? _collectionView;
    private DataTemplate?     _template;
    private View?             _directView;
    private bool              _usesGeneratedLabel;
    private bool              _measureInvalidated;
    private bool              _layoutStabilized;
    private Action<nfloat>?   _reportFirstMeasure;

    [Export("initWithFrame:")]
    public VrMauiCell(CGRect frame) : base(frame) { }

    public void Bind(object? item, DataTemplate? template, IMauiContext context, UICollectionView collectionView,
        Action<nfloat>? reportFirstMeasure = null)
    {
        _collectionView     = collectionView;
        _reportFirstMeasure = reportFirstMeasure;
        var directView = template is null ? item as View : null;

        // Recria a view quando o template muda em runtime; sem esse check,
        // cells do pool reuse manteriam a hierarquia do template antigo.
        if (_mauiView is null ||
            !ReferenceEquals(_template, template) ||
            !ReferenceEquals(_directView, directView) ||
            (_usesGeneratedLabel && (template is not null || directView is not null)))
        {
            if (_mauiView is not null)
            {
                _mauiView.MeasureInvalidated -= OnMauiMeasureInvalidated;
                if (_directView is null)
                    _mauiView.BindingContext = null;
                _mauiView.Handler?.DisconnectHandler();
                _nativeView?.RemoveFromSuperview();
                _nativeView       = null;
                _mauiView         = null;
                _layoutStabilized = false;
            }
            _template   = template;
            _directView = directView;
            _usesGeneratedLabel = template is null && directView is null;
            _mauiView   = CreateMauiView(item, template);
            _nativeView = _mauiView.ToPlatform(context);
            _mauiView.MeasureInvalidated += OnMauiMeasureInvalidated;

            // Frame-based layout: MAUI gerencia o posicionamento via Arrange(),
            // evitando conflito entre Auto Layout e o sistema de layout do MAUI.
            _nativeView.AutoresizingMask =
                UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
            ContentView.AddSubview(_nativeView);
        }

        if (_usesGeneratedLabel && _mauiView is Label label)
            label.Text = item?.ToString() ?? string.Empty;

        if (directView is null && !ReferenceEquals(_mauiView.BindingContext, item))
            _mauiView.BindingContext = item;
        SetNeedsLayout();
    }

    private static View CreateMauiView(object? item, DataTemplate? template)
    {
        if (template is not null)
        {
            var content = template.CreateContent();
            if (content is View view)
                return view;
        }

        if (item is View directView)
            return directView;

        return new Label
        {
            Text = item?.ToString() ?? string.Empty,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center
        };
    }

    private void OnMauiMeasureInvalidated(object? sender, EventArgs e)
    {
        // Ignora invalidações durante o setup inicial (BindingContext recém-atribuído
        // ainda não completou um ciclo PreferredLayoutAttributesFitting). Sem esse
        // guard, o BindingContext dispara MeasureInvalidated → InvalidateLayout() →
        // novos cells → BindingContext → loop infinito travando a lista.
        if (!_layoutStabilized) return;
        _measureInvalidated = true;
        SetNeedsLayout();
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        if (_mauiView is null || _nativeView is null) return;
        var bounds = ContentView.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        _nativeView.Frame = bounds;
        ((IView)_mauiView).Arrange(new Rect(0, 0, bounds.Width, bounds.Height));

        if (_measureInvalidated)
        {
            _measureInvalidated = false;
            // UIKit chamará PreferredLayoutAttributesFitting → nova altura → resize animado.
            _collectionView?.CollectionViewLayout.InvalidateLayout();
        }
    }

    public override void PrepareForReuse()
    {
        base.PrepareForReuse();
        _measureInvalidated = false;
        _layoutStabilized   = false;
    }

    // Self-sizing via CompositionalLayout (CreateEstimated): o UIKit chama este método
    // para obter a altura real da célula. Usamos MAUI Measure porque MAUI views não
    // expõem IntrinsicContentSize para Auto Layout — SystemLayoutSizeFittingSize retorna
    // height=0, tornando as células invisíveis.
    // [Export] necessário porque o binding .NET 10 não expõe este método como virtual.
    [Export("preferredLayoutAttributesFittingAttributes:")]
    public UICollectionViewLayoutAttributes PreferredLayoutAttributesFitting(
        UICollectionViewLayoutAttributes layoutAttributes)
    {
        if (_mauiView is null) return layoutAttributes;

        var width = layoutAttributes.Frame.Width;
        if (width <= 0) return layoutAttributes;

        // Mede a view com a largura da coluna (determinada pelo CompositionalLayout)
        // e altura livre — MAUI calcula a altura necessária para o conteúdo.
        var measured = ((IView)_mauiView).Measure(width, double.PositiveInfinity);
        var height   = Math.Max(1, measured.Height);

        // MeasureFirst: reporta a altura medida; o handler fixa para todos e reconstrói Absolute.
        _reportFirstMeasure?.Invoke((nfloat)height);

        // Célula medida pelo UIKit: a partir daqui pode reagir a MeasureInvalidated
        // (ex: expander abre/fecha) sem risco de loop no setup inicial.
        _layoutStabilized = true;

        layoutAttributes.Frame = new CGRect(
            layoutAttributes.Frame.X,
            layoutAttributes.Frame.Y,
            width,
            (nfloat)height);

        return layoutAttributes;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_mauiView is not null)
            {
                _mauiView.MeasureInvalidated -= OnMauiMeasureInvalidated;
                if (_directView is null)
                    _mauiView.BindingContext = null;
            }
            _mauiView?.Handler?.DisconnectHandler();
            _nativeView?.RemoveFromSuperview();
            _collectionView = null;
            _nativeView     = null;
            _mauiView       = null;
            _template       = null;
            _directView     = null;
        }
        base.Dispose(disposing);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// VrDataSource — UICollectionViewDataSource
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class VrDataSource : UICollectionViewDataSource
{
    private const int HeaderSection = 0;
    private const int ItemsSection = 1;
    private const int FooterSection = 2;

    private readonly DataTemplate _template;
    private readonly IMauiContext _mauiContext;
    private readonly NSString     _cellId;
    private readonly object?      _header;
    private readonly DataTemplate? _headerTemplate;
    private readonly object?      _footer;
    private readonly DataTemplate? _footerTemplate;

    // Callback opcional: a 1ª célula medida reporta a altura (modo MeasureFirst).
    internal Action<nfloat>? ReportFirstMeasure;

    public List<object> Items { get; private set; }

    public VrDataSource(
        List<object>   items,
        DataTemplate?  template,
        IMauiContext   mauiContext,
        NSString       cellId,
        object?        header,
        DataTemplate?  headerTemplate,
        object?        footer,
        DataTemplate?  footerTemplate)
    {
        Items        = items;
        _template    = template ?? new DataTemplate(typeof(Label));
        _mauiContext = mauiContext;
        _cellId      = cellId;
        _header      = header;
        _headerTemplate = headerTemplate;
        _footer      = footer;
        _footerTemplate = footerTemplate;
    }

    public override nint NumberOfSections(UICollectionView collectionView) => 3;

    public override nint GetItemsCount(UICollectionView collectionView, nint section)
    {
        return (int)section switch
        {
            HeaderSection => _header is not null || _headerTemplate is not null ? 1 : 0,
            ItemsSection => Items.Count,
            FooterSection => _footer is not null || _footerTemplate is not null ? 1 : 0,
            _ => 0
        };
    }

    public override UICollectionViewCell GetCell(UICollectionView collectionView, NSIndexPath indexPath)
    {
        var cell = (VrMauiCell)collectionView.DequeueReusableCell(_cellId, indexPath);
        if (indexPath.Section == HeaderSection)
        {
            cell.Bind(_header, _headerTemplate, _mauiContext, collectionView);
        }
        else if (indexPath.Section == FooterSection)
        {
            cell.Bind(_footer, _footerTemplate, _mauiContext, collectionView);
        }
        else if ((uint)indexPath.Item < (uint)Items.Count)
        {
            cell.Bind(Items[(int)indexPath.Item], _template, _mauiContext, collectionView, ReportFirstMeasure);
        }

        return cell;
    }

    public void ApplyCollectionChange(NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                Items.InsertRange(e.NewStartingIndex, CopyItems(e.NewItems));
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

    private static List<object> CopyItems(IList items)
    {
        var list = new List<object>(items.Count);
        foreach (var item in items)
            list.Add(item!);
        return list;
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

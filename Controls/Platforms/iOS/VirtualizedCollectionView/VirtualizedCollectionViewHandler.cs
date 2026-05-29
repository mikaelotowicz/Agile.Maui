// Platforms/iOS/VirtualizedCollectionView/VirtualizedCollectionViewHandler.cs
//
// Arquitetura iOS:
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
using ItemsLayoutOrientation = Agile.Maui.ItemsLayoutOrientation;
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
            [nameof(VirtualizedCollectionView.ItemHeight)]                             = (h, _) => h.RefreshLayout(),
            [nameof(VirtualizedCollectionView.ItemHeightRequest)]                      = (h, _) => h.RefreshLayout(),
            [nameof(VirtualizedCollectionView.ItemSizingStrategy)]                       = (h, _) => h.RefreshLayout(),
            [nameof(VirtualizedCollectionView.Span)]                            = (h, _) => h.RefreshLayout(),
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
    // Coalescing de CollectionChanged: acumula eventos rápidos (ex: 500 × Items.Add)
    // em um único PerformBatchUpdates, evitando 500 dispatches individuais ao UIKit.
    private readonly List<NotifyCollectionChangedEventArgs> _pendingChanges = [];
    private bool _flushScheduled;
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
        var horizontal = VirtualView?.Orientation == ItemsLayoutOrientation.Horizontal;
        cv.AlwaysBounceVertical   = !horizontal;
        cv.AlwaysBounceHorizontal = horizontal;
    }

    protected override void ConnectHandler(UICollectionView platformView)
    {
        base.ConnectHandler(platformView);
        // Prefetching agressivo cria células extras fora da tela antes de serem necessárias,
        // multiplicando a memória consumida por cada MAUI View no pool de reuse.
        platformView.PrefetchingEnabled = false;
        _delegate = new VrCollectionDelegate(
            onScrolled:    (x, y) => VirtualView?.RaiseScrolled(x, y),
            onScrollEnded: CheckRemainingThreshold);
        platformView.Delegate = _delegate;
        // ReloadItems() não é necessário aqui — o mapper dispara ItemsSource e ItemTemplate
        // imediatamente após ConnectHandler, cobrindo a carga inicial sem duplicação.
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

    // Garante que múltiplos mappers disparados no mesmo ciclo (ex: ItemsSource + ItemTemplate
    // no connect) resultem em um único ReloadItems() — evita ReloadData() duplo no UIKit.
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
        var columns    = Math.Max(1, VirtualView?.Span ?? 1);
        var itemHeight = VirtualView?.ItemHeight ?? -1;
        var horizontal = VirtualView?.Orientation == ItemsLayoutOrientation.Horizontal;

        // ItemHeight > 0  →  CreateAbsolute(itemHeight): altura explícita, sobrepõe tudo.
        // Fixed            →  CreateAbsolute(ItemHeightRequest): altura fixa sem per-cell measure.
        // Dynamic          →  CreateEstimated(ItemHeightRequest): self-sizing via
        //                     PreferredLayoutAttributesFitting, suporta expanders e conteúdo variável.
        var estimatedH  = (nfloat)Math.Max(44, VirtualView?.ItemHeightRequest ?? 350);
        var useAbsolute = itemHeight > 0 || VirtualView?.ItemSizingStrategy == ItemSizingStrategy.MeasureFirstItem;
        var absoluteH   = itemHeight > 0 ? (nfloat)itemHeight : estimatedH;
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
            var items = Enumerable.Range(0, columns)
                .Select(_ => NSCollectionLayoutItem.Create(itemSize))
                .ToArray();
            group = NSCollectionLayoutGroup.CreateHorizontal(groupSize, items);
        }
        else
        {
            // Scroll horizontal: cada grupo é uma coluna com `columns` itens de altura igual.
            var widthDim = useAbsolute
                ? NSCollectionLayoutDimension.CreateAbsolute(absoluteH)
                : NSCollectionLayoutDimension.CreateEstimated(estimatedH);
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
        ApplyBounceDirection(PlatformView);
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

        // Reset precisa de snapshot completo — descarta qualquer fila pendente.
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _pendingChanges.Clear();
            _flushScheduled = false;
            MainThread.BeginInvokeOnMainThread(ReloadItems);
            return;
        }

        // Acumula o evento; agenda flush apenas uma vez por ciclo de run loop.
        // Isso coalesce 500 × Add (de foreach Items.Add) em um único PerformBatchUpdates.
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

        var pending = _pendingChanges.ToArray(); // snapshot local
        _pendingChanges.Clear();

        // Lote grande ou misto (Add + Remove, Move, etc.) → snapshot fresco é mais seguro.
        // Threshold 30: abaixo disso, anima; acima, ReloadData é mais rápido e menos arriscado.
        var firstAction = pending[0].Action;
        var isMixed     = Array.Exists(pending, e => e.Action != firstAction);
        var hasMove     = Array.Exists(pending, e => e.Action == NotifyCollectionChangedAction.Move);

        if (pending.Length > 30 || isMixed || hasMove)
        {
            ReloadItems();
            return;
        }

        // Lote pequeno e uniforme: aplica todos em um único PerformBatchUpdates.
        PlatformView.PerformBatchUpdates(() =>
        {
            foreach (var e in pending)
            {
                _dataSource.ApplyCollectionChange(e);
                switch (e.Action)
                {
                    case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                        PlatformView.InsertItems(IndexPaths(e.NewStartingIndex, e.NewItems.Count));
                        break;
                    case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                        PlatformView.DeleteItems(IndexPaths(e.OldStartingIndex, e.OldItems.Count));
                        break;
                    case NotifyCollectionChangedAction.Replace when e.NewItems is not null:
                        PlatformView.ReloadItems(IndexPaths(e.NewStartingIndex, e.NewItems.Count));
                        break;
                }
            }
        }, null);

        UpdateEmptyVisibility(_dataSource.Items.Count == 0);
    }

    // ── Threshold / scroll ────────────────────────────────────────────────────

    private void CheckRemainingThreshold()
    {
        var threshold = VirtualView?.RemainingItemsThreshold ?? -1;
        if (threshold < 0 || _dataSource is null || PlatformView is null) return;

        var visiblePaths = PlatformView.IndexPathsForVisibleItems;
        if (visiblePaths.Length == 0) return;

        // Loop manual — evita closure LINQ + boxing de nint para cada NSIndexPath.
        var lastVisible = -1;
        foreach (var ip in visiblePaths)
        {
            var idx = (int)ip.Item;
            if (idx > lastVisible) lastVisible = idx;
        }
        if (lastVisible < 0) return;

        var total = _dataSource.Items.Count;
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
        // Pré-aloca quando o source expõe Count (ObservableCollection, List, Array, etc.),
        // evitando as realocações geométricas do List<T> para 500+ itens.
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
    private bool              _measureInvalidated;
    private bool              _layoutStabilized;

    [Export("initWithFrame:")]
    public VrMauiCell(CGRect frame) : base(frame) { }

    public void Bind(object item, DataTemplate template, IMauiContext context, UICollectionView collectionView)
    {
        _collectionView = collectionView;

        // Recria a view quando o template muda em runtime; sem esse check,
        // cells do pool reuse manteriam a hierarquia do template antigo.
        if (_mauiView is null || !ReferenceEquals(_template, template))
        {
            if (_mauiView is not null)
            {
                _mauiView.MeasureInvalidated -= OnMauiMeasureInvalidated;
                _mauiView.Handler?.DisconnectHandler();
                _nativeView?.RemoveFromSuperview();
                _nativeView       = null;
                _mauiView         = null;
                _layoutStabilized = false;
            }
            _template   = template;
            _mauiView   = (View)template.CreateContent();
            _nativeView = _mauiView.ToPlatform(context);
            _mauiView.MeasureInvalidated += OnMauiMeasureInvalidated;

            // Frame-based layout: MAUI gerencia o posicionamento via Arrange(),
            // evitando conflito entre Auto Layout e o sistema de layout do MAUI.
            _nativeView.AutoresizingMask =
                UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
            ContentView.AddSubview(_nativeView);
        }

        _mauiView.BindingContext = item;
        SetNeedsLayout();
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
        if (_mauiView is not null)
            _mauiView.BindingContext = null;
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
                _mauiView.MeasureInvalidated -= OnMauiMeasureInvalidated;
            _mauiView?.Handler?.DisconnectHandler();
            _nativeView?.RemoveFromSuperview();
            _collectionView = null;
            _nativeView     = null;
            _mauiView       = null;
            _template       = null;
        }
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
            cell.Bind(Items[(int)indexPath.Item], _template, _mauiContext, collectionView);
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

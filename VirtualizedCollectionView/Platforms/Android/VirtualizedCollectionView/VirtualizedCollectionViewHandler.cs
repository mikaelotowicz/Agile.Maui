// Platforms/Android/VirtualizedCollectionView/VirtualizedCollectionViewHandler.cs
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.Versioning;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Util;
using Bumptech.Glide;
using Android.Views;
using AndroidX.RecyclerView.Widget;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

using MauiView            = Microsoft.Maui.Controls.View;
using AView               = Android.Views.View;
using VirtualizedOrientation = Agile.Maui.VirtualizedOrientation;
using ItemSizingStrategy     = Agile.Maui.ItemSizingStrategy;

namespace Agile.Maui.Platforms.Android;

public sealed class VirtualizedCollectionViewHandler
    : ViewHandler<VirtualizedCollectionView, VrContainerView>
{
    public static readonly PropertyMapper<VirtualizedCollectionView, VirtualizedCollectionViewHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(VirtualizedCollectionView.ItemsSource)]                            = (h, _) => h.ScheduleReload(),
            [nameof(VirtualizedCollectionView.ItemTemplate)]                           = (h, _) => h.ScheduleReload(),
            [nameof(VirtualizedCollectionView.ItemHeight)]                             = (h, _) => h.ApplySizeStrategy(),
            [nameof(VirtualizedCollectionView.Span)]                            = (h, _) => { h.ApplyLayoutManager(); h.ApplyItemSpacing(); h.ApplyCacheSizes(); },
            [nameof(VirtualizedCollectionView.Orientation)]                            = (h, _) => { h.ApplyLayoutManager(); h.ApplyItemSpacing(); h.ApplyScrollBars(); },
            [nameof(VirtualizedCollectionView.ItemSpacing)]                            = (h, _) => h.ApplyItemSpacing(),
            [nameof(VirtualizedCollectionView.EmptyView)]                              = (h, _) => h.UpdateEmptyView(),
            [nameof(VirtualizedCollectionView.EmptyViewTemplate)]                      = (h, _) => h.UpdateEmptyView(),
            [nameof(VirtualizedCollectionView.RemainingItemsThreshold)]                = (h, _) => h.ResetRemainingThresholdGate(),
            [nameof(VirtualizedCollectionView.RemainingItemsThresholdReachedCommand)]  = (h, _) => h.ResetRemainingThresholdGate(),
            [nameof(VirtualizedCollectionView.ScrolledCommand)]                        = (h, _) => { },
            [nameof(VirtualizedCollectionView.ItemSizingStrategy)]                       = (h, _) => h.ApplySizeStrategy(),
            [nameof(VirtualizedCollectionView.ItemHeightRequest)]                      = (h, _) => h.ApplySizeStrategy(),
            [nameof(VirtualizedCollectionView.VerticalScrollBarVisibility)]            = (h, _) => h.ApplyScrollBars(),
            [nameof(VirtualizedCollectionView.HorizontalScrollBarVisibility)]          = (h, _) => h.ApplyScrollBars(),
        };

    public static readonly CommandMapper<VirtualizedCollectionView, VirtualizedCollectionViewHandler> Commands =
        new(ViewHandler.ViewCommandMapper)
        {
            [nameof(VirtualizedCollectionView.ScrollTo)] = MapScrollTo,
        };

    private VrAdapter?                  _adapter;
    private VrScrollListener?           _scrollListener;
    private VrSpacingDecoration?        _spacingDecoration;
    private INotifyCollectionChanged?   _collectionChangedSource;
    private CachingLinearLayoutManager? _cachingLm;
    private VrRecyclerListener?         _recyclerListener;
    private bool                        _remainingThresholdInsideZone;
    private readonly List<PendingCollectionChange> _pendingChanges = [];
    private bool                        _flushScheduled;
    // Coalescing de ItemsSource + ItemTemplate + ItemHeight: todos disparam no connect
    // via mapper — sem isso o adapter seria recriado até 3× antes do primeiro render.
    private bool                        _reloadScheduled;

    private sealed class PendingCollectionChange
    {
        public NotifyCollectionChangedAction Action { get; init; }
        public int NewStartingIndex { get; init; }
        public int OldStartingIndex { get; init; }
        public List<object>? NewItems { get; init; }
        public int OldItemsCount { get; init; }
    }

    public VirtualizedCollectionViewHandler() : base(Mapper, Commands) { }

    protected override VrContainerView CreatePlatformView() => new(Context);

    protected override void ConnectHandler(VrContainerView platformView)
    {
        base.ConnectHandler(platformView);
        // MapWidth/MapHeight do ViewMapper base definem WrapContent quando Width/Height = -1.
        // Forçar MatchParent aqui garante que o container sempre preenche o espaço alocado
        // pelo MAUI e que FrameLayout.onMeasure passe EXACTLY para os filhos.
        platformView.LayoutParameters = new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent);
        // NestedScrollingEnabled, ClipChildren e ClipToPadding já são definidos no
        // construtor de VrContainerView — não repetir aqui.
        ApplyLayoutManager();
        ApplyItemSpacing();
        ApplyScrollBars();
        UpdateEmptyView();
        // ReloadItems() não é necessário aqui — o mapper dispara ItemsSource e ItemTemplate
        // imediatamente após ConnectHandler, cobrindo a carga inicial sem duplicação.
        ApplyCacheSizes();

        _scrollListener = new VrScrollListener(OnScrolled);
        platformView.Rv.AddOnScrollListener(_scrollListener);

        _recyclerListener = new VrRecyclerListener(Context!);
        platformView.Rv.AddRecyclerListener(_recyclerListener);
    }

    protected override void DisconnectHandler(VrContainerView platformView)
    {
        if (_scrollListener is not null)
        {
            platformView.Rv.RemoveOnScrollListener(_scrollListener);
            _scrollListener = null;
        }

        if (_recyclerListener is not null)
            platformView.Rv.RemoveRecyclerListener(_recyclerListener);

        UnsubscribeCollection();
        _reloadScheduled = false;
        _flushScheduled  = false;
        _pendingChanges.Clear();

        // Desanexar do RecyclerView ANTES de Dispose: SetAdapter(null) recicla as views
        // e ainda invoca callbacks no adapter/listener registrados — Dispose precoce mata
        // o peer gerenciado e o runtime tenta reativá-lo do handle nativo, causando
        // NotSupportedException ("Unable to activate instance ... from native handle").
        platformView.Rv.SetAdapter(null);
        platformView.Rv.SetLayoutManager(null);

        _adapter?.Dispose();
        _adapter = null;
        _cachingLm = null;
        _recyclerListener?.Dispose();
        _recyclerListener = null;

        base.DisconnectHandler(platformView);
    }

    // ── Coalescing de ReloadItems ─────────────────────────────────────────────

    // Garante que ItemsSource + ItemTemplate + ItemHeight disparados no mesmo ciclo
    // resultem em um único ReloadItems() — evita recriar o adapter até 3× no connect.
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

    internal void ApplyLayoutManager()
    {
        if (PlatformView is null) return;

        var horizontal = VirtualView.Orientation == VirtualizedOrientation.Horizontal;
        var direction  = horizontal ? LinearLayoutManager.Horizontal : LinearLayoutManager.Vertical;
        var columns    = VirtualView.Span;

        LinearLayoutManager llm;
        if (columns == 1 && !horizontal &&
            VirtualView.ItemSizingStrategy == ItemSizingStrategy.Dynamic &&
            VirtualView.ItemHeight <= 0)
        {
            var clm = new CachingLinearLayoutManager(Context!, GetFallbackHeightPx())
            {
                InitialPrefetchItemCount = 4
            };
            _cachingLm = clm;
            _adapter?.SetCachingLayoutManager(clm);
            llm = clm;
        }
        else
        {
            _cachingLm = null;
            _adapter?.SetCachingLayoutManager(null);
            if (columns == 1)
            {
                var linear = new LinearLayoutManager(Context, direction, false);
                linear.InitialPrefetchItemCount = 6;
                llm = linear;
            }
            else
            {
                var grid = new GridLayoutManager(Context, columns, direction, false);
                grid.InitialPrefetchItemCount = columns * 3;
                llm = grid;
            }
        }

        PlatformView.Rv.HasFixedSize = true;
        PlatformView.Rv.SetLayoutManager(llm);
        if (_adapter is not null)
            PlatformView.Rv.SetAdapter(_adapter);
    }

    private int GetFallbackHeightPx()
    {
        var dp = VirtualView.ItemHeightRequest > 0 ? VirtualView.ItemHeightRequest : 350.0;
        return (int)(dp * Context!.Resources!.DisplayMetrics!.Density);
    }

    private int GetResolvedItemHeightPx()
    {
        if (VirtualView.ItemHeight > 0)
            return Math.Max(1, (int)Math.Ceiling(Context.ToPixels(VirtualView.ItemHeight)));

        return VirtualView.ItemSizingStrategy == ItemSizingStrategy.Fixed
            ? GetFallbackHeightPx()
            : RecyclerView.LayoutParams.WrapContent;
    }

    private void ApplySizeStrategy()
    {
        if (PlatformView is null) return;
        ApplyLayoutManager();
        var heightPx = GetResolvedItemHeightPx();
        if (heightPx > 0)
            _adapter?.SetFixedHeight(heightPx);
        else if (VirtualView.ItemSizingStrategy == ItemSizingStrategy.MeasureFirst)
            // Reconstrói em modo "mede o 1º e fixa" (itens voltam a WrapContent até a medição).
            ScheduleReload();
        else
            _adapter?.SetWrapContentHeight();
    }

    internal void ApplyCacheSizes()
    {
        if (PlatformView is null) return;
        var (viewCache, poolMax) = GetOptimalCacheSizes(Context!, VirtualView.Span);
        PlatformView.Rv.SetItemViewCacheSize(viewCache);
        PlatformView.Rv.GetRecycledViewPool().SetMaxRecycledViews(0, poolMax);
    }

    private static (int viewCache, int poolMax) GetOptimalCacheSizes(Context context, int columnCount)
    {
        var am   = (ActivityManager?)context.GetSystemService(Context.ActivityService);
        var info = new ActivityManager.MemoryInfo();
        am?.GetMemoryInfo(info);
        long totalMb = info.TotalMem / (1024L * 1024L);

        int viewCache, poolMax;
        if      (totalMb >= 6144) { viewCache = 8;  poolMax = 20; }
        else if (totalMb >= 3072) { viewCache = 5;  poolMax = 12; }
        else if (totalMb >= 1536) { viewCache = 3;  poolMax = 8;  }
        else                      { viewCache = 2;  poolMax = 5;  }

        // Grid precisa de mais views simultâneas — escala proporcional ao número de colunas.
        if (columnCount > 1)
        {
            viewCache = viewCache * columnCount / 2;
            poolMax   = poolMax   * columnCount / 2;
        }

        Log.Debug("VrHandler", $"RAM={totalMb}MB cols={columnCount} → cache={viewCache} pool={poolMax}");
        return (viewCache, poolMax);
    }

    private void ApplyItemSpacing()
    {
        if (PlatformView is null) return;
        if (_spacingDecoration is not null)
        {
            PlatformView.Rv.RemoveItemDecoration(_spacingDecoration);
            _spacingDecoration = null;
        }
        var spacingPx = (int)Context.ToPixels(VirtualView.ItemSpacing);
        if (spacingPx <= 0) return;
        _spacingDecoration = new VrSpacingDecoration(
            spacingPx,
            VirtualView.Span,
            VirtualView.Orientation == VirtualizedOrientation.Horizontal);
        PlatformView.Rv.AddItemDecoration(_spacingDecoration);
    }

    // Habilita/desabilita a barra de rolagem nativa do RecyclerView.
    //
    // Pegadinha: num RecyclerView criado em código (sem AttributeSet com android:scrollbars)
    // o ScrollBarDrawable interno nunca é instanciado. Só ligar VerticalScrollBarEnabled faz
    // o draw chamar scrollBar.mutate() em null → NullPointerException. A única forma pública
    // de inicializar esse drawable sem inflar XML é setVerticalScrollbarThumbDrawable, que só
    // existe a partir do API 29 — por isso o guard de versão. Abaixo de 29 a barra fica oculta
    // (degrada sem crash). Só "Always" exibe; "Default"/"Never" mantêm oculto.
    private void ApplyScrollBars()
    {
        if (PlatformView is null) return;

        var rv         = PlatformView.Rv;
        var horizontal = VirtualView.Orientation == VirtualizedOrientation.Horizontal;
        var vis        = horizontal
            ? VirtualView.HorizontalScrollBarVisibility
            : VirtualView.VerticalScrollBarVisibility;

        var visible = vis == Microsoft.Maui.ScrollBarVisibility.Always
                   && SupportsScrollbarThumbDrawable();

        if (visible)
        {
            // Thumb cinza translúcido com cantos arredondados. Setá-lo inicializa o
            // ScrollBarDrawable (evita o NPE) e dá um visual discreto.
            var thumb = new global::Android.Graphics.Drawables.GradientDrawable();
            thumb.SetShape(global::Android.Graphics.Drawables.ShapeType.Rectangle);
            thumb.SetCornerRadius(rv.Context!.Resources!.DisplayMetrics!.Density * 4);
            thumb.SetColor(global::Android.Graphics.Color.Argb(128, 136, 136, 136));

#pragma warning disable CA1416
            if (horizontal) rv.HorizontalScrollbarThumbDrawable = thumb;
            else            rv.VerticalScrollbarThumbDrawable   = thumb;
#pragma warning restore CA1416

            rv.ScrollBarSize          = ViewConfiguration.Get(rv.Context)!.ScaledScrollBarSize;
            rv.ScrollbarFadingEnabled = false; // Always = sempre visível
        }

        rv.VerticalScrollBarEnabled   = visible && !horizontal;
        rv.HorizontalScrollBarEnabled = visible && horizontal;
    }

    [SupportedOSPlatformGuard("android29.0")]
    private static bool SupportsScrollbarThumbDrawable() => OperatingSystem.IsAndroidVersionAtLeast(29);

    private void UpdateEmptyView()
    {
        if (PlatformView is null) return;
        PlatformView.SetEmptyView(BuildEmptyNativeView());
        PlatformView.UpdateEmptyVisibility(_adapter is null || _adapter.ItemCount == 0);
    }

    private AView? BuildEmptyNativeView()
    {
        var src = VirtualView.EmptyView;
        if (MauiContext is null) return null;

        if (VirtualView.EmptyViewTemplate is { } t)
        {
            var tv = (MauiView)t.CreateContent();
            if (src is not null)
                tv.BindingContext = src;
            return tv.ToPlatform(MauiContext);
        }

        if (src is null) return null;

        if (src is string s)
            return MakeEmptyLabel(s);

        if (src is MauiView v)
            return v.ToPlatform(MauiContext);

        return MakeEmptyLabel(src.ToString() ?? string.Empty);
    }

    private global::Android.Widget.TextView MakeEmptyLabel(string text)
    {
        var tv = new global::Android.Widget.TextView(Context)
        {
            Text    = text,
            Gravity = GravityFlags.Center,
        };
        tv.SetTextColor(global::Android.Graphics.Color.Gray);
        return tv;
    }

    private void ReloadItems()
    {
        if (PlatformView is null || MauiContext is null) return;
        ResetRemainingThresholdGate();
        _pendingChanges.Clear();
        _flushScheduled = false;

        var template = VirtualView.ItemTemplate;
        if (template is null)
        {
            _adapter?.Dispose();
            _adapter = null;
            PlatformView.Rv.SetAdapter(null);
            UnsubscribeCollection();
            PlatformView.UpdateEmptyVisibility(true);
            return;
        }

        var items    = SnapshotItems(VirtualView.ItemsSource);
        var heightPx = GetResolvedItemHeightPx();

        UnsubscribeCollection();

        // Recria o adapter se o template mudou em runtime; sem esse check,
        // OnCreateViewHolder continuaria usando o template antigo.
        if (_adapter is null || !ReferenceEquals(_adapter.Template, template))
        {
            _adapter?.Dispose();
            _adapter = new VrAdapter(items, template, MauiContext, heightPx, Context!,
                VirtualView.Orientation == VirtualizedOrientation.Vertical);
            if (_cachingLm is not null)
                _adapter.SetCachingLayoutManager(_cachingLm);
            PlatformView.Rv.SetAdapter(_adapter);
        }
        else
        {
            _adapter.ReplaceAll(items, heightPx);
        }

        _adapter.SetMeasureFirst(
            VirtualView.ItemSizingStrategy == ItemSizingStrategy.MeasureFirst &&
            VirtualView.ItemHeight <= 0);

        SubscribeCollection(VirtualView.ItemsSource);
        PlatformView.UpdateEmptyVisibility(items.Count == 0);
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
        if (_adapter is null) return;

        var action = e.Action;
        if (action is not (NotifyCollectionChangedAction.Add
                       or NotifyCollectionChangedAction.Remove
                       or NotifyCollectionChangedAction.Replace
                       or NotifyCollectionChangedAction.Move))
        {
            _pendingChanges.Clear();
        }

        _pendingChanges.Add(new PendingCollectionChange
        {
            Action           = action,
            NewStartingIndex = e.NewStartingIndex,
            OldStartingIndex = e.OldStartingIndex,
            NewItems         = action is NotifyCollectionChangedAction.Add
                                      or NotifyCollectionChangedAction.Replace
                ? e.NewItems is not null ? CopyItems(e.NewItems) : null
                : null,
            OldItemsCount    = e.OldItems?.Count ?? 0,
        });

        if (_flushScheduled) return;
        _flushScheduled = true;
        MainThread.BeginInvokeOnMainThread(FlushPendingChanges);
    }

    private void FlushPendingChanges()
    {
        _flushScheduled = false;
        if (_adapter is null || PlatformView is null || _pendingChanges.Count == 0) return;

        var pending = _pendingChanges.ToArray();
        _pendingChanges.Clear();

        var firstAction = pending[0].Action;
        var isMixed     = Array.Exists(pending, p => p.Action != firstAction);
        var hasMove     = Array.Exists(pending, p => p.Action == NotifyCollectionChangedAction.Move);
        var shouldReload = pending.Length > 30 ||
                           isMixed ||
                           (hasMove && pending.Length > 1) ||
                           !CanApplyPendingChanges(pending, _adapter.ItemCount);

        if (!shouldReload)
        {
            foreach (var change in pending)
            {
                var applied = change.Action switch
                {
                    NotifyCollectionChangedAction.Add when change.NewItems is not null =>
                        _adapter.TryIncrementalAdd(change.NewStartingIndex, change.NewItems),
                    NotifyCollectionChangedAction.Remove when change.OldItemsCount > 0 =>
                        _adapter.TryIncrementalRemove(change.OldStartingIndex, change.OldItemsCount),
                    NotifyCollectionChangedAction.Replace when change.NewItems is not null =>
                        _adapter.TryIncrementalReplace(change.NewStartingIndex, change.NewItems),
                    NotifyCollectionChangedAction.Move =>
                        _adapter.TryIncrementalMove(change.OldStartingIndex, change.NewStartingIndex),
                    _ => false,
                };

                if (!applied)
                {
                    shouldReload = true;
                    break;
                }
            }
        }

        if (shouldReload)
        {
            ReloadItems();
            return;
        }

        ResetRemainingThresholdGate();
        PlatformView.UpdateEmptyVisibility(_adapter.ItemCount == 0);
    }

    private static bool CanApplyPendingChanges(PendingCollectionChange[] pending, int currentCount)
    {
        foreach (var change in pending)
        {
            switch (change.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    var addCount = change.NewItems?.Count ?? 0;
                    if (addCount <= 0 ||
                        change.NewStartingIndex < 0 ||
                        change.NewStartingIndex > currentCount)
                        return false;
                    currentCount += addCount;
                    break;
                case NotifyCollectionChangedAction.Remove:
                    if (change.OldItemsCount <= 0 ||
                        change.OldStartingIndex < 0 ||
                        change.OldStartingIndex + change.OldItemsCount > currentCount)
                        return false;
                    currentCount -= change.OldItemsCount;
                    break;
                case NotifyCollectionChangedAction.Replace:
                    var replaceCount = change.NewItems?.Count ?? 0;
                    if (replaceCount <= 0 ||
                        change.OldItemsCount != replaceCount ||
                        change.NewStartingIndex < 0 ||
                        change.NewStartingIndex + replaceCount > currentCount)
                        return false;
                    break;
                case NotifyCollectionChangedAction.Move:
                    if (change.OldStartingIndex < 0 ||
                        change.NewStartingIndex < 0 ||
                        change.OldStartingIndex >= currentCount ||
                        change.NewStartingIndex >= currentCount)
                        return false;
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private void OnScrolled(int dx, int dy)
    {
        if (VirtualView is null || PlatformView is null) return;

        if (VirtualView.HasScrolledObservers)
        {
            var rv = PlatformView.Rv;
            VirtualView.RaiseScrolled(
                Context.FromPixels(rv.ComputeHorizontalScrollOffset()),
                Context.FromPixels(rv.ComputeVerticalScrollOffset()));
        }

        if (VirtualView.RemainingItemsThreshold >= 0 &&
            (IsScrollingTowardEnd(dx, dy) || _remainingThresholdInsideZone))
        {
            CheckRemainingThreshold();
        }
    }

    private bool IsScrollingTowardEnd(int dx, int dy) =>
        VirtualView.Orientation == VirtualizedOrientation.Horizontal ? dx > 0 : dy > 0;

    private void CheckRemainingThreshold()
    {
        var threshold = VirtualView.RemainingItemsThreshold;
        if (threshold < 0 || _adapter is null || PlatformView is null) return;
        var total = _adapter.ItemCount;
        if (total <= 0)
        {
            _remainingThresholdInsideZone = false;
            return;
        }

        var llm = PlatformView.Rv.GetLayoutManager() as LinearLayoutManager;
        if (llm is null) return;
        var lastVisible = llm.FindLastVisibleItemPosition();
        var insideZone = lastVisible >= 0 && total - 1 - lastVisible <= threshold;
        if (!insideZone)
        {
            _remainingThresholdInsideZone = false;
            return;
        }

        if (_remainingThresholdInsideZone || !VirtualView.CanRaiseRemainingItemsThresholdReached)
            return;

        _remainingThresholdInsideZone = true;
        VirtualView.RaiseRemainingItemsThresholdReached();
    }

    private void ResetRemainingThresholdGate() => _remainingThresholdInsideZone = false;

    private static void MapScrollTo(
        VirtualizedCollectionViewHandler handler,
        VirtualizedCollectionView        view,
        object?                          args)
    {
        if (args is not VirtualizedCollectionView.ScrollToRequest r || handler.PlatformView is null) return;
        if (r.Animated)
            handler.PlatformView.Rv.SmoothScrollToPosition(r.Index);
        else
            handler.PlatformView.Rv.ScrollToPosition(r.Index);
    }

    private static List<object> SnapshotItems(IEnumerable? source)
    {
        if (source is null) return [];
        var capacity = source is System.Collections.ICollection c ? c.Count : 0;
        var list     = new List<object>(capacity > 0 ? capacity : 16);
        foreach (var item in source) list.Add(item);
        return list;
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
// VrContainerView — FrameLayout que envolve RecyclerView + EmptyView
// ─────────────────────────────────────────────────────────────────────────────

[Register("agile/maui/virtualizedcollectionview/VrContainerView")]
public sealed class VrContainerView : global::Android.Widget.FrameLayout
{
    internal readonly VrClippedRecyclerView Rv = null!;
    private AView? _emptyView;

    public VrContainerView(Context context) : base(context)
    {
        // MatchParent defensivo: garante largura/altura corretas antes de qualquer
        // medição que ocorra antes de ConnectHandler setar os LayoutParams definitivos.
        LayoutParameters = new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent);

        Rv = new VrClippedRecyclerView(context);
        Rv.HasFixedSize = false;
        Rv.SetItemAnimator(null);
        Rv.NestedScrollingEnabled = false;
        Rv.SetClipChildren(true);
        Rv.SetClipToPadding(true);

        AddView(Rv, new LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));
    }

    public VrContainerView(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer) { }

    protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
    {
        if (Rv is null)
        {
            base.OnLayout(changed, left, top, right, bottom);
            return;
        }

        // FrameLayout.onLayout posicionaria filhos usando getMeasuredWidth(), que pode ser
        // menor que (right-left) quando MAUI mede antes de calcular o layout final —
        // VrHandlerChain confirmou: VrContainerView width=1035, measured=850.
        // Usar os bounds reais do layout para que RecyclerView e EmptyView sempre
        // preencham a largura/altura que o MAUI efetivamente alocou.
        int w = right - left;
        int h = bottom - top;
        Rv.Layout(0, 0, w, h);
        if (_emptyView?.Visibility != ViewStates.Gone)
            _emptyView?.Layout(0, 0, w, h);
    }

    public void SetEmptyView(AView? view)
    {
        if (_emptyView is not null)
        {
            RemoveView(_emptyView);
            _emptyView = null;
        }
        if (view is not null)
        {
            _emptyView = view;
            AddView(view, new LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent)
            { Gravity = GravityFlags.Center });
        }
    }

    public void UpdateEmptyVisibility(bool isEmpty)
    {
        Rv.Visibility = isEmpty ? ViewStates.Gone : ViewStates.Visible;
        if (_emptyView is not null)
            _emptyView.Visibility = isEmpty ? ViewStates.Visible : ViewStates.Gone;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// VrSpacingDecoration — espaçamento uniforme entre itens
// ─────────────────────────────────────────────────────────────────────────────

[Register("agile/maui/virtualizedcollectionview/VrSpacingDecoration")]
internal sealed class VrSpacingDecoration : RecyclerView.ItemDecoration
{
    private readonly int  _spacePx;
    private readonly int  _columns;
    private readonly bool _horizontal;

    public VrSpacingDecoration(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
        _spacePx = 0;
        _columns = 1;
        _horizontal = false;
    }

    public VrSpacingDecoration(int spacePx, int columns, bool horizontal)
    {
        _spacePx    = spacePx;
        _columns    = columns;
        _horizontal = horizontal;
    }

    public override void GetItemOffsets(
        global::Android.Graphics.Rect outRect, AView view, RecyclerView parent, RecyclerView.State state)
    {
        var pos = parent.GetChildAdapterPosition(view);
        if (pos < 0) return;

        if (!_horizontal)
        {
            // Scroll vertical: distribui espaço horizontal entre colunas (sem borda externa)
            var col        = pos % _columns;
            outRect.Left   = col * _spacePx / _columns;
            outRect.Right  = _spacePx - (col + 1) * _spacePx / _columns;
            outRect.Bottom = _spacePx;
        }
        else
        {
            // Scroll horizontal: distribui espaço vertical entre linhas
            var row        = pos % _columns;
            outRect.Top    = row * _spacePx / _columns;
            outRect.Bottom = _spacePx - (row + 1) * _spacePx / _columns;
            outRect.Right  = _spacePx;
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// VrAdapter — RecyclerView.Adapter com DiffUtil assíncrono e MAUI view recycling
// ─────────────────────────────────────────────────────────────────────────────

[Register("agile/maui/virtualizedcollectionview/VrAdapter")]
internal sealed class VrAdapter : RecyclerView.Adapter
{
    private readonly DataTemplate        _template;
    private readonly IMauiContext        _mauiContext;
    private readonly Context             _context;
    private          List<object>        _items;
    private          int                 _itemHeightPx;
    private readonly List<VrViewHolder>  _allHolders = [];
    private          bool                _disposed;
    private          CachingLinearLayoutManager? _cachingLm;
    private          bool                _measureFirst;
    private readonly bool                _coerceWidth;

    public DataTemplate Template    => _template;
    public int          ItemHeightPx => _itemHeightPx;

    public void SetFixedHeight(int px)
    {
        _measureFirst  = false;
        _itemHeightPx  = px;
        AplicarAlturaFixaATodos();
    }

    public void SetWrapContentHeight()
    {
        _measureFirst = false;
        _itemHeightPx = ViewGroup.LayoutParams.WrapContent;
        AplicarAlturaFixaATodos();
    }

    public void SetCachingLayoutManager(CachingLinearLayoutManager? clm) => _cachingLm = clm;

    // MeasureFirst: enquanto ativo, os itens ficam em WrapContent até o 1º ser medido;
    // a altura medida é então fixada para todos. Reativar volta a medir.
    public void SetMeasureFirst(bool value)
    {
        _measureFirst = value;
        if (value) _itemHeightPx = ViewGroup.LayoutParams.WrapContent;
    }

    public VrAdapter(List<object> items, DataTemplate template, IMauiContext mauiContext, int itemHeightPx, Context context, bool coerceWidth)
    {
        _items        = items;
        _template     = template;
        _mauiContext  = mauiContext;
        _itemHeightPx = itemHeightPx;
        _context      = context;
        _coerceWidth  = coerceWidth;
    }

    public VrAdapter(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
        _items       = [];
        _template    = null!;
        _mauiContext = null!;
        _itemHeightPx = ViewGroup.LayoutParams.WrapContent;
        _context     = null!;
        _coerceWidth = false;
    }

    public override int ItemCount => _items.Count;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var content  = _template.CreateContent();
        var mauiView = (MauiView)content;

        mauiView.HorizontalOptions = LayoutOptions.Fill;

        var nativeView = mauiView.ToPlatform(_mauiContext);
        int h = (_cachingLm == null && _itemHeightPx > 0)
            ? _itemHeightPx
            : ViewGroup.LayoutParams.WrapContent;

        AView itemRoot;
        if (_coerceWidth)
        {
            var host = new VrItemHost(_context, mauiView);
            host.AddView(nativeView, new global::Android.Widget.FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            host.LayoutParameters = new RecyclerView.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, h);
            itemRoot = host;
        }
        else
        {
            nativeView.LayoutParameters = new RecyclerView.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, h);
            itemRoot = nativeView;
        }

        var holder = new VrViewHolder(itemRoot, mauiView);
        lock (_allHolders) _allHolders.Add(holder);
        return holder;
    }

    private void BindItem(VrViewHolder holder, int position)
    {
        var item = _items[position];
        if (!ReferenceEquals(holder.MauiView.BindingContext, item))
            holder.MauiView.BindingContext = item;
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is VrViewHolder h && (uint)position < (uint)_items.Count)
        {
            var bindGeneration = h.NextBindGeneration();

            if (_cachingLm != null)
            {
                // WrapContent: o item se dimensiona pelo conteúdo, expanders funcionam.
                // So (re)aloca os LayoutParams quando ainda nao estao no formato esperado —
                // recriar a cada bind gera lixo e um requestLayout extra por celula no hot path.
                if (h.ItemView.LayoutParameters is not RecyclerView.LayoutParams lp ||
                    lp.Height != ViewGroup.LayoutParams.WrapContent ||
                    lp.Width  != ViewGroup.LayoutParams.MatchParent)
                {
                    h.ItemView.LayoutParameters = new RecyclerView.LayoutParams(
                        ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
                }

                BindItem(h, position);

                // So mede/cacheia a altura na PRIMEIRA vez que a posicao aparece. Em posicoes
                // ja medidas, evita token + Post a cada bind — a maior fonte de
                // lixo no scroll. O cache alimenta apenas a ESTIMATIVA de scroll (offset/range);
                // o layout real continua por WrapContent, entao a altura exibida nao e afetada.
                if (!_cachingLm.HasCachedHeight(position))
                {
                    var capturedHolder     = h;
                    var capturedGeneration = bindGeneration;
                    var capturedLm         = _cachingLm;
                    var capturedPos        = position;
                    h.ItemView.Post(() =>
                    {
                        if (_disposed || !capturedHolder.IsCurrentBind(capturedGeneration)) return;
                        int real = capturedHolder.ItemView.Height;
                        if (real > 0)
                            capturedLm.CacheItemHeight(capturedPos, real);
                    });
                }
            }
            else
            {
                BindItem(h, position);

                // MeasureFirst: mede o 1º item exibido (WrapContent) e fixa essa altura para todos.
                // A medição via Post "tiro único" falha quando o item nasce com altura 0 — o que
                // acontece se a lista é populada com o controle ainda invisível (ex.: enquanto um
                // IsCarregando=true mantém IsVisible=false). Nesse caso o item nunca era medido e
                // a lista ficava colapsada. Aqui observamos o layout do item e fixamos a altura
                // assim que ela passar a ser > 0 — ou seja, quando a tela enfim ganha dimensões.
                if (_measureFirst && _itemHeightPx <= 0)
                {
                    h.MeasureFirstHeight(real =>
                    {
                        if (_disposed || _itemHeightPx > 0) return;

                        _itemHeightPx = real;   // novos holders já nascem com esta altura
                        _measureFirst = false;
                        AplicarAlturaFixaATodos();
                    });
                }
            }
        }
    }

    // Aplica a altura fixa medida aos holders já criados (os novos pegam em OnCreateViewHolder).
    private void AplicarAlturaFixaATodos()
    {
        lock (_allHolders)
        {
            foreach (var hh in _allHolders)
            {
                if (hh.ItemView.LayoutParameters is RecyclerView.LayoutParams lp &&
                    lp.Height != _itemHeightPx)
                {
                    lp.Height = _itemHeightPx;
                    hh.ItemView.LayoutParameters = lp; // dispara requestLayout
                }
            }
        }
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position,
        IList<Java.Lang.Object> payloads)
    {
        if (payloads is { Count: > 0 } && holder is VrViewHolder h &&
            (uint)position < (uint)_items.Count)
        {
            h.NextBindGeneration();
            BindItem(h, position);
        }
        else
        {
            OnBindViewHolder(holder, position);
        }
    }

    // ── Operações incrementais ────────────────────────────────────────────────

    public bool TryIncrementalAdd(int startIndex, List<object> newItems)
    {
        if (newItems.Count <= 0 || startIndex < 0 || startIndex > _items.Count)
            return false;

        _items.InsertRange(startIndex, newItems);
        // Insert desloca os indices a partir de startIndex → invalida as alturas dali em diante.
        _cachingLm?.InvalidateFrom(startIndex);
        if (newItems.Count == 1)
            NotifyItemInserted(startIndex);
        else
            NotifyItemRangeInserted(startIndex, newItems.Count);
        return true;
    }

    public bool TryIncrementalRemove(int startIndex, int count)
    {
        if (count <= 0 || startIndex < 0 || startIndex + count > _items.Count)
            return false;

        _items.RemoveRange(startIndex, count);
        _cachingLm?.InvalidateFrom(startIndex);
        if (count == 1)
            NotifyItemRemoved(startIndex);
        else
            NotifyItemRangeRemoved(startIndex, count);
        return true;
    }

    public bool TryIncrementalReplace(int startIndex, List<object> newItems)
    {
        if (newItems.Count <= 0 || startIndex < 0 || startIndex + newItems.Count > _items.Count)
            return false;

        for (int i = 0; i < newItems.Count && startIndex + i < _items.Count; i++)
            _items[startIndex + i] = newItems[i];
        // Itens trocados podem ter alturas diferentes; indices nao deslocam.
        _cachingLm?.InvalidateRange(startIndex, newItems.Count);
        NotifyItemRangeChanged(startIndex, newItems.Count);
        return true;
    }

    public bool TryIncrementalMove(int from, int to)
    {
        if (from < 0 || to < 0 || from >= _items.Count || to >= _items.Count)
            return false;
        if (from == to)
            return true;

        var item = _items[from];
        _items.RemoveAt(from);
        _items.Insert(to, item);
        _cachingLm?.InvalidateFrom(Math.Min(from, to));
        NotifyItemMoved(from, to);
        return true;
    }

    // ── Substituição completa com DiffUtil assíncrono ─────────────────────────

    // Substituição total SÍNCRONA. NotifyDataSetChanged é à prova de inconsistência
    // (o RecyclerView re-consulta a contagem inteira no próximo layout) e, por rodar
    // de forma síncrona na UI thread, jamais corre com as notificações incrementais —
    // ao contrário do diff assíncrono anterior (Task.Run + DispatchUpdatesTo), que
    // podia completar fora de ordem e notificar as mesmas inserções uma segunda vez,
    // divergindo getItemCount() de _items.Count → "Inconsistency detected" no GapWorker.
    // Mantém o invariante: getItemCount() == _items.Count e cada Notify* corresponde
    // exatamente à mutação feita em _items.
    public void ReplaceAll(List<object> newItems, int newHeightPx)
    {
        _itemHeightPx = newHeightPx;
        _cachingLm?.InvalidateCache();
        _items = newItems;
        AplicarAlturaFixaATodos();
        NotifyDataSetChanged();
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;
        if (disposing)
        {
            lock (_allHolders)
            {
                foreach (var h in _allHolders)
                {
                    h.CancelHeavyBind();
                    h.MauiView.Handler?.DisconnectHandler();
                }
                _allHolders.Clear();
            }
        }
        base.Dispose(disposing);
    }
}

// RecyclerView measures item roots natively, but MAUI templates still need an
// explicit MAUI measure/arrange pass with the final row width.
[Register("agile/maui/virtualizedcollectionview/VrItemHost")]
internal sealed class VrItemHost : global::Android.Widget.FrameLayout
{
    private readonly MauiView? _mauiView;

    public VrItemHost(Context context, MauiView mauiView) : base(context)
    {
        _mauiView = mauiView;
        SetClipChildren(false);
    }

    public VrItemHost(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer) { }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        var target = GetTargetWidth(widthMeasureSpec);
        if (target <= 0 || ChildCount == 0)
        {
            base.OnMeasure(widthMeasureSpec, heightMeasureSpec);
            return;
        }

        var child = GetChildAt(0)!;
        var childWidth = Math.Max(0, target - PaddingLeft - PaddingRight);
        var childWidthSpec = AView.MeasureSpec.MakeMeasureSpec(childWidth, MeasureSpecMode.Exactly);

        var heightMode = AView.MeasureSpec.GetMode(heightMeasureSpec);
        var heightSize = AView.MeasureSpec.GetSize(heightMeasureSpec);
        var childHeight = heightMode == MeasureSpecMode.Unspecified
            ? 0
            : Math.Max(0, heightSize - PaddingTop - PaddingBottom);

        if (_mauiView is not null && heightMode != MeasureSpecMode.Exactly)
        {
            var widthDip = Context.FromPixels(childWidth);
            var measured = ((IView)_mauiView).Measure(widthDip, double.PositiveInfinity);
            childHeight = Math.Max(1, (int)Math.Ceiling(Context.ToPixels(measured.Height)));
        }
        else if (heightMode != MeasureSpecMode.Exactly)
        {
            child.Measure(childWidthSpec, heightMeasureSpec);
            childHeight = Math.Max(1, child.MeasuredHeight);
        }

        var childHeightSpec = AView.MeasureSpec.MakeMeasureSpec(childHeight, MeasureSpecMode.Exactly);

        child.Measure(childWidthSpec, childHeightSpec);

        var desiredHeight = heightMode == MeasureSpecMode.Exactly
            ? heightSize
            : childHeight + PaddingTop + PaddingBottom;

        SetMeasuredDimension(target, ResolveSize(desiredHeight, heightMeasureSpec));
    }

    protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
    {
        if (ChildCount == 0) return;
        var childWidth = Math.Max(0, right - left - PaddingLeft - PaddingRight);
        var childHeight = Math.Max(0, bottom - top - PaddingTop - PaddingBottom);
        if (_mauiView is not null)
        {
            ((IView)_mauiView).Arrange(new Microsoft.Maui.Graphics.Rect(
                0,
                0,
                Context.FromPixels(childWidth),
                Context.FromPixels(childHeight)));
        }

        GetChildAt(0)!.Layout(
            PaddingLeft,
            PaddingTop,
            right - left - PaddingRight,
            bottom - top - PaddingBottom);
    }

    private int GetTargetWidth(int widthMeasureSpec)
    {
        var mode = AView.MeasureSpec.GetMode(widthMeasureSpec);
        var size = AView.MeasureSpec.GetSize(widthMeasureSpec);

        var target = mode != MeasureSpecMode.Unspecified && size > 0 ? size : -1;
        if (target <= 0 && Parent is AView p && p.Width > 0)
            target = p.Width - p.PaddingLeft - p.PaddingRight;
        if (target <= 0 && Parent is AView mp && mp.MeasuredWidth > 0)
            target = mp.MeasuredWidth - mp.PaddingLeft - mp.PaddingRight;

        return target;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Helpers internos
// ─────────────────────────────────────────────────────────────────────────────

[Register("agile/maui/virtualizedcollectionview/VrViewHolder")]
internal sealed class VrViewHolder : RecyclerView.ViewHolder
{
    public MauiView MauiView { get; }
    private int _bindGeneration;

    // MeasureFirst: observador de layout + callback pendente da medição da altura.
    private LayoutObserver? _measureListener;
    private Action<int>?    _measureCallback;

    public VrViewHolder(AView platformView, MauiView mauiView)
        : base(platformView) => MauiView = mauiView;

    public VrViewHolder(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer) => MauiView = null!;

    public int NextBindGeneration()
    {
        unchecked { _bindGeneration++; }
        return _bindGeneration;
    }

    public bool IsCurrentBind(int generation) => generation == _bindGeneration;

    public void CancelHeavyBind()
    {
        NextBindGeneration();
        CancelMeasureFirst();
    }

    // Reporta a altura real do item assim que ela for > 0. Diferente de um Post único,
    // re-tenta a cada relayout — então funciona mesmo que o item nasça com altura 0
    // (controle ainda sem dimensões) e só ganhe tamanho quando a tela fica visível.
    public void MeasureFirstHeight(Action<int> onMeasured)
    {
        CancelMeasureFirst();
        _measureCallback = onMeasured;

        _measureListener = new LayoutObserver(TryReportMeasure);
        ItemView.AddOnLayoutChangeListener(_measureListener);

        // Caso o item já tenha altura e nenhum relayout dispare, mede no próximo frame.
        ItemView.Post(TryReportMeasure);
    }

    private void TryReportMeasure()
    {
        if (_measureCallback is null) return;
        int real = ItemView.Height;
        if (real <= 0) return;

        var cb = _measureCallback;
        CancelMeasureFirst();   // mede uma única vez; remove o observador
        cb(real);
    }

    private void CancelMeasureFirst()
    {
        _measureCallback = null;
        if (_measureListener is not null)
        {
            ItemView.RemoveOnLayoutChangeListener(_measureListener);
            _measureListener.Dispose();
            _measureListener = null;
        }
    }

    // Dispara o callback quando o layout do ItemView muda (incl. quando ganha altura).
    [Register("agile/maui/virtualizedcollectionview/VrLayoutObserver")]
    private sealed class LayoutObserver : Java.Lang.Object, AView.IOnLayoutChangeListener
    {
        private readonly Action? _onLayout;
        public LayoutObserver(Action onLayout) => _onLayout = onLayout;

        // Construtor de ativação: exigido pelo runtime se o peer gerenciado for coletado
        // enquanto o Java ainda referencia o listener — sem ele, NotSupportedException.
        public LayoutObserver(IntPtr handle, JniHandleOwnership transfer)
            : base(handle, transfer) { }

        public void OnLayoutChange(AView? v, int l, int t, int r, int b,
                                   int oldL, int oldT, int oldR, int oldB) => _onLayout?.Invoke();
    }
}

[Register("agile/maui/virtualizedcollectionview/VrScrollListener")]
internal sealed class VrScrollListener : RecyclerView.OnScrollListener
{
    private readonly Action<int, int> _onScrolled;
    public VrScrollListener(Action<int, int> onScrolled) => _onScrolled = onScrolled;
    public VrScrollListener(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer) => _onScrolled = static (_, _) => { };
    public override void OnScrolled(RecyclerView recyclerView, int dx, int dy) => _onScrolled(dx, dy);
}

// RecyclerView que força clipping ao canvas em DispatchDraw. Backup à prova de falhas:
// mesmo se algum ancestral MAUI desabilita clipChildren, ou se o LayoutManager
// posiciona o primeiro item com top negativo (scroll parcial), o desenho dos
// filhos jamais ultrapassa a área retangular dos bounds do RecyclerView. Isso
// impede a row visível vazar acima do RecyclerView e cobrir a SearchBar/header.
[Register("agile/maui/virtualizedcollectionview/VrClippedRecyclerView")]
internal sealed class VrClippedRecyclerView : RecyclerView
{
    public VrClippedRecyclerView(Context context) : base(context) { }

    public VrClippedRecyclerView(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer) { }

    protected override void DispatchDraw(Canvas? canvas)
    {
        if (canvas is null) return;
        var save = canvas.Save();
        canvas.ClipRect(0, 0, Width, Height);
        base.DispatchDraw(canvas);
        canvas.RestoreToCount(save);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// VrRecyclerListener — cancela Post pendente e libera Glide quando um holder
// é devolvido ao pool (antes de ser revinculado a outro item).
// ─────────────────────────────────────────────────────────────────────────────

[Register("agile/maui/virtualizedcollectionview/VrRecyclerListener")]
internal sealed class VrRecyclerListener : Java.Lang.Object, RecyclerView.IRecyclerListener
{
    private readonly Context? _context;

    public VrRecyclerListener(Context context) => _context = context;

    // Construtor de ativação: usado pelo runtime se o peer gerenciado for coletado
    // enquanto o Java ainda referencia o listener — sem ele, NotSupportedException.
    public VrRecyclerListener(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer) { }

    public void OnViewRecycled(RecyclerView.ViewHolder holder)
    {
        if (holder is VrViewHolder vh)
        {
            vh.CancelHeavyBind();
            if (_context is not null && vh.ItemView is global::Android.Widget.ImageView)
                Glide.With(_context).Clear(vh.ItemView);
            vh.MauiView.BindingContext = null;
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CachingLinearLayoutManager — LinearLayoutManager com cache progressivo de alturas.
// Elimina os saltos de deltaY causados por estimativas erradas do LLM padrão
// quando itens têm alturas heterogêneas (ex: GalleryView com imagens de tamanhos variados).
// ─────────────────────────────────────────────────────────────────────────────

[Register("agile/maui/virtualizedcollectionview/VrCachingLinearLayoutManager")]
internal sealed class CachingLinearLayoutManager : LinearLayoutManager
{
    private readonly SparseIntArray _cache = new();
    private int _avgHeight;
    private int _measuredCount;
    private readonly int _fallbackHeightPx;
    // Cache do scroll range total — evita O(n) por frame de scroll.
    // Invalidado quando uma nova altura real é registrada ou o dataset muda.
    private int _cachedScrollRange = -1;

    public CachingLinearLayoutManager(Context ctx, int fallbackPx) : base(ctx)
    {
        _fallbackHeightPx = fallbackPx;
    }

    public CachingLinearLayoutManager(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
        _fallbackHeightPx = 0;
    }

    public void CacheItemHeight(int position, int heightPx)
    {
        bool isNew = _cache.Get(position, -1) == -1;
        _cache.Put(position, heightPx);
        if (isNew && heightPx > 0)
        {
            _measuredCount++;
            _avgHeight        += (heightPx - _avgHeight) / _measuredCount;
            _cachedScrollRange = -1; // invalidar: média mudou, range muda
        }
    }

    public int GetEstimatedHeight(int position)
    {
        int h = _cache.Get(position, -1);
        if (h != -1) return h;
        return _avgHeight > 0 ? _avgHeight : _fallbackHeightPx;
    }

    // True quando a posicao ja tem altura REAL medida em cache — o adapter usa isso para
    // pular o Post de medicao em posicoes ja vistas (corta alocacao no hot path do scroll).
    public bool HasCachedHeight(int position) => _cache.Get(position, -1) != -1;

    // Chamado pelo adapter em IncrementalAdd/Remove — preserva o cache de alturas
    // mas invalida o range total (número de itens mudou).
    public void InvalidateScrollRange() => _cachedScrollRange = -1;

    // Remove as alturas cacheadas das posicoes >= position (que mudaram de indice apos um
    // insert/remove no meio da lista) — sem isso, com o skip-if-cached do adapter, a
    // estimativa de scroll ficaria presa em alturas de itens errados. Append no fim
    // (position == ItemCount) nao remove nada, entao infinite scroll fica intacto.
    public void InvalidateFrom(int position)
    {
        for (int i = _cache.Size() - 1; i >= 0; i--)
            if (_cache.KeyAt(i) >= position)
                _cache.RemoveAt(i);
        _cachedScrollRange = -1;
    }

    // Remove as alturas cacheadas de um intervalo [start, start+count) — usado no Replace,
    // onde os itens mudam mas os indices nao deslocam.
    public void InvalidateRange(int start, int count)
    {
        for (int i = _cache.Size() - 1; i >= 0; i--)
        {
            int key = _cache.KeyAt(i);
            if (key >= start && key < start + count)
                _cache.RemoveAt(i);
        }
        _cachedScrollRange = -1;
    }

    public void InvalidateCache()
    {
        _cache.Clear();
        _avgHeight         = 0;
        _measuredCount     = 0;
        _cachedScrollRange = -1;
    }

    public override int ComputeVerticalScrollOffset(RecyclerView.State state)
    {
        if (ChildCount == 0) return 0;
        var first    = GetChildAt(0)!;
        int firstPos = GetPosition(first);
        int offset   = -GetDecoratedTop(first);
        for (int i = 0; i < firstPos; i++)
            offset += GetEstimatedHeight(i);
        return Math.Max(0, offset);
    }

    public override int ComputeVerticalScrollRange(RecyclerView.State state)
    {
        // Cache O(1): recalcula apenas quando o dataset ou as alturas mudam.
        if (_cachedScrollRange >= 0) return _cachedScrollRange;

        int total = 0;
        int count = ItemCount;
        for (int i = 0; i < count; i++)
            total += GetEstimatedHeight(i);
        _cachedScrollRange = total;
        return total;
    }

    public override int ComputeVerticalScrollExtent(RecyclerView.State state) => Height;
}

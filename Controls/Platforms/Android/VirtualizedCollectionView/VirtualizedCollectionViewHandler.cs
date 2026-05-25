// Platforms/Android/VirtualizedCollectionView/VirtualizedCollectionViewHandler.cs
using System.Collections;
using System.Collections.Specialized;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Util;
using Bumptech.Glide;
using Android.Views;
using AndroidX.RecyclerView.Widget;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

using Microsoft.Maui.Controls;
using MauiView = Microsoft.Maui.Controls.View;
using AView    = Android.Views.View;

namespace Agile.Maui.Platforms.Android;

public sealed class VirtualizedCollectionViewHandler
    : ViewHandler<VirtualizedCollectionView, VrContainerView>
{
    public static readonly PropertyMapper<VirtualizedCollectionView, VirtualizedCollectionViewHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(VirtualizedCollectionView.ItemsSource)]                            = (h, _) => h.ReloadItems(),
            [nameof(VirtualizedCollectionView.ItemTemplate)]                           = (h, _) => h.ReloadItems(),
            [nameof(VirtualizedCollectionView.ItemHeight)]                             = (h, _) => h.ReloadItems(),
            [nameof(VirtualizedCollectionView.ColumnCount)]                            = (h, _) => { h.ApplyLayoutManager(); h.ApplyItemSpacing(); h.ApplyCacheSizes(); },
            [nameof(VirtualizedCollectionView.Orientation)]                            = (h, _) => { h.ApplyLayoutManager(); h.ApplyItemSpacing(); },
            [nameof(VirtualizedCollectionView.ItemSpacing)]                            = (h, _) => h.ApplyItemSpacing(),
            [nameof(VirtualizedCollectionView.EmptyView)]                              = (h, _) => h.UpdateEmptyView(),
            [nameof(VirtualizedCollectionView.EmptyViewTemplate)]                      = (h, _) => h.UpdateEmptyView(),
            [nameof(VirtualizedCollectionView.RemainingItemsThreshold)]                = (h, _) => { },
            [nameof(VirtualizedCollectionView.RemainingItemsThresholdReachedCommand)]  = (h, _) => { },
            [nameof(VirtualizedCollectionView.ScrolledCommand)]                        = (h, _) => { },
            [nameof(VirtualizedCollectionView.ItemSizeStrategy)]                       = (h, _) => h.ApplySizeStrategy(),
            [nameof(VirtualizedCollectionView.ItemHeightRequest)]                      = (h, _) => h.ApplySizeStrategy(),
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
        platformView.Rv.NestedScrollingEnabled = false;
        platformView.Rv.SetClipChildren(true);
        platformView.Rv.SetClipToPadding(true);
        ApplyLayoutManager();
        ApplyItemSpacing();
        UpdateEmptyView();
        ReloadItems();

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
        UnsubscribeCollection();
        _adapter?.Dispose();
        _adapter = null;
        _cachingLm = null;
        _recyclerListener?.Dispose();
        _recyclerListener = null;
        platformView.Rv.SetAdapter(null);
        platformView.Rv.SetLayoutManager(null);
        base.DisconnectHandler(platformView);
    }

    internal void ApplyLayoutManager()
    {
        if (PlatformView is null) return;

        var horizontal = VirtualView.Orientation == VirtualizedOrientation.Horizontal;
        var direction  = horizontal ? LinearLayoutManager.Horizontal : LinearLayoutManager.Vertical;
        var columns    = VirtualView.ColumnCount;

        LinearLayoutManager llm;
        if (columns == 1 && !horizontal && VirtualView.ItemSizeStrategy == ItemSizeStrategy.Dynamic)
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

    private void ApplySizeStrategy()
    {
        if (PlatformView is null) return;
        ApplyLayoutManager();
        if (VirtualView.ItemSizeStrategy == ItemSizeStrategy.Fixed)
            _adapter?.SetFixedHeight(GetFallbackHeightPx());
    }

    internal void ApplyCacheSizes()
    {
        if (PlatformView is null) return;
        var (viewCache, poolMax) = GetOptimalCacheSizes(Context!, VirtualView.ColumnCount);
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
            VirtualView.ColumnCount,
            VirtualView.Orientation == VirtualizedOrientation.Horizontal);
        PlatformView.Rv.AddItemDecoration(_spacingDecoration);
    }

    private void UpdateEmptyView()
    {
        if (PlatformView is null) return;
        PlatformView.SetEmptyView(BuildEmptyNativeView());
        PlatformView.UpdateEmptyVisibility(_adapter is null || _adapter.ItemCount == 0);
    }

    private AView? BuildEmptyNativeView()
    {
        var src = VirtualView.EmptyView;
        if (src is null || MauiContext is null) return null;

        if (src is string s)
            return MakeEmptyLabel(s);

        if (src is MauiView v)
            return v.ToPlatform(MauiContext);

        if (VirtualView.EmptyViewTemplate is { } t)
        {
            var tv = (MauiView)t.CreateContent();
            tv.BindingContext = src;
            return tv.ToPlatform(MauiContext);
        }

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
        var heightPx = VirtualView.ItemSizeStrategy == ItemSizeStrategy.Fixed
            ? GetFallbackHeightPx()
            : VirtualView.ItemHeight > 0
                ? (int)Context.ToPixels(VirtualView.ItemHeight)
                : RecyclerView.LayoutParams.WrapContent;

        UnsubscribeCollection();

        if (_adapter is null)
        {
            _adapter = new VrAdapter(items, template, MauiContext, heightPx, Context!);
            if (_cachingLm is not null)
                _adapter.SetCachingLayoutManager(_cachingLm);
            PlatformView.Rv.SetAdapter(_adapter);
        }
        else
        {
            _adapter.UpdateItemsAsync(items, heightPx);
        }

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
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_adapter is null) return;
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                    _adapter.IncrementalAdd(e.NewStartingIndex, e.NewItems.Cast<object>().ToList());
                    break;
                case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                    _adapter.IncrementalRemove(e.OldStartingIndex, e.OldItems.Count);
                    break;
                case NotifyCollectionChangedAction.Replace when e.NewItems is not null:
                    _adapter.IncrementalReplace(e.NewStartingIndex, e.NewItems.Cast<object>().ToList());
                    break;
                case NotifyCollectionChangedAction.Move:
                    _adapter.IncrementalMove(e.OldStartingIndex, e.NewStartingIndex);
                    break;
                default:
                    _adapter.UpdateItemsAsync(SnapshotItems(VirtualView.ItemsSource), _adapter.ItemHeightPx);
                    break;
            }
            PlatformView?.UpdateEmptyVisibility(_adapter.ItemCount == 0);
        });
    }

    private void OnScrolled(int dx, int dy)
    {
        if (VirtualView is null || PlatformView is null) return;
        VirtualView.RaiseScrolled(
            Context.FromPixels(PlatformView.Rv.ComputeHorizontalScrollOffset()),
            Context.FromPixels(PlatformView.Rv.ComputeVerticalScrollOffset()));
        CheckRemainingThreshold();
    }

    private void CheckRemainingThreshold()
    {
        var threshold = VirtualView.RemainingItemsThreshold;
        if (threshold < 0 || _adapter is null || PlatformView is null) return;
        var llm = PlatformView.Rv.GetLayoutManager() as LinearLayoutManager;
        if (llm is null) return;
        var lastVisible = llm.FindLastVisibleItemPosition();
        var total       = _adapter.ItemCount;
        if (total > 0 && total - 1 - lastVisible <= threshold)
            VirtualView.RaiseRemainingItemsThresholdReached();
    }

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
        var list = new List<object>();
        foreach (var item in source) list.Add(item);
        return list;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// VrContainerView — FrameLayout que envolve RecyclerView + EmptyView
// ─────────────────────────────────────────────────────────────────────────────

public sealed class VrContainerView : global::Android.Widget.FrameLayout
{
    internal readonly ClippedRecyclerView Rv;
    private AView? _emptyView;

    public VrContainerView(Context context) : base(context)
    {
        // MatchParent defensivo: garante largura/altura corretas antes de qualquer
        // medição que ocorra antes de ConnectHandler setar os LayoutParams definitivos.
        LayoutParameters = new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent);

        Rv = new ClippedRecyclerView(context);
        Rv.HasFixedSize = false;
        Rv.SetItemAnimator(null);
        Rv.NestedScrollingEnabled = false;
        Rv.SetClipChildren(true);
        Rv.SetClipToPadding(true);

        AddView(Rv, new LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));
    }

    protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
    {
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

internal sealed class VrSpacingDecoration : RecyclerView.ItemDecoration
{
    private readonly int  _spacePx;
    private readonly int  _columns;
    private readonly bool _horizontal;

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

internal sealed class VrAdapter : RecyclerView.Adapter
{
    private readonly DataTemplate        _template;
    private readonly IMauiContext        _mauiContext;
    private readonly Context             _context;
    private          List<object>        _items;
    private          int                 _itemHeightPx;
    private readonly List<VrViewHolder>  _allHolders = [];
    private          CancellationTokenSource? _diffCts;
    private          bool                _disposed;
    private          CachingLinearLayoutManager? _cachingLm;

    public int ItemHeightPx => _itemHeightPx;

    public void SetFixedHeight(int px) => _itemHeightPx = px;

    public void SetCachingLayoutManager(CachingLinearLayoutManager? clm) => _cachingLm = clm;

    public VrAdapter(List<object> items, DataTemplate template, IMauiContext mauiContext, int itemHeightPx, Context context)
    {
        _items        = items;
        _template     = template;
        _mauiContext  = mauiContext;
        _itemHeightPx = itemHeightPx;
        _context      = context;
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
        nativeView.LayoutParameters = new RecyclerView.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, h);

        var holder = new VrViewHolder(nativeView, mauiView);
        lock (_allHolders) _allHolders.Add(holder);
        return holder;
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is VrViewHolder h && (uint)position < (uint)_items.Count)
        {
            if (_cachingLm != null)
            {
                // WrapContent: o item se dimensiona pelo conteúdo, expanders funcionam.
                // O Post apenas alimenta o cache do CachingLM para estimativa de scroll.
                h.ItemView.LayoutParameters = new RecyclerView.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);

                h.MauiView.BindingContext = null;
                h.MauiView.BindingContext = _items[position];

                h.CancelHeavyBind();
                h.BindCts = new CancellationTokenSource();
                var token       = h.BindCts.Token;
                var capturedLm  = _cachingLm;
                var capturedPos = position;
                h.ItemView.Post(() =>
                {
                    if (token.IsCancellationRequested) return;
                    int real = h.ItemView.Height;
                    if (real > 0)
                        capturedLm.CacheItemHeight(capturedPos, real);
                });
            }
            else
            {
                h.MauiView.BindingContext = null;
                h.MauiView.BindingContext = _items[position];
            }
        }
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position,
        IList<Java.Lang.Object> payloads)
    {
        if (payloads is { Count: > 0 } && holder is VrViewHolder h &&
            (uint)position < (uint)_items.Count)
        {
            h.MauiView.BindingContext = null;
            h.MauiView.BindingContext = _items[position];
        }
        else
        {
            OnBindViewHolder(holder, position);
        }
    }

    // ── Operações incrementais ────────────────────────────────────────────────

    public void IncrementalAdd(int startIndex, List<object> newItems)
    {
        _items.InsertRange(startIndex, newItems);
        if (newItems.Count == 1)
            NotifyItemInserted(startIndex);
        else
            NotifyItemRangeInserted(startIndex, newItems.Count);
    }

    public void IncrementalRemove(int startIndex, int count)
    {
        _items.RemoveRange(startIndex, count);
        if (count == 1)
            NotifyItemRemoved(startIndex);
        else
            NotifyItemRangeRemoved(startIndex, count);
    }

    public void IncrementalReplace(int startIndex, List<object> newItems)
    {
        for (int i = 0; i < newItems.Count && startIndex + i < _items.Count; i++)
            _items[startIndex + i] = newItems[i];
        NotifyItemRangeChanged(startIndex, newItems.Count);
    }

    public void IncrementalMove(int from, int to)
    {
        var item = _items[from];
        _items.RemoveAt(from);
        _items.Insert(to, item);
        NotifyItemMoved(from, to);
    }

    // ── Substituição completa com DiffUtil assíncrono ─────────────────────────

    public async void UpdateItemsAsync(List<object> newItems, int newHeightPx)
    {
        _itemHeightPx = newHeightPx;
        _cachingLm?.InvalidateCache();

        _diffCts?.Cancel();
        _diffCts?.Dispose();
        _diffCts = new CancellationTokenSource();
        var token   = _diffCts.Token;
        var oldList = _items;

        DiffUtil.DiffResult? result;
        try
        {
            result = await Task.Run(
                () => DiffUtil.CalculateDiff(new VrDiffCallback(oldList, newItems), true),
                token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception) { return; }
        if (token.IsCancellationRequested) return;

        _items = newItems;
        result.DispatchUpdatesTo(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;
        if (disposing)
        {
            _diffCts?.Cancel();
            _diffCts?.Dispose();
            _diffCts = null;
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

// ─────────────────────────────────────────────────────────────────────────────
// Helpers internos
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class VrViewHolder : RecyclerView.ViewHolder
{
    public MauiView MauiView { get; }
    internal CancellationTokenSource? BindCts;

    public VrViewHolder(AView platformView, MauiView mauiView)
        : base(platformView) => MauiView = mauiView;

    public void CancelHeavyBind()
    {
        BindCts?.Cancel();
        BindCts?.Dispose();
        BindCts = null;
    }
}

internal sealed class VrDiffCallback : DiffUtil.Callback
{
    private readonly IList<object> _old;
    private readonly IList<object> _new;

    public VrDiffCallback(IList<object> old, IList<object> @new)
    {
        _old = old;
        _new = @new;
    }

    public override int OldListSize => _old.Count;
    public override int NewListSize => _new.Count;

    public override bool AreItemsTheSame(int op, int np) =>
        ReferenceEquals(_old[op], _new[np]) || (_old[op]?.Equals(_new[np]) ?? false);

    public override bool AreContentsTheSame(int op, int np) =>
        AreItemsTheSame(op, np);
}

internal sealed class VrScrollListener : RecyclerView.OnScrollListener
{
    private readonly Action<int, int> _onScrolled;
    public VrScrollListener(Action<int, int> onScrolled) => _onScrolled = onScrolled;
    public override void OnScrolled(RecyclerView recyclerView, int dx, int dy) => _onScrolled(dx, dy);
}

// RecyclerView que força clipping ao canvas em DispatchDraw. Backup à prova de falhas:
// mesmo se algum ancestral MAUI desabilita clipChildren, ou se o LayoutManager
// posiciona o primeiro item com top negativo (scroll parcial), o desenho dos
// filhos jamais ultrapassa a área retangular dos bounds do RecyclerView. Isso
// impede a row visível vazar acima do RecyclerView e cobrir a SearchBar/header.
internal sealed class ClippedRecyclerView : RecyclerView
{
    public ClippedRecyclerView(Context context) : base(context) { }

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

internal sealed class VrRecyclerListener : Java.Lang.Object, RecyclerView.IRecyclerListener
{
    private readonly Context _context;

    public VrRecyclerListener(Context context) => _context = context;

    public void OnViewRecycled(RecyclerView.ViewHolder holder)
    {
        if (holder is VrViewHolder vh)
        {
            vh.CancelHeavyBind();
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

internal sealed class CachingLinearLayoutManager : LinearLayoutManager
{
    private readonly SparseIntArray _cache = new();
    private int _avgHeight;
    private int _measuredCount;
    private readonly int _fallbackHeightPx;

    public CachingLinearLayoutManager(Context ctx, int fallbackPx) : base(ctx)
    {
        _fallbackHeightPx = fallbackPx;
    }

    public void CacheItemHeight(int position, int heightPx)
    {
        bool isNew = _cache.Get(position, -1) == -1;
        _cache.Put(position, heightPx);
        if (isNew && heightPx > 0)
        {
            _measuredCount++;
            _avgHeight += (heightPx - _avgHeight) / _measuredCount;
        }
    }

    public int GetEstimatedHeight(int position)
    {
        int h = _cache.Get(position, -1);
        if (h != -1) return h;
        return _avgHeight > 0 ? _avgHeight : _fallbackHeightPx;
    }

    public void InvalidateCache()
    {
        _cache.Clear();
        _avgHeight     = 0;
        _measuredCount = 0;
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
        int total = 0;
        for (int i = 0; i < ItemCount; i++)
            total += GetEstimatedHeight(i);
        return total;
    }

    public override int ComputeVerticalScrollExtent(RecyclerView.State state) => Height;
}

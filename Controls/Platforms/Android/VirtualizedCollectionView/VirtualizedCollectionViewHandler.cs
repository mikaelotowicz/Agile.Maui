// Platforms/Android/VirtualizedCollectionView/VirtualizedCollectionViewHandler.cs
using System.Collections;
using System.Collections.Specialized;
using Android.Content;
using Android.Graphics;
using Android.Views;
using AndroidX.RecyclerView.Widget;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

using MauiView = Microsoft.Maui.Controls.View;
using AView    = Android.Views.View;

namespace Controls.Platforms.Android;

public sealed class VirtualizedCollectionViewHandler
    : ViewHandler<VirtualizedCollectionView, RecyclerView>
{
    public static readonly PropertyMapper<VirtualizedCollectionView, VirtualizedCollectionViewHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(VirtualizedCollectionView.ItemsSource)]  = (h, _) => h.ReloadItems(),
            [nameof(VirtualizedCollectionView.ItemTemplate)] = (h, _) => h.ReloadItems(),
            [nameof(VirtualizedCollectionView.ItemHeight)]   = (h, _) => h.ReloadItems(),
            [nameof(VirtualizedCollectionView.ColumnCount)]  = (h, _) => h.ApplyLayoutManager(),
            [nameof(VirtualizedCollectionView.Orientation)]  = (h, _) => h.ApplyLayoutManager(),
            [nameof(VirtualizedCollectionView.RemainingItemsThreshold)]               = (h, _) => { },
            [nameof(VirtualizedCollectionView.RemainingItemsThresholdReachedCommand)] = (h, _) => { },
        };

    // internal porque o tipo do campo não pode ser file-scoped
    private VrAdapter?                 _adapter;
    private VrScrollListener?          _scrollListener;
    private INotifyCollectionChanged?  _collectionChangedSource;

    public VirtualizedCollectionViewHandler() : base(Mapper) { }

    protected override RecyclerView CreatePlatformView()
    {
        var rv = new ClippedRecyclerView(Context);
        rv.HasFixedSize = false;
        rv.SetItemAnimator(null);          // sem animações → scroll mais suave
        rv.SetItemViewCacheSize(20);       // cache offscreen (padrão = 2)
        rv.GetRecycledViewPool().SetMaxRecycledViews(0, 30);
        // Impede propagação de scroll para o CoordinatorLayout do Shell,
        // evitando que o layout da página se desloque e sobreponha o SearchBar.
        rv.NestedScrollingEnabled = false;
        rv.SetClipChildren(true);
        rv.SetClipToPadding(true);
        return rv;
    }

    protected override void ConnectHandler(RecyclerView platformView)
    {
        base.ConnectHandler(platformView);
        // Reforça após ViewMapper do MAUI para garantir que não seja sobrescrito.
        platformView.NestedScrollingEnabled = false;
        platformView.SetClipChildren(true);
        platformView.SetClipToPadding(true);
        ApplyLayoutManager();
        ReloadItems();

        _scrollListener = new VrScrollListener(OnScrolled);
        platformView.AddOnScrollListener(_scrollListener);
    }

    protected override void DisconnectHandler(RecyclerView platformView)
    {
        if (_scrollListener is not null)
        {
            platformView.RemoveOnScrollListener(_scrollListener);
            _scrollListener = null;
        }
        UnsubscribeCollection();
        _adapter?.Dispose();
        _adapter = null;
        platformView.SetAdapter(null);
        platformView.SetLayoutManager(null);
        base.DisconnectHandler(platformView);
    }

    internal void ApplyLayoutManager()
    {
        if (PlatformView is null) return;

        var horizontal = VirtualView.Orientation == VirtualizedOrientation.Horizontal;
        var direction  = horizontal ? LinearLayoutManager.Horizontal : LinearLayoutManager.Vertical;
        var columns    = VirtualView.ColumnCount;

        LinearLayoutManager llm;
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

        PlatformView.SetLayoutManager(llm);
        if (_adapter is not null)
            PlatformView.SetAdapter(_adapter);
    }

    private void ReloadItems()
    {
        if (PlatformView is null || MauiContext is null) return;

        var template = VirtualView.ItemTemplate;
        if (template is null)
        {
            _adapter?.Dispose();
            _adapter = null;
            PlatformView.SetAdapter(null);
            UnsubscribeCollection();
            return;
        }

        var items    = SnapshotItems(VirtualView.ItemsSource);
        var heightPx = VirtualView.ItemHeight > 0
            ? (int)Context.ToPixels(VirtualView.ItemHeight)
            : RecyclerView.LayoutParams.WrapContent;

        UnsubscribeCollection();

        if (_adapter is null)
        {
            _adapter = new VrAdapter(items, template, MauiContext, heightPx);
            PlatformView.SetAdapter(_adapter);
        }
        else
        {
            _adapter.UpdateItemsAsync(items, heightPx);
        }

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
        });
    }

    private void OnScrolled(int dx, int dy)
    {
        if (VirtualView is null || PlatformView is null) return;
        VirtualView.RaiseScrolled(
            Context.FromPixels(PlatformView.ComputeHorizontalScrollOffset()),
            Context.FromPixels(PlatformView.ComputeVerticalScrollOffset()));
        CheckRemainingThreshold();
    }

    private void CheckRemainingThreshold()
    {
        var threshold = VirtualView.RemainingItemsThreshold;
        if (threshold < 0 || _adapter is null || PlatformView is null) return;
        var llm = PlatformView.GetLayoutManager() as LinearLayoutManager;
        if (llm is null) return;
        var lastVisible = llm.FindLastVisibleItemPosition();
        var total       = _adapter.ItemCount;
        if (total > 0 && total - 1 - lastVisible <= threshold)
            VirtualView.RaiseRemainingItemsThresholdReached();
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
// VrAdapter — RecyclerView.Adapter com DiffUtil assíncrono e MAUI view recycling
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class VrAdapter : RecyclerView.Adapter
{
    private readonly DataTemplate        _template;
    private readonly IMauiContext        _mauiContext;
    private          List<object>        _items;
    private          int                 _itemHeightPx;
    private readonly List<VrViewHolder>  _allHolders = [];
    private          CancellationTokenSource? _diffCts;
    private          bool                _disposed;

    public int ItemHeightPx => _itemHeightPx;

    public VrAdapter(List<object> items, DataTemplate template, IMauiContext mauiContext, int itemHeightPx)
    {
        _items       = items;
        _template    = template;
        _mauiContext = mauiContext;
        _itemHeightPx = itemHeightPx;
    }

    public override int ItemCount => _items.Count;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var content  = _template.CreateContent();
        var mauiView = (MauiView)content;

        var nativeView = mauiView.ToPlatform(_mauiContext);
        nativeView.LayoutParameters = _itemHeightPx > 0
            ? new RecyclerView.LayoutParams(ViewGroup.LayoutParams.MatchParent, _itemHeightPx)
            : new RecyclerView.LayoutParams(ViewGroup.LayoutParams.MatchParent,
                                            ViewGroup.LayoutParams.WrapContent);

        var holder = new VrViewHolder(nativeView, mauiView);
        lock (_allHolders) _allHolders.Add(holder);
        return holder;
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is VrViewHolder h && (uint)position < (uint)_items.Count)
        {
            // Limpa antes de rebind para que bindings não vejam contexto antigo
            h.MauiView.BindingContext = null;
            h.MauiView.BindingContext = _items[position];
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
                    h.MauiView.Handler?.DisconnectHandler();
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

    public VrViewHolder(AView platformView, MauiView mauiView)
        : base(platformView) => MauiView = mauiView;
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

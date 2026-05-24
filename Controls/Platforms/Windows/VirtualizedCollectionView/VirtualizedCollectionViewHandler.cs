// Platforms/Windows/VirtualizedCollectionView/VirtualizedCollectionViewHandler.cs
//
// Arquitetura Windows:
//   ScrollViewer (WinUI)
//     └── StackPanel (WinUI)  ← items adicionados aqui como UIElements
//
// Cada item é:
//   1. Criado via DataTemplate.CreateContent() → MAUI View
//   2. Adicionado como filho MAUI via VirtualView.AddMauiChild() → Parent definido
//   3. Convertido para UIElement via ToPlatform() → adicionado ao StackPanel
//
// O Parent MAUI garante que o ciclo Measure/Arrange do MAUI alcance o item.
// Sem Parent, os views são órfãos e CrossPlatformMeasure retorna valores errados.
//
// Limitação atual: sem virtualização WinUI (todos os items estão no StackPanel).
// O infinite scroll limita o total via RemainingItemsThreshold.

using System.Collections;
using System.Collections.Specialized;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using WinScrollViewer         = Microsoft.UI.Xaml.Controls.ScrollViewer;
using WinStackPanel           = Microsoft.UI.Xaml.Controls.StackPanel;
using WinOrientation          = Microsoft.UI.Xaml.Controls.Orientation;
using WinScrollMode           = Microsoft.UI.Xaml.Controls.ScrollMode;
using WinScrollBarVisibility  = Microsoft.UI.Xaml.Controls.ScrollBarVisibility;
using WinHorizontalAlignment  = Microsoft.UI.Xaml.HorizontalAlignment;
using WinFrameworkElement     = Microsoft.UI.Xaml.FrameworkElement;
using MauiDataTemplate        = Microsoft.Maui.Controls.DataTemplate;
using MauiView                = Microsoft.Maui.Controls.View;

namespace Controls.Platforms.Windows;

internal sealed class VirtualizedCollectionViewHandler
    : ViewHandler<VirtualizedCollectionView, WinScrollViewer>
{
    public static readonly PropertyMapper<VirtualizedCollectionView, VirtualizedCollectionViewHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(VirtualizedCollectionView.ItemsSource)]  = (h, _) => h.ReloadItems(),
            [nameof(VirtualizedCollectionView.ItemTemplate)] = (h, _) => h.ReloadItems(),
            [nameof(VirtualizedCollectionView.ItemHeight)]   = (h, _) => h.UpdateHeights(),
            [nameof(VirtualizedCollectionView.ColumnCount)]  = (h, _) => h.ReloadItems(),
            [nameof(VirtualizedCollectionView.Orientation)]  = (h, _) => h.ReloadItems(),
            [nameof(VirtualizedCollectionView.RemainingItemsThreshold)]              = (h, _) => { },
            [nameof(VirtualizedCollectionView.RemainingItemsThresholdReachedCommand)] = (h, _) => { },
        };

    private WinStackPanel      _panel       = new();
    private INotifyCollectionChanged? _collectionChangedSource;

    // Mapa item → (MAUI view, FrameworkElement) para updates e remoções
    private readonly List<(object item, MauiView mauiView, WinFrameworkElement native)> _cells = [];

    public VirtualizedCollectionViewHandler() : base(Mapper) { }

    protected override WinScrollViewer CreatePlatformView()
    {
        _panel = new WinStackPanel
        {
            Orientation = WinOrientation.Vertical,
        };

        return new WinScrollViewer
        {
            Content              = _panel,
            HorizontalScrollMode = WinScrollMode.Disabled,
            VerticalScrollMode   = WinScrollMode.Auto,
            HorizontalScrollBarVisibility = WinScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility   = WinScrollBarVisibility.Auto,
        };
    }

    protected override void ConnectHandler(WinScrollViewer platformView)
    {
        base.ConnectHandler(platformView);
        platformView.ViewChanged += OnScrollViewerViewChanged;
        ReloadItems();
    }

    protected override void DisconnectHandler(WinScrollViewer platformView)
    {
        platformView.ViewChanged -= OnScrollViewerViewChanged;
        UnsubscribeCollection();
        ClearAllCells();
        base.DisconnectHandler(platformView);
    }

    // ── Carga / recarga ──────────────────────────────────────────────────────

    private void ReloadItems()
    {
        if (PlatformView is null || MauiContext is null) return;

        UnsubscribeCollection();
        ClearAllCells();

        var source = VirtualView.ItemsSource;
        if (source is not null)
        {
            foreach (var item in source)
                AppendCell(item);
        }

        SubscribeCollection(source);
    }

    private void UpdateHeights()
    {
        double h = VirtualView.ItemHeight;
        if (h <= 0) return;
        foreach (var (_, _, native) in _cells)
            native.Height = h;
    }

    // ── Gestão de cells ──────────────────────────────────────────────────────

    private void AppendCell(object item)
    {
        var (mauiView, native) = CreateCell(item);
        _cells.Add((item, mauiView, native));
        _panel.Children.Add(native);
    }

    private (MauiView mauiView, WinFrameworkElement native) CreateCell(object item)
    {
        var template = VirtualView.ItemTemplate as MauiDataTemplate
                       ?? new MauiDataTemplate(typeof(Label));

        var mauiView = (MauiView)template.CreateContent();

        // Adicionar como filho MAUI ANTES de ToPlatform() e BindingContext.
        // Isso garante que mauiView.Parent = VirtualizedCollectionView,
        // permitindo que o ciclo Measure/Arrange do MAUI alcance o item.
        VirtualView.AddMauiChild(mauiView);

        // BindingContext depois do Parent, para que o cascade do Parent
        // não sobrescreva o binding explícito do item.
        mauiView.BindingContext = item;

        var native = (WinFrameworkElement)mauiView.ToPlatform(MauiContext!);
        native.HorizontalAlignment = WinHorizontalAlignment.Stretch;

        double h = VirtualView.ItemHeight;
        if (h > 0) native.Height = h;

        return (mauiView, native);
    }

    private void RemoveCellAt(int index)
    {
        if ((uint)index >= (uint)_cells.Count) return;
        var (_, mauiView, native) = _cells[index];
        _panel.Children.Remove(native);
        VirtualView.RemoveMauiChild(mauiView);
        mauiView.Handler?.DisconnectHandler();
        _cells.RemoveAt(index);
    }

    private void ClearAllCells()
    {
        _panel.Children.Clear();
        for (int i = _cells.Count - 1; i >= 0; i--)
        {
            var (_, mauiView, _) = _cells[i];
            VirtualView.RemoveMauiChild(mauiView);
            mauiView.Handler?.DisconnectHandler();
        }
        _cells.Clear();
    }

    // ── INotifyCollectionChanged ─────────────────────────────────────────────

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
        PlatformView?.DispatcherQueue.TryEnqueue(() =>
        {
            if (PlatformView is null || MauiContext is null) return;

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                    for (int i = 0; i < e.NewItems.Count; i++)
                    {
                        var item = e.NewItems[i]!;
                        var (mauiView, native) = CreateCell(item);
                        int idx = e.NewStartingIndex + i;
                        _cells.Insert(idx, (item, mauiView, native));
                        _panel.Children.Insert(idx, native);
                    }
                    CheckRemainingThreshold();
                    break;

                case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                    for (int i = e.OldItems.Count - 1; i >= 0; i--)
                        RemoveCellAt(e.OldStartingIndex + i);
                    break;

                case NotifyCollectionChangedAction.Replace when e.NewItems is not null:
                    for (int i = 0; i < e.NewItems.Count; i++)
                    {
                        int idx = e.NewStartingIndex + i;
                        RemoveCellAt(idx);
                        var (mauiView, native) = CreateCell(e.NewItems[i]!);
                        _cells.Insert(idx, (e.NewItems[i]!, mauiView, native));
                        _panel.Children.Insert(idx, native);
                    }
                    break;

                case NotifyCollectionChangedAction.Move:
                    var moved = _cells[e.OldStartingIndex];
                    _cells.RemoveAt(e.OldStartingIndex);
                    _panel.Children.RemoveAt(e.OldStartingIndex);
                    _cells.Insert(e.NewStartingIndex, moved);
                    _panel.Children.Insert(e.NewStartingIndex, moved.native);
                    break;

                default:
                    ReloadItems();
                    break;
            }
        });
    }

    // ── Scroll e threshold ───────────────────────────────────────────────────

    private void OnScrollViewerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (PlatformView is null || VirtualView is null) return;
        VirtualView.RaiseScrolled(PlatformView.HorizontalOffset, PlatformView.VerticalOffset);
        if (!e.IsIntermediate)
            CheckRemainingThreshold();
    }

    private void CheckRemainingThreshold()
    {
        var threshold = VirtualView?.RemainingItemsThreshold ?? -1;
        if (threshold < 0 || PlatformView is null) return;

        var total    = _cells.Count;
        var itemH    = VirtualView!.ItemHeight > 0 ? VirtualView.ItemHeight : 120;
        var lastVis  = (int)((PlatformView.VerticalOffset + PlatformView.ViewportHeight) / itemH);

        if (total > 0 && total - 1 - lastVis <= threshold)
            VirtualView.RaiseRemainingItemsThresholdReached();
    }
}

// Platforms/Windows/VirtualizedCollectionView/VirtualizedCollectionView.Windows.cs
//
// No Windows, VirtualizedCollectionView herda de ContentView e define
// Content = CollectionView do MAUI, usando o ContentViewHandler padrão.
// No Android/iOS os handlers criam RecyclerView/UICollectionView nativos
// e ignoram o Content.

using Microsoft.UI.Dispatching;
using ItemsLayoutOrientation = Agile.Maui.ItemsLayoutOrientation;
using MauiOrientation = Microsoft.Maui.Controls.ItemsLayoutOrientation;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinScrollView = Microsoft.UI.Xaml.Controls.ScrollView;

namespace Agile.Maui;

public partial class VirtualizedCollectionView
{
    private readonly CollectionView _cv;

    // ── Cursor via reflection (ProtectedCursor é protected em WinUI 3) ────────
    private static readonly System.Reflection.PropertyInfo? s_protectedCursorProp =
        typeof(UIElement).GetProperty(
            "ProtectedCursor",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    // ── Delegates para AddHandler/RemoveHandler (exigem mesma instância) ─────
    private PointerEventHandler? _onDragPressed;
    private PointerEventHandler? _onDragMoved;
    private PointerEventHandler? _onDragReleased;
    private PointerEventHandler? _onDragCaptureLost;

    // ── Estado do drag ────────────────────────────────────────────────────────
    private UIElement?     _dragView;
    private ScrollViewer?  _dragScrollViewer;   // ListView/ListViewBase (MAUI ≤ 9)
    private WinScrollView? _dragScrollView;     // ItemsView (MAUI 10 / WinAppSDK 1.4+)
    private bool           _dragPointerDown;
    private bool           _dragging;
    private global::Windows.Foundation.Point _dragStartPoint;
    private global::Windows.Foundation.Point _dragLastPoint;
    private const double   DragThreshold = 4.0;

    // ── Inércia ───────────────────────────────────────────────────────────────
    // Queue é O(1) no Dequeue vs. O(n) do List.RemoveAt(0).
    // _latestVelSample separa o último item sem precisar de acesso por índice.
    private readonly Queue<(double dx, double dy, long timeMs)> _velQueue = new();
    private (double dx, double dy, long timeMs) _latestVelSample;

    private DispatcherQueueTimer? _inertiaTimer;
    private double _inertiaVx;
    private double _inertiaVy;
    private long   _lastTickMs;

    private const double FrictionFactor        = 0.92;   // decaimento por frame a 16 ms
    private const double MinVelocityPxPerFrame = 0.3;    // velocidade mínima antes de parar
    private const long   VelocityWindowMs      = 100;    // janela de amostras de velocidade
    // ─────────────────────────────────────────────────────────────────────────

    public VirtualizedCollectionView()
    {
        _cv = new CollectionView();
        Content = _cv;
        _cv.RemainingItemsThresholdReached += (_, _) => RaiseRemainingItemsThresholdReached();
        _cv.Scrolled += (_, e) => RaiseScrolled(e.HorizontalOffset, e.VerticalOffset);
        _cv.HandlerChanged += OnCvHandlerChanged;
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
            case nameof(Span):
            case nameof(Orientation):
            case nameof(ItemSpacing):             SyncLayout();                                          break;
        }
    }

    private void SyncLayout()
    {
        var orientation = Orientation == ItemsLayoutOrientation.Vertical
            ? MauiOrientation.Vertical
            : MauiOrientation.Horizontal;
        double spacing = ItemSpacing;
        _cv.ItemsLayout = Span > 1
            ? new GridItemsLayout(Span, orientation)
                { HorizontalItemSpacing = spacing, VerticalItemSpacing = spacing }
            : new LinearItemsLayout(orientation)
                { ItemSpacing = spacing };
    }

    public void ScrollTo(int index, bool animated = true) =>
        _cv.ScrollTo(index, animate: animated);

    // ── Ciclo de vida do handler ──────────────────────────────────────────────

    private void OnCvHandlerChanged(object? sender, EventArgs e)
    {
        DetachDragScroll();
        if (_cv.Handler is not null)
            AttachDragScroll();
    }

    private void AttachDragScroll()
    {
        if (_cv.Handler?.PlatformView is not FrameworkElement fe) return;
        _dragView = fe;

        // AddHandler com handledEventsToo: true é obrigatório porque ListViewItem
        // marca PointerPressed como Handled para seleção; += nunca dispara nesses casos.
        _onDragPressed     = new PointerEventHandler(OnDragPressed);
        _onDragMoved       = new PointerEventHandler(OnDragMoved);
        _onDragReleased    = new PointerEventHandler(OnDragReleased);
        _onDragCaptureLost = new PointerEventHandler(OnDragCaptureLost);

        fe.AddHandler(UIElement.PointerPressedEvent,     _onDragPressed,     handledEventsToo: true);
        fe.AddHandler(UIElement.PointerMovedEvent,       _onDragMoved,       handledEventsToo: true);
        fe.AddHandler(UIElement.PointerReleasedEvent,    _onDragReleased,    handledEventsToo: true);
        fe.AddHandler(UIElement.PointerCaptureLostEvent, _onDragCaptureLost, handledEventsToo: true);

        if (fe.IsLoaded)
            FindScrollTarget(fe);
        else
            fe.Loaded += OnDragViewLoaded;
    }

    private void DetachDragScroll()
    {
        StopInertia();
        if (_dragView is FrameworkElement fe && _onDragPressed is not null)
        {
            fe.RemoveHandler(UIElement.PointerPressedEvent,     _onDragPressed);
            fe.RemoveHandler(UIElement.PointerMovedEvent,       _onDragMoved!);
            fe.RemoveHandler(UIElement.PointerReleasedEvent,    _onDragReleased!);
            fe.RemoveHandler(UIElement.PointerCaptureLostEvent, _onDragCaptureLost!);
            fe.Loaded -= OnDragViewLoaded;
        }
        _dragView          = null;
        _dragScrollViewer  = null;
        _dragScrollView    = null;
        _dragPointerDown   = false;
        _dragging          = false;
        _onDragPressed     = null;
        _onDragMoved       = null;
        _onDragReleased    = null;
        _onDragCaptureLost = null;
        ClearVelSamples();
    }

    private void OnDragViewLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        fe.Loaded -= OnDragViewLoaded;
        // Adia um tick para garantir que o template interno já foi expandido
        fe.DispatcherQueue.TryEnqueue(() => FindScrollTarget(fe));
    }

    private void FindScrollTarget(DependencyObject root)
    {
        _dragScrollViewer = FindDescendant<ScrollViewer>(root);
        if (_dragScrollViewer is null)
            _dragScrollView = FindDescendant<WinScrollView>(root);
    }

    // ── Eventos de ponteiro ───────────────────────────────────────────────────

    private void OnDragPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
        if (!e.GetCurrentPoint(_dragView).Properties.IsLeftButtonPressed) return;
        StopInertia();
        ClearVelSamples();
        _dragPointerDown = true;
        _dragging        = false;
        _dragStartPoint  = e.GetCurrentPoint(_dragView).Position;
        _dragLastPoint   = _dragStartPoint;
    }

    private void OnDragMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragPointerDown || _dragView is null) return;
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;

        var pos = e.GetCurrentPoint(_dragView).Position;

        if (!_dragging)
        {
            double dx = pos.X - _dragStartPoint.X;
            double dy = pos.Y - _dragStartPoint.Y;
            if (dx * dx + dy * dy < DragThreshold * DragThreshold) return;

            if (_dragScrollViewer is null && _dragScrollView is null)
                FindScrollTarget(_dragView);
            if (_dragScrollViewer is null && _dragScrollView is null) return;

            _dragging      = true;
            _dragLastPoint = pos;
            _dragView.CapturePointer(e.Pointer);
            SetDragCursor(InputSystemCursorShape.Hand);
        }

        bool   horizontal = Orientation == ItemsLayoutOrientation.Horizontal;
        double deltaX     = _dragLastPoint.X - pos.X;
        double deltaY     = _dragLastPoint.Y - pos.Y;
        _dragLastPoint    = pos;

        DoScroll(horizontal ? deltaX : 0, horizontal ? 0 : deltaY);

        // Registra amostra e descarta as mais antigas que VelocityWindowMs
        long now    = Environment.TickCount64;
        var  sample = (deltaX, deltaY, now);
        _velQueue.Enqueue(sample);
        _latestVelSample = sample;
        long cutoff = now - VelocityWindowMs;
        while (_velQueue.Count > 1 && _velQueue.Peek().timeMs < cutoff)
            _velQueue.Dequeue();

        e.Handled = true;
    }

    private void OnDragReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            RestoreDragCursor();
            StartInertia();                              // ① lê e limpa a queue
            _dragView?.ReleasePointerCapture(e.Pointer); // ② pode disparar CaptureLost de forma
                                                         //    síncrona — queue já consumida
        }
        else
        {
            ClearVelSamples();
        }
        _dragPointerDown = false;
        _dragging        = false;
    }

    private void OnDragCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _dragPointerDown = false;
        _dragging        = false;
        RestoreDragCursor();
        // Não interrompe inércia já em andamento (timer criado em StartInertia).
        // ReleasePointerCapture em OnDragReleased dispara este evento de forma síncrona,
        // mas nesse ponto StartInertia já criou o timer.
        if (_inertiaTimer is null)
            ClearVelSamples();
    }

    // ── Inércia ───────────────────────────────────────────────────────────────

    private void StartInertia()
    {
        if (_velQueue.Count < 2 || (_dragScrollViewer is null && _dragScrollView is null))
        {
            ClearVelSamples();
            return;
        }

        var    first     = _velQueue.Peek();
        var    last      = _latestVelSample;
        double elapsedMs = Math.Max(last.timeMs - first.timeMs, 1.0);

        double totalDx = 0, totalDy = 0;
        foreach (var (dx, dy, _) in _velQueue) { totalDx += dx; totalDy += dy; }
        ClearVelSamples();

        // Converte para pixels por frame a 60 fps (16 ms/frame)
        const double msPerFrame = 1000.0 / 60.0;
        _inertiaVx = totalDx / elapsedMs * msPerFrame;
        _inertiaVy = totalDy / elapsedMs * msPerFrame;

        if (Math.Abs(_inertiaVx) + Math.Abs(_inertiaVy) < MinVelocityPxPerFrame) return;

        if (_dragView is not FrameworkElement fe) return;
        StopInertia();
        _lastTickMs              = Environment.TickCount64;
        _inertiaTimer            = fe.DispatcherQueue.CreateTimer();
        _inertiaTimer.Interval   = TimeSpan.FromMilliseconds(16);
        _inertiaTimer.IsRepeating = true;
        _inertiaTimer.Tick       += OnInertiaTick;
        _inertiaTimer.Start();
    }

    private void StopInertia()
    {
        if (_inertiaTimer is null) return;
        _inertiaTimer.Stop();
        _inertiaTimer.Tick -= OnInertiaTick;
        _inertiaTimer = null;
    }

    private void OnInertiaTick(DispatcherQueueTimer sender, object args)
    {
        // Física baseada no tempo real: compensa timer atrasado (UI thread ocupada)
        long   now          = Environment.TickCount64;
        double elapsedMs    = Math.Clamp(now - _lastTickMs, 1.0, 100.0);
        _lastTickMs         = now;
        double framesElapsed = elapsedMs / 16.0;

        // Checa velocidade mínima antes de qualquer trabalho
        if (Math.Abs(_inertiaVx) + Math.Abs(_inertiaVy) < MinVelocityPxPerFrame)
        {
            StopInertia();
            return;
        }

        // Deslocamento proporcional ao tempo real decorrido (v × frames)
        bool   horizontal = Orientation == ItemsLayoutOrientation.Horizontal;
        double moveX      = horizontal ? _inertiaVx * framesElapsed : 0;
        double moveY      = horizontal ? 0 : _inertiaVy * framesElapsed;

        // Para ao atingir a borda do scroll
        if (!HasScrollCapacity(moveX, moveY))
        {
            StopInertia();
            return;
        }

        DoScroll(moveX, moveY);

        // Aplica atrito proporcional ao tempo (FrictionFactor^frames)
        double decay = Math.Pow(FrictionFactor, framesElapsed);
        _inertiaVx *= decay;
        _inertiaVy *= decay;
    }

    // ── Helpers de scroll ─────────────────────────────────────────────────────

    private void DoScroll(double deltaX, double deltaY)
    {
        if (_dragScrollViewer is not null)
        {
            _dragScrollViewer.ChangeView(
                _dragScrollViewer.HorizontalOffset + deltaX,
                _dragScrollViewer.VerticalOffset   + deltaY,
                null, disableAnimation: true);
        }
        else if (_dragScrollView is not null)
        {
            _ = _dragScrollView.ScrollBy(deltaX, deltaY);
        }
    }

    // Retorna false quando o scroll já encostou na borda na direção do movimento,
    // evitando que o timer fique rodando sem efeito.
    private bool HasScrollCapacity(double deltaX, double deltaY)
    {
        bool horizontal = Orientation == ItemsLayoutOrientation.Horizontal;

        if (_dragScrollViewer is not null)
        {
            return horizontal
                ? deltaX > 0
                    ? _dragScrollViewer.HorizontalOffset < _dragScrollViewer.ScrollableWidth  - 0.5
                    : _dragScrollViewer.HorizontalOffset > 0.5
                : deltaY > 0
                    ? _dragScrollViewer.VerticalOffset   < _dragScrollViewer.ScrollableHeight - 0.5
                    : _dragScrollViewer.VerticalOffset   > 0.5;
        }

        if (_dragScrollView is not null)
        {
            return horizontal
                ? deltaX > 0
                    ? _dragScrollView.HorizontalOffset < _dragScrollView.ScrollableWidth  - 0.5
                    : _dragScrollView.HorizontalOffset > 0.5
                : deltaY > 0
                    ? _dragScrollView.VerticalOffset   < _dragScrollView.ScrollableHeight - 0.5
                    : _dragScrollView.VerticalOffset   > 0.5;
        }

        return false;
    }

    private void ClearVelSamples()
    {
        _velQueue.Clear();
        _latestVelSample = default;
    }

    // ── Cursor ────────────────────────────────────────────────────────────────

    private void SetDragCursor(InputSystemCursorShape shape)
    {
        if (_dragView is null) return;
        s_protectedCursorProp?.SetValue(_dragView, InputSystemCursor.Create(shape));
    }

    private void RestoreDragCursor()
    {
        if (_dragView is null) return;
        s_protectedCursorProp?.SetValue(_dragView,
            InputSystemCursor.Create(InputSystemCursorShape.Arrow));
    }

    // ── Utilitário de árvore visual ───────────────────────────────────────────

    private static T? FindDescendant<T>(DependencyObject element)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is T found) return found;
            var result = FindDescendant<T>(child);
            if (result is not null) return result;
        }
        return null;
    }
}

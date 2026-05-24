// sample/VirtualizedListPage.xaml.cs
using System.Collections.ObjectModel;
using Controls;

namespace sample;

public partial class VirtualizedListPage : ContentPage
{
    private const int InitialBatch   = 500;
    private const int LoadMoreBatch  = 200;
    private const int FixedItemHeight = 112;

    // Todos os itens gerados (para busca local)
    private readonly List<ProductItem> _allItems;

    // ObservableCollection exposta via binding no XAML
    public ObservableCollection<ProductItem> Items { get; } = [];

    private int  _nextId      = 0;
    private int  _columnCount = 1;
    private bool _fixedHeight = false;
    private bool _isLoading   = false;
    private string _currentSearch = "";

    private readonly PerformanceMonitor _perf = new();
    private          IDispatcherTimer?   _decayTimer;
    private          bool                _firstLayoutDone = false;

    public VirtualizedListPage()
    {
        _perf.StartLoad();
        InitializeComponent();
        BindingContext = this;

        // Gera todo o dataset no background para não travar a UI
        _allItems = ProductItem.GenerateBatch(0, 2000);
        _nextId   = _allItems.Count;

        LoadInitialItems();
        StartFpsDecayTimer();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_firstLayoutDone)
        {
            _firstLayoutDone = true;
            _perf.EndLoad();
            UpdateMetricsLabel();
        }
    }

    private IDispatcherTimer? _uiTickTimer;

    private void StartFpsDecayTimer()
    {
        _uiTickTimer = Dispatcher.CreateTimer();
        _uiTickTimer.Interval = TimeSpan.FromMilliseconds(16);
        _uiTickTimer.Tick += (_, _) => _perf.UiTick();
        _uiTickTimer.Start();

        _decayTimer = Dispatcher.CreateTimer();
        _decayTimer.Interval = TimeSpan.FromMilliseconds(200);
        _decayTimer.Tick += (_, _) =>
        {
            _perf.Decay();
            UpdateMetricsLabel();
        };
        _decayTimer.Start();
    }

    private void UpdateMetricsLabel()
    {
        FpsLabel.Text     = $"UI {_perf.UiFps:F0} · Scroll {_perf.ScrollFps:F0}";
        MetricsLabel.Text = _perf.FormatReport();
    }

    // ── Carga inicial ────────────────────────────────────────────────────

    private void LoadInitialItems()
    {
        var batch = _allItems.Take(InitialBatch).ToList();
        foreach (var item in batch)
            Items.Add(item);

        UpdateCountLabel();
        StatusLabel.Text = $"{InitialBatch} produtos carregados. Role para baixo para mais.";
    }

    // ── Infinite scroll ──────────────────────────────────────────────────

    private async void OnLoadMore(object? sender, EventArgs e)
    {
        if (_isLoading || !string.IsNullOrEmpty(_currentSearch)) return;
        _isLoading = true;
        LoadingIndicator.IsVisible  = true;
        LoadingIndicator.IsRunning  = true;
        StatusLabel.Text = "Carregando mais...";

        // Simula latência de rede (ex: API paginada)
        await Task.Delay(600);

        // Gera novo lote se necessário
        if (_nextId >= _allItems.Count)
        {
            var extra = ProductItem.GenerateBatch(_nextId, LoadMoreBatch);
            _allItems.AddRange(extra);
            _nextId += LoadMoreBatch;
        }

        var start = Items.Count;
        var toAdd = _allItems.Skip(start).Take(LoadMoreBatch).ToList();
        foreach (var item in toAdd)
            Items.Add(item);

        UpdateCountLabel();
        StatusLabel.Text = $"+{toAdd.Count} itens adicionados. Total: {Items.Count}";

        LoadingIndicator.IsRunning  = false;
        LoadingIndicator.IsVisible  = false;
        _isLoading = false;
    }

    // ── Busca / filtro ───────────────────────────────────────────────────

    private CancellationTokenSource? _searchCts;

    private async void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token  = _searchCts.Token;
        var query  = e.NewTextValue?.Trim() ?? "";

        // Debounce de 300ms para não filtrar a cada tecla
        try { await Task.Delay(300, token); }
        catch (OperationCanceledException) { return; }

        _currentSearch = query;

        if (string.IsNullOrEmpty(query))
        {
            // Volta ao estado completo
            Items.Clear();
            foreach (var item in _allItems.Take(InitialBatch))
                Items.Add(item);
            UpdateCountLabel();
            StatusLabel.Text = "Busca cancelada.";
            return;
        }

        // Filtra no background
        List<ProductItem> results;
        try
        {
            results = await Task.Run(() =>
                _allItems.Where(p =>
                    p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    p.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    p.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList(), token);
        }
        catch (OperationCanceledException) { return; }

        Items.Clear();
        foreach (var item in results)
            Items.Add(item);

        UpdateCountLabel();
        StatusLabel.Text = results.Count == 0
            ? $"Nenhum resultado para \"{query}\""
            : $"{results.Count} resultado(s) para \"{query}\"";
    }

    // ── Toggle grade / lista ─────────────────────────────────────────────

    private void OnColumnToggleClicked(object? sender, EventArgs e)
    {
        _columnCount = _columnCount == 1 ? 2 : 1;
        ProductList.ColumnCount = _columnCount;

        ColumnToggleButton.Text = _columnCount == 1 ? "⊞ Grade" : "☰ Lista";

        // Ajusta a altura ao mudar colunas (grade precisa de mais espaço vertical)
        if (_fixedHeight)
            ProductList.ItemHeight = _columnCount == 1 ? FixedItemHeight : FixedItemHeight + 40;

        StatusLabel.Text = _columnCount == 1 ? "Modo lista (1 coluna)" : "Modo grade (2 colunas)";
    }

    // ── Toggle altura fixa / wrap ────────────────────────────────────────

    private void OnHeightToggleClicked(object? sender, EventArgs e)
    {
        _fixedHeight = !_fixedHeight;

        if (_fixedHeight)
        {
            ProductList.ItemHeight = _columnCount == 1 ? FixedItemHeight : FixedItemHeight + 40;
            HeightToggleButton.Text            = "↕ Auto";
            HeightToggleButton.BackgroundColor = Color.FromArgb("#512BD4");
            HeightToggleButton.TextColor       = Colors.White;
            StatusLabel.Text = $"Altura fixa: {ProductList.ItemHeight}dp — RecyclerView mais rápido";
        }
        else
        {
            ProductList.ItemHeight = -1;
            HeightToggleButton.Text            = "↕ Fixo";
            HeightToggleButton.BackgroundColor = Application.Current!.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#4D4D4D") : Color.FromArgb("#E0E0E0");
            HeightToggleButton.TextColor = Application.Current.RequestedTheme == AppTheme.Dark
                ? Colors.White : Colors.Black;
            StatusLabel.Text = "Altura automática (wrap_content) — mais flexível, mais lento";
        }
    }

    // ── Eventos de scroll ────────────────────────────────────────────────

    private long _lastScrollTick;

    private void OnScrolled(object? sender, VirtualizedScrolledEventArgs e)
    {
        // ScrollTick sempre (para event-rate preciso). Update do label é throttled
        // a 10fps para evitar re-layouts MAUI excessivos durante fling.
        _perf.ScrollTick();
        var now = Environment.TickCount64;
        if (now - _lastScrollTick < 100) return;
        _lastScrollTick = now;
        ScrollLabel.Text = $"Y: {e.ScrollY:F0}dp";
        UpdateMetricsLabel();
    }

    // ── Expanders ────────────────────────────────────────────────────────

    private void OnSpecsTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject b && b.BindingContext is ProductItem p)
            p.IsExpandedSpecs = !p.IsExpandedSpecs;
    }

    private void OnReviewsTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject b && b.BindingContext is ProductItem p)
            p.IsExpandedReviews = !p.IsExpandedReviews;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private void UpdateCountLabel() =>
        ItemCountLabel.Text = $"{Items.Count:N0} produto{(Items.Count != 1 ? "s" : "")}";
}

// sample/CollectionViewPage.xaml.cs
using System.Collections.ObjectModel;

namespace sample;

public partial class CollectionViewPage : ContentPage
{
    private const int InitialBatch  = 500;
    private const int LoadMoreBatch = 200;

    private readonly List<ProductItem>     _allItems;
    public  ObservableCollection<ProductItem> Items { get; } = [];

    private readonly PerformanceMonitor _perf  = new();
    private          IDispatcherTimer?   _decayTimer;

    private int    _nextId        = 0;
    private int    _columnCount   = 1;
    private bool   _isLoading     = false;
    private string _currentSearch = "";
    private bool   _firstLayoutDone = false;

    public CollectionViewPage()
    {
        _perf.StartLoad();
        InitializeComponent();
        BindingContext = this;

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

    // ── FPS decay timer ──────────────────────────────────────────────────

    private IDispatcherTimer? _uiTickTimer;

    private void StartFpsDecayTimer()
    {
        // Timer rápido (~60fps) mede fluidez do UI thread
        _uiTickTimer = Dispatcher.CreateTimer();
        _uiTickTimer.Interval = TimeSpan.FromMilliseconds(16);
        _uiTickTimer.Tick += (_, _) => _perf.UiTick();
        _uiTickTimer.Start();

        // Timer lento atualiza UI das métricas e expira ScrollFps quando idle
        _decayTimer = Dispatcher.CreateTimer();
        _decayTimer.Interval = TimeSpan.FromMilliseconds(200);
        _decayTimer.Tick += (_, _) =>
        {
            _perf.Decay();
            UpdateMetricsLabel();
        };
        _decayTimer.Start();
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
        LoadingIndicator.IsVisible = true;
        LoadingIndicator.IsRunning = true;
        StatusLabel.Text = "Carregando mais...";

        await Task.Delay(600);

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

        LoadingIndicator.IsRunning = false;
        LoadingIndicator.IsVisible = false;
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

        try { await Task.Delay(300, token); }
        catch (OperationCanceledException) { return; }

        _currentSearch = query;

        if (string.IsNullOrEmpty(query))
        {
            Items.Clear();
            foreach (var item in _allItems.Take(InitialBatch))
                Items.Add(item);
            UpdateCountLabel();
            StatusLabel.Text = "Busca cancelada.";
            return;
        }

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

        ProductList.ItemsLayout = _columnCount == 1
            ? new LinearItemsLayout(ItemsLayoutOrientation.Vertical)
            : new GridItemsLayout(2, ItemsLayoutOrientation.Vertical);

        ColumnToggleButton.Text = _columnCount == 1 ? "⊞ Grade" : "☰ Lista";
        StatusLabel.Text = _columnCount == 1 ? "Modo lista (1 coluna)" : "Modo grade (2 colunas)";
    }

    // ── Reset métricas ───────────────────────────────────────────────────

    private void OnResetClicked(object? sender, EventArgs e)
    {
        _perf.Reset();
        _perf.StartLoad();
        _perf.EndLoad();      // só pra deixar 0
        UpdateMetricsLabel();
        StatusLabel.Text = "Métricas zeradas. Role para medir.";
    }

    // ── Eventos de scroll ────────────────────────────────────────────────

    private void OnScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        _perf.ScrollTick();
        ScrollLabel.Text = $"Y: {e.VerticalOffset:F0}dp";
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

    private void UpdateMetricsLabel()
    {
        FpsLabel.Text     = $"UI {_perf.UiFps:F0} · Scroll {_perf.ScrollFps:F0}";
        MetricsLabel.Text = _perf.FormatReport();
    }
}

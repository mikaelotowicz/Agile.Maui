using System.Collections.ObjectModel;
using Agile.Maui;

namespace sample;

public partial class VirtualizedListPage : ContentPage
{
    private const int InitialBatch = 500;
    private const int LoadMoreBatch = 200;
    private const int FixedItemHeight = 112;

    private readonly List<ProductItem> _allItems;

    public ObservableCollection<ProductItem> Items { get; } = [];

    private int _nextId;
    private int _columnCount = 1;
    private bool _fixedHeight;
    private bool _isLoading;
    private string _currentSearch = "";

    private readonly PerformanceMonitor _perf = new();
    private IDispatcherTimer? _decayTimer;
    private IDispatcherTimer? _uiTickTimer;
    private bool _firstLayoutDone;

    public VirtualizedListPage()
    {
        _perf.StartLoad();
        InitializeComponent();
        BindingContext = this;

        _allItems = ProductItem.GenerateBatch(0, 2000);
        _nextId = _allItems.Count;

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
        FpsLabel.Text = $"UI {_perf.UiFps:F0} · Scroll {_perf.ScrollFps:F0}";
        MetricsLabel.Text = _perf.FormatReport();
    }

    private void LoadInitialItems()
    {
        var batch = _allItems.Take(InitialBatch).ToList();
        foreach (var item in batch)
            Items.Add(item);

        UpdateCountLabel();
        StatusLabel.Text = $"{InitialBatch} products loaded. Scroll down to load more.";
    }

    private async void OnLoadMore(object? sender, EventArgs e)
    {
        if (_isLoading || !string.IsNullOrEmpty(_currentSearch)) return;
        _isLoading = true;
        LoadingIndicator.IsVisible = true;
        LoadingIndicator.IsRunning = true;
        StatusLabel.Text = "Loading more...";

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
        StatusLabel.Text = $"+{toAdd.Count} items added. Total: {Items.Count}";

        LoadingIndicator.IsRunning = false;
        LoadingIndicator.IsVisible = false;
        _isLoading = false;
    }

    private CancellationTokenSource? _searchCts;

    private async void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        var query = e.NewTextValue?.Trim() ?? "";

        try { await Task.Delay(300, token); }
        catch (OperationCanceledException) { return; }

        _currentSearch = query;

        if (string.IsNullOrEmpty(query))
        {
            Items.Clear();
            foreach (var item in _allItems.Take(InitialBatch))
                Items.Add(item);
            UpdateCountLabel();
            StatusLabel.Text = "Search cleared.";
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
            ? $"No results for \"{query}\""
            : $"{results.Count} result(s) for \"{query}\"";
    }

    private void OnColumnToggleClicked(object? sender, EventArgs e)
    {
        _columnCount = _columnCount == 1 ? 2 : 1;
        ProductList.Span = _columnCount;

        ColumnToggleButton.Text = _columnCount == 1 ? "Grid" : "List";

        if (_fixedHeight)
            ProductList.ItemHeight = _columnCount == 1 ? FixedItemHeight : FixedItemHeight + 40;

        StatusLabel.Text = _columnCount == 1 ? "List mode (1 column)" : "Grid mode (2 columns)";
    }

    private void OnHeightToggleClicked(object? sender, EventArgs e)
    {
        _fixedHeight = !_fixedHeight;

        if (_fixedHeight)
        {
            ProductList.ItemHeight = _columnCount == 1 ? FixedItemHeight : FixedItemHeight + 40;
            HeightToggleButton.Text = "Auto";
            HeightToggleButton.BackgroundColor = Color.FromArgb("#512BD4");
            HeightToggleButton.TextColor = Colors.White;
            StatusLabel.Text = $"Fixed height: {ProductList.ItemHeight}dp - faster native virtualization";
        }
        else
        {
            ProductList.ItemHeight = -1;
            HeightToggleButton.Text = "Fixed";
            HeightToggleButton.BackgroundColor = Color.FromArgb("#C8C8C8");
            HeightToggleButton.TextColor = Color.FromArgb("#111111");
            StatusLabel.Text = "Dynamic height - more flexible, more measurement work";
        }
    }

    private long _lastScrollTick;

    private void OnScrolled(object? sender, VirtualizedScrolledEventArgs e)
    {
        _perf.ScrollTick();
        var now = Environment.TickCount64;
        if (now - _lastScrollTick < 100) return;
        _lastScrollTick = now;
        ScrollLabel.Text = $"Y: {e.VerticalOffset:F0}dp";
        UpdateMetricsLabel();
    }

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

    private void UpdateCountLabel() =>
        ItemCountLabel.Text = $"{Items.Count:N0} product{(Items.Count != 1 ? "s" : "")}";
}

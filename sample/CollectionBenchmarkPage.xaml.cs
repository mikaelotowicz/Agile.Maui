using System.Diagnostics;
using Agile.Maui;

namespace sample;

public partial class CollectionBenchmarkPage : ContentPage
{
    private const int TotalItems = 5000;
    private const int WarmupItems = 120;
    private const int ScrollSteps = 110;
    private const int ScrollDelayMs = 12;
    private const int ManualSeconds = 10;

    private readonly List<ProductItem> _sourceItems = ProductItem.GenerateBatch(0, TotalItems);
    private readonly List<ProductItem> _warmupItems;
    private readonly IDispatcherTimer _frameTimer;
    private BenchmarkMetrics? _activeMetrics;
    private BenchmarkTarget _activeTarget;
    private BenchmarkMetrics? _collectionJumpResult;
    private BenchmarkMetrics? _virtualizedJumpResult;
    private BenchmarkMetrics? _collectionManualResult;
    private BenchmarkMetrics? _virtualizedManualResult;
    private BenchmarkScenario _lastScenario = BenchmarkScenario.Jump;
    private bool _isRunning;
    private bool _runVirtualizedFirst;

    public ObservableRangeCollection<ProductItem> CollectionItems { get; } = [];
    public ObservableRangeCollection<ProductItem> VirtualizedItems { get; } = [];

    public CollectionBenchmarkPage()
    {
        InitializeComponent();
        BindingContext = this;
        _warmupItems = _sourceItems.Take(WarmupItems).ToList();

        _frameTimer = Dispatcher.CreateTimer();
        _frameTimer.Interval = TimeSpan.FromMilliseconds(16);
        _frameTimer.Tick += (_, _) => _activeMetrics?.UiTick();
        _frameTimer.Start();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_frameTimer.IsRunning)
            _frameTimer.Start();
    }

    protected override void OnDisappearing()
    {
        _frameTimer.Stop();
        base.OnDisappearing();
    }

    private async void OnRunClicked(object? sender, EventArgs e)
    {
        if (_isRunning)
            return;

        BeginRun("Jump: running");

        try
        {
            _lastScenario = BenchmarkScenario.Jump;
            _collectionJumpResult = null;
            _virtualizedJumpResult = null;
            UpdateResultLabels();

            StatusLabel.Text = "Warming up";
            PhaseLabel.Text = "Neutral warm-up";
            await WarmupAsync(BenchmarkTarget.CollectionView);
            await WarmupAsync(BenchmarkTarget.VirtualizedCollectionView);
            await CooldownAsync();

            var firstTarget = _runVirtualizedFirst
                ? BenchmarkTarget.VirtualizedCollectionView
                : BenchmarkTarget.CollectionView;
            var secondTarget = firstTarget == BenchmarkTarget.CollectionView
                ? BenchmarkTarget.VirtualizedCollectionView
                : BenchmarkTarget.CollectionView;

            StoreResult(await RunJumpPhaseAsync(firstTarget, 1));
            UpdateResultLabels();
            await CooldownAsync();

            StoreResult(await RunJumpPhaseAsync(secondTarget, 2));
            UpdateResultLabels();
            UpdateWinner();
            _runVirtualizedFirst = !_runVirtualizedFirst;

            StatusLabel.Text = "Completed";
            PhaseLabel.Text = $"{TotalItems:N0} items, {ScrollSteps:N0} jump steps, order alternates";
        }
        finally
        {
            EndRun();
        }
    }

    private async void OnManualCollectionClicked(object? sender, EventArgs e) =>
        await RunManualAsync(BenchmarkTarget.CollectionView);

    private async void OnManualVirtualizedClicked(object? sender, EventArgs e) =>
        await RunManualAsync(BenchmarkTarget.VirtualizedCollectionView);

    private void OnResetClicked(object? sender, EventArgs e)
    {
        if (_isRunning)
            return;

        CollectionItems.ReplaceAll([]);
        VirtualizedItems.ReplaceAll([]);
        _collectionJumpResult = null;
        _virtualizedJumpResult = null;
        _collectionManualResult = null;
        _virtualizedManualResult = null;
        _activeMetrics = null;

        CollectionList.IsVisible = false;
        VirtualizedList.IsVisible = false;
        IdleOverlay.IsVisible = true;

        StatusLabel.Text = "Ready";
        PhaseLabel.Text = "Run Jump or one manual 10s pass";
        WinnerLabel.Text = "Result: awaiting execution";
        UpdateResultLabels();
    }

    private async Task RunManualAsync(BenchmarkTarget target)
    {
        if (_isRunning)
            return;

        BeginRun("Manual: scroll with your finger");

        try
        {
            _lastScenario = BenchmarkScenario.Manual;
            ClearManualResult(target);
            UpdateResultLabels();

            var metrics = await RunManualPhaseAsync(target);
            StoreResult(metrics);
            UpdateResultLabels();
            UpdateWinner();

            StatusLabel.Text = "Manual completed";
            PhaseLabel.Text = $"Run the other manual pass for comparison";
        }
        finally
        {
            EndRun();
        }
    }

    private void BeginRun(string resultText)
    {
        _isRunning = true;
        SetControlsEnabled(false);
        WinnerLabel.Text = resultText;
    }

    private void EndRun()
    {
        _activeMetrics = null;
        _isRunning = false;
        SetControlsEnabled(true);
    }

    private void SetControlsEnabled(bool enabled)
    {
        RunButton.IsEnabled = enabled;
        ManualCollectionButton.IsEnabled = enabled;
        ManualVirtualizedButton.IsEnabled = enabled;
        ResetButton.IsEnabled = enabled;
    }

    private async Task WarmupAsync(BenchmarkTarget target)
    {
        _activeTarget = target;
        _activeMetrics = null;

        ShowTarget(target);
        ClearTarget(target);
        await WaitForUiAsync(80);

        AddItems(target, _warmupItems);
        await WaitForUiAsync(120);
        ScrollTo(target, WarmupItems - 1);
        await WaitForUiAsync(120);
        ScrollTo(target, 0);
        await WaitForUiAsync(80);

        ClearTarget(target);
        await WaitForUiAsync(80);
    }

    private async Task<BenchmarkMetrics> RunJumpPhaseAsync(BenchmarkTarget target, int phaseNumber)
    {
        var metrics = new BenchmarkMetrics(target, BenchmarkScenario.Jump);

        _activeTarget = target;
        _activeMetrics = null;

        ShowTarget(target);
        ClearTarget(target);
        await WaitForUiAsync(150);
        CollectGarbage();

        StatusLabel.Text = metrics.Name;
        PhaseLabel.Text = $"{phaseNumber} / 2 jump loading";

        metrics.StartMemoryBytes = GC.GetTotalMemory(forceFullCollection: true);
        var bindWatch = Stopwatch.StartNew();
        AddItems(target, _sourceItems);
        await WaitForUiAsync(1);
        bindWatch.Stop();
        metrics.BindMilliseconds = bindWatch.Elapsed.TotalMilliseconds;

        await WaitForUiAsync(250);

        PhaseLabel.Text = $"{phaseNumber} / 2 programmatic jump";
        metrics.ResetFrameSampling();
        _activeMetrics = metrics;

        var scrollWatch = Stopwatch.StartNew();
        await RunScrollScriptAsync(target);
        scrollWatch.Stop();
        _activeMetrics = null;

        metrics.ScrollMilliseconds = scrollWatch.Elapsed.TotalMilliseconds;
        metrics.EndMemoryBytes = GC.GetTotalMemory(forceFullCollection: false);
        metrics.Finish(TotalItems);

        return metrics;
    }

    private async Task<BenchmarkMetrics> RunManualPhaseAsync(BenchmarkTarget target)
    {
        var metrics = new BenchmarkMetrics(target, BenchmarkScenario.Manual);

        _activeTarget = target;
        _activeMetrics = null;

        ShowTarget(target);
        ClearTarget(target);
        await WaitForUiAsync(150);
        CollectGarbage();

        StatusLabel.Text = metrics.Name;
        PhaseLabel.Text = "Manual loading";

        metrics.StartMemoryBytes = GC.GetTotalMemory(forceFullCollection: true);
        var bindWatch = Stopwatch.StartNew();
        AddItems(target, _sourceItems);
        await WaitForUiAsync(1);
        bindWatch.Stop();
        metrics.BindMilliseconds = bindWatch.Elapsed.TotalMilliseconds;

        await WaitForUiAsync(250);
        ScrollTo(target, 0);
        await WaitForUiAsync(80);

        metrics.ResetFrameSampling();
        _activeMetrics = metrics;

        var scrollWatch = Stopwatch.StartNew();
        for (var remaining = ManualSeconds; remaining > 0; remaining--)
        {
            StatusLabel.Text = $"{metrics.Name} manual";
            PhaseLabel.Text = $"Scroll now with your finger: {remaining}s";
            await WaitForUiAsync(1000);
        }

        scrollWatch.Stop();
        _activeMetrics = null;

        metrics.ScrollMilliseconds = scrollWatch.Elapsed.TotalMilliseconds;
        metrics.EndMemoryBytes = GC.GetTotalMemory(forceFullCollection: false);
        metrics.Finish(TotalItems);

        return metrics;
    }

    private void ShowTarget(BenchmarkTarget target)
    {
        IdleOverlay.IsVisible = false;
        CollectionList.IsVisible = target == BenchmarkTarget.CollectionView;
        VirtualizedList.IsVisible = target == BenchmarkTarget.VirtualizedCollectionView;
    }

    private void ClearTarget(BenchmarkTarget target)
    {
        if (target == BenchmarkTarget.CollectionView)
        {
            VirtualizedItems.ReplaceAll([]);
            CollectionItems.ReplaceAll([]);
            return;
        }

        CollectionItems.ReplaceAll([]);
        VirtualizedItems.ReplaceAll([]);
    }

    private void AddItems(BenchmarkTarget target, IReadOnlyList<ProductItem> items)
    {
        if (target == BenchmarkTarget.CollectionView)
            CollectionItems.AddRange(items);
        else
            VirtualizedItems.AddRange(items);
    }

    private async Task RunScrollScriptAsync(BenchmarkTarget target)
    {
        ScrollTo(target, 0);
        await WaitForUiAsync(80);

        for (var step = 0; step <= ScrollSteps; step++)
        {
            var index = (int)Math.Round(step * (TotalItems - 1) / (double)ScrollSteps);
            ScrollTo(target, index);
            await WaitForUiAsync(ScrollDelayMs);
        }

        for (var step = ScrollSteps; step >= 0; step--)
        {
            var index = (int)Math.Round(step * (TotalItems - 1) / (double)ScrollSteps);
            ScrollTo(target, index);
            await WaitForUiAsync(ScrollDelayMs);
        }
    }

    private void ScrollTo(BenchmarkTarget target, int index)
    {
        if (target == BenchmarkTarget.CollectionView)
        {
            CollectionList.ScrollTo(index, position: ScrollToPosition.Start, animate: false);
            return;
        }

        VirtualizedList.ScrollTo(index, false);
    }

    private void OnCollectionScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        if (_activeTarget == BenchmarkTarget.CollectionView)
            _activeMetrics?.ScrollTick();
    }

    private void OnVirtualizedScrolled(object? sender, VirtualizedScrolledEventArgs e)
    {
        if (_activeTarget == BenchmarkTarget.VirtualizedCollectionView)
            _activeMetrics?.ScrollTick();
    }

    private void StoreResult(BenchmarkMetrics metrics)
    {
        if (metrics.Scenario == BenchmarkScenario.Jump)
        {
            if (metrics.Target == BenchmarkTarget.CollectionView)
                _collectionJumpResult = metrics;
            else
                _virtualizedJumpResult = metrics;

            return;
        }

        if (metrics.Target == BenchmarkTarget.CollectionView)
            _collectionManualResult = metrics;
        else
            _virtualizedManualResult = metrics;
    }

    private void ClearManualResult(BenchmarkTarget target)
    {
        if (target == BenchmarkTarget.CollectionView)
            _collectionManualResult = null;
        else
            _virtualizedManualResult = null;
    }

    private void UpdateResultLabels()
    {
        CollectionMetricsLabel.Text = FormatPanel(_collectionJumpResult, _collectionManualResult);
        VirtualizedMetricsLabel.Text = FormatPanel(_virtualizedJumpResult, _virtualizedManualResult);
    }

    private void UpdateWinner()
    {
        if (_lastScenario == BenchmarkScenario.Manual)
        {
            UpdateManualWinner();
            return;
        }

        UpdateJumpWinner();
    }

    private void UpdateJumpWinner()
    {
        if (_collectionJumpResult is null || _virtualizedJumpResult is null)
        {
            WinnerLabel.Text = "Jump: run both lists to compare";
            return;
        }

        var collectionScore = _collectionJumpResult.TotalMilliseconds;
        var virtualizedScore = _virtualizedJumpResult.TotalMilliseconds;

        if (collectionScore <= 0 || virtualizedScore <= 0)
        {
            WinnerLabel.Text = "Jump: completed";
            return;
        }

        var faster = virtualizedScore <= collectionScore ? _virtualizedJumpResult : _collectionJumpResult;
        var slower = virtualizedScore <= collectionScore ? _collectionJumpResult : _virtualizedJumpResult;
        var percent = ((slower.TotalMilliseconds / faster.TotalMilliseconds) - 1) * 100;

        WinnerLabel.Text = $"Jump: {ShortName(faster.Target)} faster by {percent:F0}%";
    }

    private void UpdateManualWinner()
    {
        if (_collectionManualResult is null || _virtualizedManualResult is null)
        {
            WinnerLabel.Text = "Manual: run CV 10s and VCV 10s";
            return;
        }

        var collectionFps = _collectionManualResult.AverageFps;
        var virtualizedFps = _virtualizedManualResult.AverageFps;

        if (collectionFps <= 0 && virtualizedFps <= 0)
        {
            WinnerLabel.Text = "Manual: no scroll events captured";
            return;
        }

        if (Math.Abs(collectionFps - virtualizedFps) < 1)
        {
            var smoother = _virtualizedManualResult.SlowFrames <= _collectionManualResult.SlowFrames
                ? _virtualizedManualResult
                : _collectionManualResult;
            WinnerLabel.Text = $"Manual: FPS similar; {ShortName(smoother.Target)} fewer slow frames";
            return;
        }

        var faster = virtualizedFps >= collectionFps ? _virtualizedManualResult : _collectionManualResult;
        var slower = virtualizedFps >= collectionFps ? _collectionManualResult : _virtualizedManualResult;
        var percent = ((faster.AverageFps / Math.Max(1, slower.AverageFps)) - 1) * 100;

        WinnerLabel.Text = $"Manual: {ShortName(faster.Target)} higher FPS by {percent:F0}%";
    }

    private static string ShortName(BenchmarkTarget target) =>
        target == BenchmarkTarget.CollectionView ? "CV" : "VCV";

    private static string FormatPanel(BenchmarkMetrics? jump, BenchmarkMetrics? manual)
    {
        var jumpText = jump is null ? "Jump: awaiting" : FormatJump(jump);
        var manualText = manual is null ? "Manual 10s: awaiting" : FormatManual(manual);

        return $"{jumpText}\n\n{manualText}";
    }

    private static string FormatJump(BenchmarkMetrics metrics) =>
        $"Jump\n" +
        $"Load: {metrics.BindMilliseconds:F0} ms\n" +
        $"ScrollTo: {metrics.ScrollMilliseconds:F0} ms\n" +
        $"Events: {metrics.ScrollEvents:N0} ({metrics.ScrollPeakPerSecond:F0}/s peak)\n" +
        $"UI: {metrics.AverageFps:F0} fps avg, {metrics.MinimumFps:F0} min\n" +
        $"Slow: {metrics.SlowFrames:N0} | Mem: {metrics.MemoryDeltaMegabytes:+0.0;-0.0;0.0} MB";

    private static string FormatManual(BenchmarkMetrics metrics) =>
        $"Manual 10s\n" +
        $"Load: {metrics.BindMilliseconds:F0} ms\n" +
        $"Events: {metrics.ScrollEvents:N0} ({metrics.ScrollPeakPerSecond:F0}/s peak)\n" +
        $"UI: {metrics.AverageFps:F0} fps avg, {metrics.MinimumFps:F0} min\n" +
        $"Slow: {metrics.SlowFrames:N0} | Mem: {metrics.MemoryDeltaMegabytes:+0.0;-0.0;0.0} MB";

    private static async Task WaitForUiAsync(int milliseconds)
    {
        if (milliseconds <= 0)
        {
            await Task.Yield();
            return;
        }

        await Task.Delay(milliseconds);
    }

    private static async Task CooldownAsync()
    {
        await WaitForUiAsync(250);
        CollectGarbage();
        await WaitForUiAsync(120);
    }

    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private enum BenchmarkTarget
    {
        CollectionView,
        VirtualizedCollectionView
    }

    private enum BenchmarkScenario
    {
        Jump,
        Manual
    }

    private sealed class BenchmarkMetrics
    {
        private const int ScrollWindow = 40;

        private readonly Queue<long> _scrollTicks = new();
        private double _fpsTotal;
        private int _fpsSamples;
        private long _lastUiTick;

        public BenchmarkMetrics(BenchmarkTarget target, BenchmarkScenario scenario)
        {
            Target = target;
            Scenario = scenario;
            Name = target == BenchmarkTarget.CollectionView
                ? "CollectionView"
                : "VirtualizedCollectionView";
        }

        public string Name { get; }
        public BenchmarkTarget Target { get; }
        public BenchmarkScenario Scenario { get; }
        public int Items { get; private set; }
        public double BindMilliseconds { get; set; }
        public double ScrollMilliseconds { get; set; }
        public double TotalMilliseconds => BindMilliseconds + ScrollMilliseconds;
        public int ScrollEvents { get; private set; }
        public double ScrollPeakPerSecond { get; private set; }
        public double AverageFps => _fpsSamples == 0 ? 0 : _fpsTotal / _fpsSamples;
        public double MinimumFps { get; private set; } = 60;
        public int SlowFrames { get; private set; }
        public long StartMemoryBytes { get; set; }
        public long EndMemoryBytes { get; set; }
        public double MemoryDeltaMegabytes => (EndMemoryBytes - StartMemoryBytes) / (1024.0 * 1024.0);

        public void ResetFrameSampling()
        {
            _fpsTotal = 0;
            _fpsSamples = 0;
            _lastUiTick = 0;
            MinimumFps = 60;
            SlowFrames = 0;
        }

        public void UiTick()
        {
            var now = Environment.TickCount64;
            if (_lastUiTick == 0)
            {
                _lastUiTick = now;
                return;
            }

            var delta = now - _lastUiTick;
            _lastUiTick = now;

            if (delta <= 0)
                return;

            var fps = Math.Min(60, 1000.0 / delta);
            _fpsTotal += fps;
            _fpsSamples++;

            if (fps < MinimumFps)
                MinimumFps = fps;

            if (delta > 25)
                SlowFrames++;
        }

        public void ScrollTick()
        {
            var now = Environment.TickCount64;
            ScrollEvents++;

            _scrollTicks.Enqueue(now);
            while (_scrollTicks.Count > ScrollWindow)
                _scrollTicks.Dequeue();

            if (_scrollTicks.Count < 2)
                return;

            var elapsed = now - _scrollTicks.Peek();
            if (elapsed <= 0)
                return;

            var perSecond = (_scrollTicks.Count - 1) * 1000.0 / elapsed;
            if (perSecond > ScrollPeakPerSecond)
                ScrollPeakPerSecond = perSecond;
        }

        public void Finish(int items)
        {
            Items = items;
            if (_fpsSamples == 0)
                MinimumFps = 0;
        }
    }
}

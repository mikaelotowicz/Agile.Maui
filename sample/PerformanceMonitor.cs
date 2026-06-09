using System.Diagnostics;

namespace sample;

public sealed class PerformanceMonitor
{
    private const int ScrollFpsWindow = 60;
    private const int ScrollExpiryMs = 700;
    private const int UiFpsWindow = 60;

    private readonly Queue<long> _scrollTicksMs = new();
    private readonly Queue<long> _uiTicksMs = new();
    private readonly Stopwatch _loadSw = new();
    private long _lastScrollMs;
    private long _lastUiTickMs;

    public TimeSpan LoadTime { get; private set; }
    public double ScrollFps { get; private set; }
    public double ScrollPeakFps { get; private set; }
    public double UiFps { get; private set; }
    public double UiMinFps { get; private set; } = double.MaxValue;
    public int TotalScrollEvents { get; private set; }
    public long MemoryBytes => GC.GetTotalMemory(forceFullCollection: false);

    public void StartLoad() => _loadSw.Restart();

    public void EndLoad()
    {
        _loadSw.Stop();
        LoadTime = _loadSw.Elapsed;
    }

    public void ScrollTick()
    {
        var nowMs = Environment.TickCount64;
        TotalScrollEvents++;
        _lastScrollMs = nowMs;

        _scrollTicksMs.Enqueue(nowMs);
        while (_scrollTicksMs.Count > ScrollFpsWindow)
            _scrollTicksMs.Dequeue();

        if (_scrollTicksMs.Count >= 2)
        {
            var deltaMs = nowMs - _scrollTicksMs.Peek();
            if (deltaMs > 0)
            {
                ScrollFps = (_scrollTicksMs.Count - 1) * 1000.0 / deltaMs;
                if (ScrollFps > ScrollPeakFps) ScrollPeakFps = ScrollFps;
            }
        }
    }

    public void UiTick()
    {
        var nowMs = Environment.TickCount64;
        if (_lastUiTickMs == 0)
        {
            _lastUiTickMs = nowMs;
            return;
        }

        _uiTicksMs.Enqueue(nowMs);
        while (_uiTicksMs.Count > UiFpsWindow)
            _uiTicksMs.Dequeue();

        if (_uiTicksMs.Count >= 2)
        {
            var deltaMs = nowMs - _uiTicksMs.Peek();
            if (deltaMs > 0)
            {
                UiFps = (_uiTicksMs.Count - 1) * 1000.0 / deltaMs;
                if (UiFps > 60) UiFps = 60;
                if (UiFps < UiMinFps) UiMinFps = UiFps;
            }
        }

        _lastUiTickMs = nowMs;
    }

    public void Decay()
    {
        if (_scrollTicksMs.Count == 0) return;
        var idleMs = Environment.TickCount64 - _lastScrollMs;
        if (idleMs > ScrollExpiryMs)
        {
            ScrollFps = 0;
            _scrollTicksMs.Clear();
        }
    }

    public void Reset()
    {
        _scrollTicksMs.Clear();
        _uiTicksMs.Clear();
        _lastScrollMs = 0;
        _lastUiTickMs = 0;
        ScrollFps = 0;
        ScrollPeakFps = 0;
        UiFps = 0;
        UiMinFps = double.MaxValue;
        TotalScrollEvents = 0;
        LoadTime = TimeSpan.Zero;
    }

    public string FormatReport()
    {
        var uiMin = UiMinFps == double.MaxValue ? 0 : UiMinFps;
        return $"Load: {LoadTime.TotalMilliseconds:F0}ms · UI: {UiFps:F0}fps (min {uiMin:F0}) · " +
               $"Scroll: {ScrollFps:F0}/s (events {TotalScrollEvents}) · Mem: {MemoryBytes / (1024.0 * 1024):F1}MB";
    }
}

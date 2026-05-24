// sample/PerformanceMonitor.cs
using System.Diagnostics;

namespace sample;

/// <summary>
/// Coleta métricas para comparar componentes de lista:
/// <list type="bullet">
///   <item><b>LoadTime</b> — tempo do construtor até o primeiro Appearing</item>
///   <item><b>ScrollFps</b> — eventos Scrolled por segundo (taxa em que o componente
///         reporta deslocamento; depende de como o componente despacha)</item>
///   <item><b>UiFps</b> — frames que o UI thread consegue completar a 60fps alvo
///         (timer agendado a cada ~16ms; quanto mais perto de 60, mais responsivo
///         o UI thread está durante scroll/idle)</item>
///   <item><b>Memory</b> — bytes managed</item>
/// </list>
/// </summary>
public sealed class PerformanceMonitor
{
    private const int  ScrollFpsWindow = 60;
    private const int  ScrollExpiryMs  = 700;
    private const int  UiFpsWindow     = 60;
    private const long TargetFrameMs   = 16;       // ~60fps

    private readonly Queue<long> _scrollTicksMs = new();
    private readonly Queue<long> _uiTicksMs     = new();
    private readonly Stopwatch   _loadSw        = new();
    private          long        _lastScrollMs;
    private          long        _lastUiTickMs;

    public TimeSpan LoadTime           { get; private set; }
    public double   ScrollFps          { get; private set; }
    public double   ScrollPeakFps      { get; private set; }
    public double   UiFps              { get; private set; }
    public double   UiMinFps           { get; private set; } = double.MaxValue;
    public int      TotalScrollEvents  { get; private set; }
    public long     MemoryBytes        => GC.GetTotalMemory(forceFullCollection: false);

    public void StartLoad() => _loadSw.Restart();

    public void EndLoad()
    {
        _loadSw.Stop();
        LoadTime = _loadSw.Elapsed;
    }

    /// <summary>Registra evento de scroll do componente.</summary>
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

    /// <summary>
    /// Chame a cada tick do dispatcher (16ms target). Mede quão pontuais os ticks
    /// estão chegando — se o UI thread atrasa, intervalos crescem e UiFps cai.
    /// </summary>
    public void UiTick()
    {
        var nowMs = Environment.TickCount64;
        if (_lastUiTickMs == 0) { _lastUiTickMs = nowMs; return; }

        _uiTicksMs.Enqueue(nowMs);
        while (_uiTicksMs.Count > UiFpsWindow)
            _uiTicksMs.Dequeue();

        if (_uiTicksMs.Count >= 2)
        {
            var deltaMs = nowMs - _uiTicksMs.Peek();
            if (deltaMs > 0)
            {
                UiFps = (_uiTicksMs.Count - 1) * 1000.0 / deltaMs;
                // Cap em 60fps (não conta "extra" se o timer dispara mais frequente que isso)
                if (UiFps > 60) UiFps = 60;
                if (UiFps < UiMinFps) UiMinFps = UiFps;
            }
        }
        _lastUiTickMs = nowMs;
    }

    /// <summary>Decai ScrollFps quando o scroll para.</summary>
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
               $"Scroll: {ScrollFps:F0}/s (eventos {TotalScrollEvents}) · Mem: {MemoryBytes / (1024.0 * 1024):F1}MB";
    }
}

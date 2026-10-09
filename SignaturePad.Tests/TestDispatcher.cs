using System.Runtime.CompilerServices;
using Microsoft.Maui.Dispatching;

namespace Agile.Maui.SignaturePadTests;

internal static class TestDispatcherInstaller
{
    [ModuleInitializer]
    internal static void Install() => DispatcherProvider.SetCurrent(new TestDispatcherProvider());
}

internal sealed class TestDispatcherProvider : IDispatcherProvider
{
    [ThreadStatic] private static TestDispatcher? _dispatcher;
    public IDispatcher? GetForCurrentThread() => _dispatcher ??= new TestDispatcher();

    /// <summary>Dispatcher da thread corrente (a mesma instância que os controles criados nela usam).</summary>
    public static TestDispatcher ForCurrentThread()
        => (TestDispatcher)DispatcherProvider.Current.GetForCurrentThread()!;
}

/// <summary>
/// Dispatcher de teste. Por padrão executa as actions inline; com <see cref="EnqueueMode"/>
/// ligado, enfileira e só executa em <see cref="ProcessQueue"/> — útil para testar coalescing.
/// </summary>
internal sealed class TestDispatcher : IDispatcher
{
    private readonly Queue<Action> _queue = new();

    public bool EnqueueMode { get; set; }
    public int PendingCount => _queue.Count;

    public bool IsDispatchRequired => false;

    public bool Dispatch(Action action)
    {
        if (EnqueueMode)
        {
            _queue.Enqueue(action);
            return true;
        }

        action();
        return true;
    }

    public bool DispatchDelayed(TimeSpan delay, Action action) => Dispatch(action);

    public IDispatcherTimer CreateTimer() => new TestDispatcherTimer();

    /// <summary>Drena a fila; retorna quantas actions executou.</summary>
    public int ProcessQueue()
    {
        var executed = 0;
        while (_queue.Count > 0)
        {
            _queue.Dequeue()();
            executed++;
        }

        return executed;
    }
}

internal sealed class TestDispatcherTimer : IDispatcherTimer
{
    public TimeSpan Interval { get; set; }
    public bool IsRepeating { get; set; } = true;
    public bool IsRunning { get; private set; }
    public event EventHandler? Tick;

    public void Start() => IsRunning = true;
    public void Stop() => IsRunning = false;

    /// <summary>Dispara o Tick manualmente (controle total do tempo no teste).</summary>
    public void FireTick() => Tick?.Invoke(this, EventArgs.Empty);
}

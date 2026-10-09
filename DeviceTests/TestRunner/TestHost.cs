namespace Agile.Maui.DeviceTests;

/// <summary>
/// Serviços de UI para os testes: monta uma View numa página real (handler conectado e
/// layout concluído, com tamanhos reais), desmonta (DisconnectHandler) e espera condições
/// sem bloquear a UI thread (polling com <see cref="Task.Delay(int)"/>).
/// </summary>
public sealed class TestHost
{
    private readonly ContentView _hostArea;

    internal TestHost(ContentPage page, ContentView hostArea)
    {
        Page = page;
        _hostArea = hostArea;
    }

    /// <summary>Página real em que os testes montam suas views.</summary>
    public ContentPage Page { get; }

    /// <summary>
    /// Monta a view na área de host e espera handler conectado + layout com tamanho real.
    /// </summary>
    public async Task MountAsync(View view, int timeoutMs = 10_000)
    {
        _hostArea.Content = view;
        await WaitForAsync(
            () => view.Handler is not null && view.Width > 0 && view.Height > 0,
            timeoutMs,
            $"layout de {view.GetType().Name} (handler conectado e tamanho > 0)");
        // Um ciclo extra de dispatcher para o 1º draw/arranjo nativo assentar.
        await PumpAsync();
    }

    /// <summary>
    /// Remove o conteúdo montado e desconecta os handlers explicitamente —
    /// exercita o caminho de teardown (DisconnectHandler) de forma determinística.
    /// </summary>
    public async Task UnmountAsync()
    {
        var view = _hostArea.Content;
        _hostArea.Content = null;
        await PumpAsync();
        view?.DisconnectHandlers();
        await PumpAsync();
    }

    /// <summary>Espera assíncrona (polling de 50 ms) sem bloquear a UI thread.</summary>
    public async Task WaitForAsync(Func<bool> condition, int timeoutMs, string description)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException($"Timeout ({timeoutMs} ms) esperando: {description}");
            await Task.Delay(50);
        }
    }

    /// <summary>Espera uma Task sinalizar, com timeout e mensagem útil.</summary>
    public async Task WaitForAsync(Task task, int timeoutMs, string description)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
        if (completed != task)
            throw new TimeoutException($"Timeout ({timeoutMs} ms) esperando: {description}");
        await task; // propaga exceção, se houver
    }

    /// <summary>Cede alguns ciclos à UI thread (layout/render nativos).</summary>
    public Task PumpAsync(int ms = 100) => Task.Delay(ms);

    /// <summary>Limpa a área de host entre testes (chamado pelo runner).</summary>
    internal async Task ResetAsync()
    {
        if (_hostArea.Content is not null)
        {
            var view = _hostArea.Content;
            _hostArea.Content = null;
            await PumpAsync(50);
            try { view.DisconnectHandlers(); }
            catch { /* teardown best-effort entre testes */ }
        }
    }
}

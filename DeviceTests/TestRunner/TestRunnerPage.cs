namespace Agile.Maui.DeviceTests;

/// <summary>
/// Página única do app: roda a suíte inteira no startup. Durante a execução, a área
/// inferior hospeda a view sob teste (tamanhos reais); ao final, mostra a lista de
/// resultados. No Windows, o app se encerra sozinho após gravar o arquivo de resultado
/// (defina AGILE_DEVICETESTS_KEEP_OPEN=1 para inspecionar a janela).
/// </summary>
public sealed class TestRunnerPage : ContentPage
{
    private readonly Label _status;
    private readonly ContentView _hostArea;
    private bool _started;

    public TestRunnerPage()
    {
        Title = "Agile.Maui DeviceTests";

        _status = new Label
        {
            Text = "Iniciando…",
            FontSize = 14,
            Margin = new Thickness(12, 8),
            LineBreakMode = LineBreakMode.TailTruncation,
        };

        _hostArea = new ContentView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
        };

        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        grid.Add(_status, 0, 0);
        grid.Add(_hostArea, 0, 1);

        Content = grid;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        if (_started)
            return;
        _started = true;

        var host = new TestHost(this, _hostArea);
        IReadOnlyList<DeviceTestResult> results;
        try
        {
            results = await DeviceTestRunner.RunAllAsync(host, status => _status.Text = status);
        }
        catch (Exception ex)
        {
            _status.Text = $"Falha catastrófica do runner: {ex.Message}";
            Console.WriteLine($"[DEVICETEST] FAIL runner | {ex}");
            Console.WriteLine("[DEVICETEST-SUMMARY] total=0 pass=0 fail=1");
            return;
        }

        var pass = results.Count(r => r.Passed);
        var fail = results.Count - pass;
        _status.Text = $"Concluído: {results.Count} testes — {pass} PASS, {fail} FAIL";
        _hostArea.Content = BuildResultsView(results);

#if WINDOWS
        if (Environment.GetEnvironmentVariable("AGILE_DEVICETESTS_KEEP_OPEN") != "1")
        {
            await Task.Delay(1500); // tempo para o flush do arquivo e um vislumbre da UI
            Environment.Exit(fail == 0 ? 0 : 1);
        }
#endif
    }

    private static View BuildResultsView(IReadOnlyList<DeviceTestResult> results)
    {
        var stack = new VerticalStackLayout { Spacing = 4, Padding = new Thickness(12) };
        foreach (var r in results)
        {
            stack.Add(new Label
            {
                Text = $"{(r.Passed ? "✔" : "✘")} {r.Name} — {r.Message}",
                TextColor = r.Passed ? Colors.Green : Colors.Red,
                FontSize = 13,
            });
        }
        return new ScrollView { Content = stack };
    }
}

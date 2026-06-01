using Agile.Maui;
using System.ComponentModel;
using System.Text;

namespace sample;

[QueryProperty(nameof(FilePath), "path")]
public partial class PdfViewerPage : ContentPage
{
    private string?        _filePath;
    private Timer?         _statsTimer;
    private volatile bool  _isAlive;   // página viva entre OnAppearing/OnDisappearing
    private readonly StringBuilder _logBuffer = new();

    public PdfViewerPage()
    {
        InitializeComponent();
        ShowEmptyState();
    }

    // Recebe caminho via QueryProperty quando navegado via Shell
    public string? FilePath
    {
        set
        {
            if (string.IsNullOrEmpty(value)) return;
            var path = Uri.UnescapeDataString(value);
            FileNameLabel.Text = Path.GetFileName(path);
            LoadPdf(path);
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _isAlive = true;
        ApplyIcons();
        if (Application.Current is not null)
            Application.Current.RequestedThemeChanged += OnThemeChanged;
        Viewer.PropertyChanged += OnViewerPropertyChanged;

        PdfViewerLog.Received += OnLogReceived;

        _statsTimer = new Timer(_ => UpdateCacheStats(), null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        PdfViewerLog.Write("Pdf/Sample", "OnAppearing");
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isAlive = false;
        PdfViewerLog.Write("Pdf/Sample", "OnDisappearing");

        if (Application.Current is not null)
            Application.Current.RequestedThemeChanged -= OnThemeChanged;
        Viewer.PropertyChanged -= OnViewerPropertyChanged;
        PdfViewerLog.Received  -= OnLogReceived;

        _statsTimer?.Dispose();
        _statsTimer = null;
    }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) => ApplyIcons();

    private void ApplyIcons()
    {
        bool dark  = Application.Current?.RequestedTheme == AppTheme.Dark;
        string suf = dark ? "_dark" : "";
        BackButton.Source = ImageSource.FromFile($"ic_back{suf}.svg");
    }

    // ── Carregar PDF ──────────────────────────────────────────────────────────

    private void LoadPdf(string pathOrUrl)
    {
        ShowLoading("Carregando PDF…");
        PdfViewerLog.Write("Pdf/Sample", $"LoadPdf: '{pathOrUrl[..Math.Min(80, pathOrUrl.Length)]}'");

        Viewer.Source = pathOrUrl;

        string name = pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(pathOrUrl.Split('?')[0])
            : Path.GetFileName(pathOrUrl);

        FileNameLabel.Text = string.IsNullOrEmpty(name) ? "PDF" : name;
        StatsLabel.Text    = string.Empty;
        _filePath          = pathOrUrl;

        UpdatePageControls(0, 0);
        UpdateZoomLabel(1.0);
    }

    // ── Chips de URL rápida ───────────────────────────────────────────────────

    private void OnLoadSampleSmall(object? sender, EventArgs e)
        => LoadPdf("https://www.africau.edu/images/default/sample.pdf");

    private void OnLoadSampleMedium(object? sender, EventArgs e)
        => LoadPdf("https://www.ietf.org/rfc/rfc2616.txt.pdf");

    private void OnLoadSampleLarge(object? sender, EventArgs e)
        => LoadPdf("https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/PDF32000_2008.pdf");

    // ── Picker de arquivo ─────────────────────────────────────────────────────

    private async void OnPickFileClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Selecione um PDF",
                FileTypes   = new FilePickerFileType(
                    new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                        { DevicePlatform.WinUI,       new[] { ".pdf" } },
                        { DevicePlatform.iOS,          new[] { "com.adobe.pdf" } },
                        { DevicePlatform.Android,      new[] { "application/pdf" } },
                        { DevicePlatform.MacCatalyst,  new[] { "com.adobe.pdf" } },
                    }),
            });

            if (result is null) return;

            string path = result.FullPath;

#if ANDROID
            if (path.StartsWith("content://", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path))
            {
                PdfViewerLog.Write("Pdf/Sample", $"Android: copiando content:// → cache  {result.FileName}");
                var dest = Path.Combine(FileSystem.CacheDirectory, result.FileName);
                await using var src  = await result.OpenReadAsync();
                await using var file = File.Create(dest);
                await src.CopyToAsync(file);
                path = dest;
            }
#endif
            LoadPdf(path);
        }
        catch (Exception ex)
        {
            PdfViewerLog.Write("Pdf/Sample", $"OnPickFileClicked ERRO: {ex.Message}");
            await DisplayAlert("Erro", ex.Message, "OK");
        }
    }

    // ── Eventos do PdfViewer ─────────────────────────────────────────────

    private void OnDocumentLoaded(object? sender, PdfDocumentLoadedEventArgs e)
    {
        PdfViewerLog.Write("Pdf/Sample", $"DocumentLoaded  pages={e.PageCount}");
        LoadingOverlay.IsVisible = false;
        UpdatePageControls(0, e.PageCount);
        UpdateZoomLabel(Viewer.ZoomFactor);
        StatsLabel.Text = $"{e.PageCount} páginas  ·  cache ≤{Viewer.MaxCacheMB} MB";
    }

    private async void OnDocumentLoadFailed(object? sender, PdfDocumentLoadFailedEventArgs e)
    {
        PdfViewerLog.Write("Pdf/Sample", $"DocumentLoadFailed: {e.Message}");
        LoadingOverlay.IsVisible = false;
        ShowEmptyState("Erro ao carregar.");
        await DisplayAlert("Erro ao abrir PDF", e.Message, "OK");
    }

    private void OnPageChanged(object? sender, PdfPageChangedEventArgs e)
    {
        UpdatePageControls(e.Page, Viewer.PageCount);
    }

    private void OnViewerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PdfViewer.ZoomFactor))
            UpdateZoomLabel(Viewer.ZoomFactor);
    }

    // ── Navegação ─────────────────────────────────────────────────────────────

    private void OnPrevClicked(object? sender, EventArgs e)
    {
        if (Viewer.CurrentPage <= 0) return;
        Viewer.CurrentPage--;
    }

    private void OnNextClicked(object? sender, EventArgs e)
    {
        if (Viewer.CurrentPage >= Viewer.PageCount - 1) return;
        Viewer.CurrentPage++;
    }

    // ── Log ───────────────────────────────────────────────────────────────────

    private void OnLogClicked(object? sender, EventArgs e)
    {
        LogPanel.IsVisible = !LogPanel.IsVisible;
        // Esconde configurações ao abrir log e vice-versa
        if (LogPanel.IsVisible) SettingsPanel.IsVisible = false;
    }

    private void OnClearLogClicked(object? sender, EventArgs e)
    {
        _logBuffer.Clear();
        LogLabel.Text = string.Empty;
    }

    private async void OnCopyLogClicked(object? sender, EventArgs e)
    {
        try
        {
            await Clipboard.SetTextAsync(_logBuffer.ToString());
            await DisplayAlert(null, "Log copiado para a área de transferência.", "OK");
        }
        catch { /* Clipboard pode falhar em algumas plataformas */ }
    }

    private void OnLogReceived(string line)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _logBuffer.AppendLine(line);

            // Mantém as últimas 120 linhas
            var text = _logBuffer.ToString();
            int nl   = text.AsSpan().Count('\n');
            if (nl > 120)
            {
                int cut = 0;
                for (int i = 0; i < nl - 120; i++)
                    cut = text.IndexOf('\n', cut) + 1;
                _logBuffer.Clear().Append(text.AsSpan(cut));
            }

            LogLabel.Text = _logBuffer.ToString();

            if (LogPanel.IsVisible)
                _ = LogScrollView.ScrollToAsync(0, double.MaxValue, animated: false);
        });
    }

    // ── Painel de configurações ────────────────────────────────────────────────

    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        SettingsPanel.IsVisible = !SettingsPanel.IsVisible;
        if (SettingsPanel.IsVisible) LogPanel.IsVisible = false;
    }

    private void OnCacheMBChanged(object? sender, ValueChangedEventArgs e)
    {
        int mb = (int)Math.Round(e.NewValue / 10.0) * 10;
        Viewer.MaxCacheMB  = mb;
        CacheMBLabel.Text  = $"{mb} MB";
    }

    private void OnRenderScaleChanged(object? sender, ValueChangedEventArgs e)
    {
        double scale = Math.Round(e.NewValue * 4) / 4;
        Viewer.RenderScale    = scale;
        RenderScaleLabel.Text = $"{scale:F2}×";
    }

    // ── Estatísticas (timer) ──────────────────────────────────────────────────

    private void UpdateCacheStats()
    {
        // O callback do Timer roda em thread-pool e pode disparar depois do Dispose
        // (OnDisappearing não espera o callback em voo). Curto-circuita se a página morreu.
        if (!_isAlive) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Re-checa na UI thread: a página pode ter saído da tela enquanto o invoke
            // estava enfileirado. Evita NRE ao tocar controles já liberados.
            if (!_isAlive || Viewer is null || !SettingsPanel.IsVisible || Viewer.PageCount == 0)
                return;

            PrefetchLabel.Text = $"{Viewer.PrefetchAbove} / {Viewer.PrefetchBelow}";

            CacheStatsLabel.Text =
                $"Páginas: {Viewer.PageCount}   " +
                $"Zoom: {Viewer.ZoomFactor:F2}×   " +
                $"Scale: {Viewer.RenderScale:F1}×\n" +
                $"Cache max: {Viewer.MaxCacheMB} MB   " +
                $"Prefetch: ▲{Viewer.PrefetchAbove} ▼{Viewer.PrefetchBelow}";
        });
    }

    // ── Top bar ───────────────────────────────────────────────────────────────

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        // async void: envolve em try/catch para não vazar exceção não observada
        // (GoToAsync pode falhar se a navegação Shell estiver em estado inválido).
        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            PdfViewerLog.Write("Pdf/Sample", $"OnBackClicked ERRO: {ex.Message}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ShowLoading(string message)
    {
        LoadingLabel.Text        = message;
        LoadingSpinner.IsVisible = true;
        LoadingSpinner.IsRunning = true;
        LoadingOverlay.IsVisible = true;
    }

    private void ShowEmptyState(string message = "Selecione um PDF acima")
    {
        LoadingLabel.Text        = message;
        LoadingSpinner.IsVisible = false;
        LoadingSpinner.IsRunning = false;
        LoadingOverlay.IsVisible = true;
    }

    private void UpdatePageControls(int current, int total)
    {
        if (total <= 0)
        {
            PageLabel.Text       = "—";
            PrevButton.IsEnabled = false;
            NextButton.IsEnabled = false;
        }
        else
        {
            PageLabel.Text       = $"{current + 1} / {total}";
            PrevButton.IsEnabled = current > 0;
            NextButton.IsEnabled = current < total - 1;
        }
    }

    private void UpdateZoomLabel(double zoom)
        => ZoomLabel.Text = $"{zoom * 100:F0}%";
}

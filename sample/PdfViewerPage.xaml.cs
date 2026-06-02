using Agile.Maui;
using System.ComponentModel;

namespace sample;

/// <summary>
/// Página de demonstração do <see cref="PdfViewer"/>: navbar personalizada (menu lateral,
/// título, imprimir/compartilhar/⋮), visualizador com zoom e barra inferior de navegação.
/// Carrega automaticamente um PDF de exemplo empacotado na primeira aparição.
/// </summary>
[QueryProperty(nameof(FilePath), "path")]
public partial class PdfViewerPage : ContentPage
{
    private const string BundledSample        = "NT_2016_002.pdf";
    private const string BundledSampleDisplay = "NT_2016_002_v1 61.pdf";

    private string? _filePath;
    private bool    _autoLoadTried;

    public PdfViewerPage()
    {
        InitializeComponent();
        ShowLoading("Carregando…");   // o PDF de exemplo é auto-carregado em OnAppearing
    }

    /// <summary>Caminho ou URL recebido por navegação Shell (<c>?path=...</c>).</summary>
    public string? FilePath
    {
        set
        {
            if (!string.IsNullOrEmpty(value))
                LoadPdf(Uri.UnescapeDataString(value));
        }
    }

    // ── Ciclo de vida ───────────────────────────────────────────────────────────

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Viewer.PropertyChanged += OnViewerPropertyChanged;
        TryAutoLoadBundled();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Viewer.PropertyChanged -= OnViewerPropertyChanged;
    }

    // Copia o PDF de exemplo empacotado para o cache e o abre. Só na primeira aparição e
    // apenas se nenhum documento já tiver sido aberto (ex.: via navegação com ?path=).
    private async void TryAutoLoadBundled()
    {
        if (_autoLoadTried || _filePath is not null) return;
        _autoLoadTried = true;

        try
        {
            var dest = Path.Combine(FileSystem.CacheDirectory, BundledSample);
            using (var src = await FileSystem.OpenAppPackageFileAsync(BundledSample))
            using (var fs  = File.Create(dest))
                await src.CopyToAsync(fs);

            LoadPdf(dest, BundledSampleDisplay);
        }
        catch
        {
            ShowEmptyState("Use o menu ⋮ para abrir um PDF.");
        }
    }

    // ── Carregar PDF ──────────────────────────────────────────────────────────────

    private void LoadPdf(string pathOrUrl, string? displayName = null)
    {
        ShowLoading("Carregando PDF…");

        Viewer.Source = pathOrUrl;
        _filePath     = pathOrUrl;

        FileNameLabel.Text = displayName ?? ResolveFileName(pathOrUrl);
        StatsLabel.Text    = string.Empty;

        UpdatePageControls(0, 0);
        UpdateZoomLabel(1.0);
    }

    private static string ResolveFileName(string pathOrUrl)
    {
        bool   isUrl = pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        string name  = Path.GetFileName(isUrl ? pathOrUrl.Split('?')[0] : pathOrUrl);
        return string.IsNullOrEmpty(name) ? "PDF" : name;
    }

    // ── Eventos do PdfViewer ────────────────────────────────────────────────────

    private void OnDocumentLoaded(object? sender, PdfDocumentLoadedEventArgs e)
    {
        LoadingOverlay.IsVisible = false;
        UpdatePageControls(0, e.PageCount);
        UpdateZoomLabel(Viewer.ZoomFactor);
        StatsLabel.Text = $"{e.PageCount} páginas";
    }

    private async void OnDocumentLoadFailed(object? sender, PdfDocumentLoadFailedEventArgs e)
    {
        LoadingOverlay.IsVisible = false;
        ShowEmptyState("Erro ao carregar.");
        await DisplayAlert("Erro ao abrir PDF", e.Message, "OK");
    }

    private void OnPageChanged(object? sender, PdfPageChangedEventArgs e)
        => UpdatePageControls(e.Page, Viewer.PageCount);

    private void OnViewerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PdfViewer.ZoomFactor))
            UpdateZoomLabel(Viewer.ZoomFactor);
    }

    // ── NavBar: menu lateral (flyout) e overflow (⋮) ──────────────────────────────

    private void OnFlyoutClicked(object? sender, EventArgs e)
    {
        if (Shell.Current is not null)
            Shell.Current.FlyoutIsPresented = true;
    }

    // Reúne as ações "secundárias" num action sheet. A barra de miniaturas só existe no desktop.
    private async void OnMenuClicked(object? sender, EventArgs e)
    {
        const string openLabel = "Abrir PDF";
        bool   isDesktop  = DeviceInfo.Current.Idiom == DeviceIdiom.Desktop;
        string thumbLabel = Viewer.EnableThumbnailBar ? "Ocultar miniaturas" : "Mostrar miniaturas";

        string[] options = isDesktop ? [openLabel, thumbLabel] : [openLabel];
        string   choice  = await DisplayActionSheet("Opções", "Cancelar", null, options);

        if (choice == openLabel)        await PickFileAsync();
        else if (choice == thumbLabel)  ToggleThumbnails();
    }

    // ── Abrir arquivo ─────────────────────────────────────────────────────────────

    private async Task PickFileAsync()
    {
        try
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Selecione um PDF",
                FileTypes   = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI,       new[] { ".pdf" } },
                    { DevicePlatform.iOS,         new[] { "com.adobe.pdf" } },
                    { DevicePlatform.Android,     new[] { "application/pdf" } },
                    { DevicePlatform.MacCatalyst, new[] { "com.adobe.pdf" } },
                }),
            });

            if (result is null) return;

            string path = result.FullPath;

#if ANDROID
            // O Android entrega um content:// sem caminho de arquivo real; copia para o cache.
            if (path.StartsWith("content://", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
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
            await DisplayAlert("Erro", ex.Message, "OK");
        }
    }

    // ── Imprimir / Compartilhar ────────────────────────────────────────────────────

    private async void OnPrintClicked(object? sender, EventArgs e)
        => await DisplayAlert("Imprimir", "A impressão ainda não está disponível neste exemplo.", "OK");

    private async void OnShareClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_filePath))
        {
            await DisplayAlert("Compartilhar", "Nenhum documento aberto para compartilhar.", "OK");
            return;
        }

        try
        {
            // URL remota: compartilha o link. Arquivo local: compartilha o próprio PDF.
            if (_filePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                await Share.RequestAsync(new ShareTextRequest { Uri = _filePath, Title = FileNameLabel.Text });
            else
                await Share.RequestAsync(new ShareFileRequest { Title = FileNameLabel.Text, File = new ShareFile(_filePath) });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro", ex.Message, "OK");
        }
    }

    // ── Miniaturas ─────────────────────────────────────────────────────────────────

    private void OnToggleThumbnailsClicked(object? sender, EventArgs e) => ToggleThumbnails();

    private void ToggleThumbnails() => Viewer.EnableThumbnailBar = !Viewer.EnableThumbnailBar;

    // ── Navegação de páginas ─────────────────────────────────────────────────────

    private void OnPrevClicked(object? sender, EventArgs e)
    {
        if (Viewer.CurrentPage > 0) Viewer.CurrentPage--;
    }

    private void OnNextClicked(object? sender, EventArgs e)
    {
        if (Viewer.CurrentPage < Viewer.PageCount - 1) Viewer.CurrentPage++;
    }

    // ── Zoom (botões − / +) ─────────────────────────────────────────────────────

    // Passo do zoom pelos botões: 25% no mobile, 10% no desktop (mais fino com mouse).
    private double ZoomStep => DeviceInfo.Current.Idiom == DeviceIdiom.Desktop ? 0.10 : 0.25;

    private void OnZoomInClicked(object? sender, EventArgs e)  => StepZoom(+1);
    private void OnZoomOutClicked(object? sender, EventArgs e) => StepZoom(-1);
    private void OnResetZoomClicked(object? sender, EventArgs e) => Viewer.ZoomFactor = 1.0;

    // Aplica um passo de zoom arredondando para o múltiplo do passo (100/125/150…) e limita
    // ao intervalo [MinZoom, MaxZoom].
    private void StepZoom(int direction)
    {
        double step   = ZoomStep;
        double target = Math.Round((Viewer.ZoomFactor + direction * step) / step) * step;
        Viewer.ZoomFactor = Math.Clamp(target, Viewer.MinZoom, Viewer.MaxZoom);
    }

    // ── UI helpers ──────────────────────────────────────────────────────────────

    private void ShowLoading(string message)
    {
        LoadingLabel.Text        = message;
        LoadingSpinner.IsVisible = true;
        LoadingSpinner.IsRunning = true;
        LoadingOverlay.IsVisible = true;
    }

    private void ShowEmptyState(string message)
    {
        LoadingLabel.Text        = message;
        LoadingSpinner.IsVisible = false;
        LoadingSpinner.IsRunning = false;
        LoadingOverlay.IsVisible = true;
    }

    private void UpdatePageControls(int current, int total)
    {
        bool hasDoc = total > 0;
        PageLabel.Text       = hasDoc ? $"{current + 1} / {total}" : "—";
        PrevButton.IsEnabled = hasDoc && current > 0;
        NextButton.IsEnabled = hasDoc && current < total - 1;
    }

    private void UpdateZoomLabel(double zoom)
        => ZoomLabel.Text = $"{zoom * 100:F0}%";
}

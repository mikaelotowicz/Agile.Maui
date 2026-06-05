using Agile.Maui;
using Microsoft.Extensions.DependencyInjection;
using sample.Services;
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
    private const string BundledSample        = "InteligenciaArtificial.pdf";
    private const string BundledSampleDisplay = "InteligenciaArtificial.pdf";

    private string? _filePath;
    private bool    _autoLoadTried;

    // Resolvido sob demanda do contêiner de DI (o Handler já existe quando o menu é acionado).
    private IAnchoredMenu? _menu;
    private IAnchoredMenu  Menu => _menu ??= Handler!.MauiContext!.Services.GetRequiredService<IAnchoredMenu>();

    public PdfViewerPage()
    {
        InitializeComponent();
        ShowLoading("Carregando…");   // o PDF de exemplo é auto-carregado em OnAppearing

        UpdateThumbDrawerButton();
        UpdateOrientationIcon();
    }

    // Botão de miniaturas (drawer) na barra inferior. Uma única propriedade controla tudo:
    // ThumbnailBarPlacement (None = oculto; Left/Right = visível naquele lado). Só no MOBILE.
    private void UpdateThumbDrawerButton()
    {
        bool mobile = DeviceInfo.Current.Idiom != DeviceIdiom.Desktop;
        var  place  = Viewer.ThumbnailBarPlacement;

        ThumbDrawerBtn.IsVisible = mobile && place != PdfThumbnailPlacement.None;

        bool right = place == PdfThumbnailPlacement.Right;
        Grid.SetColumn(ThumbDrawerBtn, right ? 2 : 0);
        ThumbDrawerBtn.HorizontalOptions = right ? LayoutOptions.End : LayoutOptions.Start;
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
        Viewer.PropertyChanged    += OnViewerPropertyChanged;
        Viewer.SearchResultChanged += OnSearchResultChanged;
        TryAutoLoadBundled();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Viewer.PropertyChanged     -= OnViewerPropertyChanged;
        Viewer.SearchResultChanged -= OnSearchResultChanged;
        _searchDebounceCts?.Cancel();
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
        else if (e.PropertyName == nameof(PdfViewer.ThumbnailBarPlacement))
            UpdateThumbDrawerButton();
        else if (e.PropertyName == nameof(PdfViewer.ScrollOrientation))
            UpdateOrientationIcon();
    }

    // ── NavBar: menu lateral (flyout) e overflow (⋮) ──────────────────────────────

    private void OnFlyoutClicked(object? sender, EventArgs e)
    {
        if (Shell.Current is not null)
            Shell.Current.FlyoutIsPresented = true;
    }

    // Botão da navbar: alterna a direção do scroll vertical (contínuo) ⇄ horizontal (paginado).
    private void OnOrientationClicked(object? sender, EventArgs e)
    {
        Viewer.ScrollOrientation = Viewer.ScrollOrientation == PdfScrollOrientation.Horizontal
            ? PdfScrollOrientation.Vertical
            : PdfScrollOrientation.Horizontal;
        UpdateOrientationIcon();
    }

    // O ícone reflete a AÇÃO: em vertical mostra o ícone "horizontal" (livro) para mudar para
    // horizontal; em horizontal mostra o ícone "vertical" (contínuo).
    private void UpdateOrientationIcon()
        => OrientationIcon.Glyph = Viewer.ScrollOrientation == PdfScrollOrientation.Horizontal
            ? Icons.ScrollV : Icons.ScrollH;

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
    {
        if (Viewer.PageCount == 0)
        {
            await DisplayAlert("Imprimir", "Nenhum documento aberto para imprimir.", "OK");
            return;
        }

        try
        {
            await Viewer.PrintAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro", ex.Message, "OK");
        }
    }

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

    // ── Busca ────────────────────────────────────────────────────────────────────

    private bool   _searchOpen;
    private string _lastSearchTerm = string.Empty;
    private CancellationTokenSource? _searchDebounceCts;

    private void OnSearchToggleClicked(object? sender, EventArgs e)
    {
        if (_searchOpen) { CollapseSearch(); return; }
        _searchOpen = true;
        SearchToggleBtn.IsVisible = false;   // a lupa some; o botão ✕ dentro da barra fecha
        AnimateSearchWidth(SearchTargetWidth());
        SearchEntry.Focus();
        UpdateSearchDismissOverlay();        // aberta e vazia → habilita o "tocar fora p/ fechar"
    }

    private void CollapseSearch()
    {
        _searchOpen = false;
        _searchDebounceCts?.Cancel();
        Viewer.ClearSearch();
        SearchEntry.Text        = string.Empty;
        SearchCountLabel.Text   = string.Empty;
        _lastSearchTerm         = string.Empty;
        SearchPrevBtn.IsEnabled = SearchNextBtn.IsEnabled = false;
        SearchEntry.Unfocus();
        AnimateSearchWidth(0);
        SearchToggleBtn.IsVisible      = true;
        SearchDismissOverlay.IsVisible = false;
    }

    // O overlay de "tocar fora p/ fechar" só fica ativo com a busca aberta E sem texto. Assim que o
    // usuário digita, ele é removido para não bloquear o scroll/zoom (necessário para ver os
    // resultados); some também ao recolher.
    private void UpdateSearchDismissOverlay()
        => SearchDismissOverlay.IsVisible = _searchOpen && string.IsNullOrEmpty(SearchEntry.Text);

    // Tocar no documento com a barra aberta e vazia recolhe-a (o overlay só existe nesse estado).
    private void OnSearchDismissTapped(object? sender, EventArgs e) => CollapseSearch();

    // Pesquisa automática enquanto digita, com debounce de 200 ms (evita buscar a cada tecla).
    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateSearchDismissOverlay();   // imediato (sem debounce): some ao digitar, volta ao apagar

        _searchDebounceCts?.Cancel();
        _searchDebounceCts = new CancellationTokenSource();
        var ct   = _searchDebounceCts.Token;
        var term = e.NewTextValue ?? string.Empty;

        _ = Task.Delay(200, ct).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (ct.IsCancellationRequested) return;
                if (string.IsNullOrWhiteSpace(term))
                {
                    Viewer.ClearSearch();
                    _lastSearchTerm = string.Empty;
                }
                else if (term != _lastSearchTerm)
                {
                    _lastSearchTerm = term;
                    Viewer.Search(term);
                }
            });
        }, TaskScheduler.Default);
    }

    // Campo vazio ao perder o foco → recolhe a barra sozinho (cobre o desktop, onde clicar fora
    // remove o foco). _searchOpen é zerado no início de CollapseSearch, então o Unfocus() interno
    // não reentra aqui. No Android tocar fora não tira o foco → o SearchDismissOverlay cobre esse caso.
    private void OnSearchEntryUnfocused(object? sender, FocusEventArgs e)
    {
        if (_searchOpen && string.IsNullOrWhiteSpace(SearchEntry.Text))
            CollapseSearch();
    }

    // Largura da barra quando aberta: no CELULAR ocupa a navbar inteira; no desktop/tablet é uma
    // barra parcial à direita (~330). NavBar.Width já está medido quando o usuário aciona a lupa.
    private double SearchTargetWidth()
    {
        if (DeviceInfo.Current.Idiom == DeviceIdiom.Phone)
            return NavBar.Width > 0 ? NavBar.Width : 360;
        return 330;
    }

    // Anima a largura da barra (overlay sobre a navbar). Crescer empurra/cobre o conteúdo à
    // ESQUERDA; no celular vai até a borda esquerda (navbar inteira).
    private void AnimateSearchWidth(double to)
    {
        double from = SearchBar.Width > 0 ? SearchBar.Width : Math.Max(0, SearchBar.WidthRequest);
        var anim = new Animation(v => SearchBar.WidthRequest = v, from, to);
        anim.Commit(this, "searchExpand", 16, 200, Easing.CubicOut);
    }

    private void OnSearchCompleted(object? sender, EventArgs e)
    {
        var term = SearchEntry.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(term)) { Viewer.ClearSearch(); _lastSearchTerm = string.Empty; return; }
        if (term != _lastSearchTerm) { _lastSearchTerm = term; Viewer.Search(term); }
        else                          Viewer.FindNext();   // Enter de novo → próxima ocorrência
    }

    private void OnSearchPrevClicked(object? sender, EventArgs e) => Viewer.FindPrevious();
    private void OnSearchNextClicked(object? sender, EventArgs e) => Viewer.FindNext();
    private void OnSearchCloseClicked(object? sender, EventArgs e) => CollapseSearch();

    private void OnSearchResultChanged(object? sender, PdfSearchResultEventArgs e)
    {
        bool has = e.MatchCount > 0;
        SearchCountLabel.Text = has
            ? $"{e.CurrentIndex + 1}/{e.MatchCount}"
            : (string.IsNullOrEmpty(SearchEntry.Text) ? string.Empty : "0/0");
        SearchPrevBtn.IsEnabled = has;
        SearchNextBtn.IsEnabled = has;
    }

    // ── Miniaturas ─────────────────────────────────────────────────────────────────

    private void OnToggleThumbnailsClicked(object? sender, EventArgs e) => ToggleThumbnails();

    private void ToggleThumbnails() => Viewer.EnableThumbnailBar = !Viewer.EnableThumbnailBar;

    // Mobile: alterna o drawer de miniaturas (clicar de novo fecha; também fecha tocando fora ou
    // ao escolher uma página).
    private void OnOpenThumbDrawerClicked(object? sender, EventArgs e)
        => Viewer.IsThumbnailBarOpen = !Viewer.IsThumbnailBarOpen;

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

    // Zoom anterior, guardado ao ajustar para 100% (toggle do botão "ajustar à página").
    private double? _zoomBeforeReset;

    // Toggle: se ampliado, guarda o zoom atual e volta a 100%; se já em 100%, restaura o zoom
    // anterior. Como após restaurar o zoom volta a ser != 100%, um novo clique reajusta a 100%
    // (e vice-versa).
    private void OnResetZoomClicked(object? sender, EventArgs e)
    {
        if (Math.Abs(Viewer.ZoomFactor - 1.0) > 0.001)
        {
            _zoomBeforeReset = Viewer.ZoomFactor;
            Viewer.ZoomFactor = 1.0;
        }
        else if (_zoomBeforeReset is double prev)
        {
            Viewer.ZoomFactor = Math.Clamp(prev, Viewer.MinZoom, Viewer.MaxZoom);
        }
    }

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

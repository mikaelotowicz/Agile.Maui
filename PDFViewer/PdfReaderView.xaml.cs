using System.ComponentModel;
using System.Windows.Input;

namespace Agile.Maui;

/// <summary>
/// Leitor de PDF "pronto": toolbar (busca, imprimir, compartilhar, miniaturas, orientação) +
/// barra inferior (zoom e navegação de páginas) em volta do <see cref="PdfViewer"/> base.
/// Autossuficiente (cores e ícones vetoriais próprios). Para uma UI totalmente customizada, use
/// o <see cref="PdfViewer"/> diretamente.
/// </summary>
public partial class PdfReaderView : ContentView
{
    private string? _shareFilePath;
    private string? _streamShareFilePath;
    private PdfReaderNavigationButtonMode _activeNavigationButtonMode = PdfReaderNavigationButtonMode.None;

    public PdfReaderView()
    {
        InitializeComponent();
        ApplyChrome();
        UpdateOrientationIcon();

        // No Windows as miniaturas são uma sidebar FIXA — já abre por padrão no componente pronto.
        // (Pode ser sobrescrito pelo consumidor via EnableThumbnailBar="False".) No mobile o drawer
        // continua fechado, abrindo pelo botão da barra inferior.
        if (DeviceInfo.Current.Platform == DevicePlatform.WinUI)
        {
            EnableThumbnailBar = true;
            SearchBarMaxWidth = 408;
        }

        ApplySearchBarLayout();
        ToolbarHost.SizeChanged += (_, _) => ApplySearchBarLayout();
        Loaded += (_, _) => UpdateNavigationButton();

        // Atualiza o caption de zoom também quando o zoom muda por GESTO (pinch/double-tap) dentro
        // do PdfViewer — não só pelos botões +/−.
        Viewer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PdfViewer.ZoomFactor))
                UpdateZoomLabel(Viewer.ZoomFactor);
        };
        Viewer.PageTapped += OnViewerPageTapped;
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        UpdateNavigationButton();
    }

    // ── Pass-through para o PdfViewer ───────────────────────────────────────────
    public static readonly BindableProperty SourceProperty =
        BindableProperty.Create(nameof(Source), typeof(string), typeof(PdfReaderView));

    public static readonly BindableProperty PdfStreamProperty =
        BindableProperty.Create(nameof(PdfStream), typeof(Stream), typeof(PdfReaderView));

    public static readonly BindableProperty PasswordProperty =
        BindableProperty.Create(nameof(Password), typeof(string), typeof(PdfReaderView));

    public static readonly BindableProperty ScrollOrientationProperty =
        BindableProperty.Create(nameof(ScrollOrientation), typeof(PdfScrollOrientation), typeof(PdfReaderView),
            PdfScrollOrientation.Vertical, BindingMode.TwoWay,
            propertyChanged: (b, _, _) => ((PdfReaderView)b).UpdateOrientationIcon());

    public static readonly BindableProperty ThumbnailBarPlacementProperty =
        BindableProperty.Create(nameof(ThumbnailBarPlacement), typeof(PdfThumbnailPlacement), typeof(PdfReaderView),
            PdfThumbnailPlacement.Right, propertyChanged: (b, _, _) => ((PdfReaderView)b).ApplyChrome());

    public static readonly BindableProperty IsThumbnailBarOpenProperty =
        BindableProperty.Create(nameof(IsThumbnailBarOpen), typeof(bool), typeof(PdfReaderView), false,
            BindingMode.TwoWay);

    public string?               Source                { get => (string?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public Stream?               PdfStream             { get => (Stream?)GetValue(PdfStreamProperty); set => SetValue(PdfStreamProperty, value); }
    public string?               Password              { get => (string?)GetValue(PasswordProperty); set => SetValue(PasswordProperty, value); }
    public PdfScrollOrientation  ScrollOrientation     { get => (PdfScrollOrientation)GetValue(ScrollOrientationProperty); set => SetValue(ScrollOrientationProperty, value); }
    public PdfThumbnailPlacement ThumbnailBarPlacement { get => (PdfThumbnailPlacement)GetValue(ThumbnailBarPlacementProperty); set => SetValue(ThumbnailBarPlacementProperty, value); }
    public bool                  IsThumbnailBarOpen    { get => (bool)GetValue(IsThumbnailBarOpenProperty); set => SetValue(IsThumbnailBarOpenProperty, value); }

    /// <summary>Acesso ao componente base, para configurações avançadas.</summary>
    public PdfViewer ViewerControl => Viewer;

    // ── Pass-through adicional (zoom, cache, render, textos, etc.) ───────────────
    public static readonly BindableProperty ZoomFactorProperty =
        BindableProperty.Create(nameof(ZoomFactor), typeof(double), typeof(PdfReaderView), 1.0, BindingMode.TwoWay);
    public static readonly BindableProperty MinZoomProperty =
        BindableProperty.Create(nameof(MinZoom), typeof(double), typeof(PdfReaderView), 0.5);
    public static readonly BindableProperty MaxZoomProperty =
        BindableProperty.Create(nameof(MaxZoom), typeof(double), typeof(PdfReaderView), 8.0);
    public static readonly BindableProperty IsPinchZoomEnabledProperty =
        BindableProperty.Create(nameof(IsPinchZoomEnabled), typeof(bool), typeof(PdfReaderView), true);
    public static readonly BindableProperty PageBackgroundColorProperty =
        BindableProperty.Create(nameof(PageBackgroundColor), typeof(Color), typeof(PdfReaderView), Colors.White);
    public static readonly BindableProperty PageSpacingProperty =
        BindableProperty.Create(nameof(PageSpacing), typeof(double), typeof(PdfReaderView), 8.0);
    public static readonly BindableProperty RenderScaleProperty =
        BindableProperty.Create(nameof(RenderScale), typeof(double), typeof(PdfReaderView), 1.5);
    public static readonly BindableProperty MaxCacheMBProperty =
        BindableProperty.Create(nameof(MaxCacheMB), typeof(int), typeof(PdfReaderView), 200);
    public static readonly BindableProperty PrefetchAboveProperty =
        BindableProperty.Create(nameof(PrefetchAbove), typeof(int), typeof(PdfReaderView), 2);
    public static readonly BindableProperty PrefetchBelowProperty =
        BindableProperty.Create(nameof(PrefetchBelow), typeof(int), typeof(PdfReaderView), 3);
    public static readonly BindableProperty EnableThumbnailBarProperty =
        BindableProperty.Create(nameof(EnableThumbnailBar), typeof(bool), typeof(PdfReaderView), false,
            propertyChanged: (b, _, _) => ((PdfReaderView)b).ApplyChrome());
    public static readonly BindableProperty CopyButtonTextProperty =
        BindableProperty.Create(nameof(CopyButtonText), typeof(string), typeof(PdfReaderView), "Copy");
    public static readonly BindableProperty CopiedMessageTextProperty =
        BindableProperty.Create(nameof(CopiedMessageText), typeof(string), typeof(PdfReaderView), "Copied");
    public static readonly BindableProperty ThumbnailBarTitleTextProperty =
        BindableProperty.Create(nameof(ThumbnailBarTitleText), typeof(string), typeof(PdfReaderView), "Pages");
    public static readonly BindableProperty PrintJobNameProperty =
        BindableProperty.Create(nameof(PrintJobName), typeof(string), typeof(PdfReaderView), "Document");
    public static readonly BindableProperty NavigationButtonModeProperty =
        BindableProperty.Create(nameof(NavigationButtonMode), typeof(PdfReaderNavigationButtonMode), typeof(PdfReaderView),
            PdfReaderNavigationButtonMode.None, propertyChanged: (b, _, _) => ((PdfReaderView)b).UpdateNavigationButton());
    public static readonly BindableProperty NavigationButtonCommandProperty =
        BindableProperty.Create(nameof(NavigationButtonCommand), typeof(ICommand), typeof(PdfReaderView), null,
            propertyChanged: (b, _, _) => ((PdfReaderView)b).UpdateNavigationButton());
    public static readonly BindableProperty NavigationButtonCommandParameterProperty =
        BindableProperty.Create(nameof(NavigationButtonCommandParameter), typeof(object), typeof(PdfReaderView));
    public static readonly BindableProperty IsFullscreenProperty =
        BindableProperty.Create(nameof(IsFullscreen), typeof(bool), typeof(PdfReaderView), false,
            BindingMode.TwoWay, propertyChanged: (b, _, _) => ((PdfReaderView)b).OnFullscreenChanged());
    public static readonly BindableProperty ShowFullscreenToggleProperty =
        BindableProperty.Create(nameof(ShowFullscreenToggle), typeof(bool), typeof(PdfReaderView), false,
            propertyChanged: (b, _, _) => ((PdfReaderView)b).UpdateFullscreenToggle());
    public static readonly BindableProperty FullscreenTogglePlacementProperty =
        BindableProperty.Create(nameof(FullscreenTogglePlacement), typeof(PdfReaderFullscreenTogglePlacement), typeof(PdfReaderView),
            PdfReaderFullscreenTogglePlacement.Top, propertyChanged: (b, _, _) => ((PdfReaderView)b).UpdateFullscreenToggle());

    public double ZoomFactor          { get => (double)GetValue(ZoomFactorProperty); set => SetValue(ZoomFactorProperty, value); }
    public double MinZoom             { get => (double)GetValue(MinZoomProperty); set => SetValue(MinZoomProperty, value); }
    public double MaxZoom             { get => (double)GetValue(MaxZoomProperty); set => SetValue(MaxZoomProperty, value); }
    public bool   IsPinchZoomEnabled  { get => (bool)GetValue(IsPinchZoomEnabledProperty); set => SetValue(IsPinchZoomEnabledProperty, value); }
    public Color  PageBackgroundColor { get => (Color)GetValue(PageBackgroundColorProperty); set => SetValue(PageBackgroundColorProperty, value); }
    public double PageSpacing         { get => (double)GetValue(PageSpacingProperty); set => SetValue(PageSpacingProperty, value); }
    public double RenderScale         { get => (double)GetValue(RenderScaleProperty); set => SetValue(RenderScaleProperty, value); }
    public int    MaxCacheMB          { get => (int)GetValue(MaxCacheMBProperty); set => SetValue(MaxCacheMBProperty, value); }
    public int    PrefetchAbove       { get => (int)GetValue(PrefetchAboveProperty); set => SetValue(PrefetchAboveProperty, value); }
    public int    PrefetchBelow       { get => (int)GetValue(PrefetchBelowProperty); set => SetValue(PrefetchBelowProperty, value); }
    public bool   EnableThumbnailBar  { get => (bool)GetValue(EnableThumbnailBarProperty); set => SetValue(EnableThumbnailBarProperty, value); }
    public string CopyButtonText      { get => (string)GetValue(CopyButtonTextProperty); set => SetValue(CopyButtonTextProperty, value); }
    public string CopiedMessageText   { get => (string)GetValue(CopiedMessageTextProperty); set => SetValue(CopiedMessageTextProperty, value); }
    public string ThumbnailBarTitleText { get => (string)GetValue(ThumbnailBarTitleTextProperty); set => SetValue(ThumbnailBarTitleTextProperty, value); }
    public string PrintJobName        { get => (string)GetValue(PrintJobNameProperty); set => SetValue(PrintJobNameProperty, value); }
    /// <summary>Modo do botao de navegacao exibido no inicio da toolbar.</summary>
    public PdfReaderNavigationButtonMode NavigationButtonMode { get => (PdfReaderNavigationButtonMode)GetValue(NavigationButtonModeProperty); set => SetValue(NavigationButtonModeProperty, value); }
    /// <summary>Comando opcional para substituir a acao padrao do botao de navegacao.</summary>
    public ICommand? NavigationButtonCommand { get => (ICommand?)GetValue(NavigationButtonCommandProperty); set => SetValue(NavigationButtonCommandProperty, value); }
    /// <summary>Parametro enviado para <see cref="NavigationButtonCommand"/>.</summary>
    public object? NavigationButtonCommandParameter { get => GetValue(NavigationButtonCommandParameterProperty); set => SetValue(NavigationButtonCommandParameterProperty, value); }
    /// <summary>Oculta o chrome interno do leitor e deixa o PDF ocupar todo o espaco do controle.</summary>
    public bool IsFullscreen { get => (bool)GetValue(IsFullscreenProperty); set => SetValue(IsFullscreenProperty, value); }
    /// <summary>Exibe um botao flutuante sobre o PDF para entrar/sair do fullscreen interno.</summary>
    public bool ShowFullscreenToggle { get => (bool)GetValue(ShowFullscreenToggleProperty); set => SetValue(ShowFullscreenToggleProperty, value); }
    /// <summary>Posicao vertical do botao flutuante de fullscreen.</summary>
    public PdfReaderFullscreenTogglePlacement FullscreenTogglePlacement { get => (PdfReaderFullscreenTogglePlacement)GetValue(FullscreenTogglePlacementProperty); set => SetValue(FullscreenTogglePlacementProperty, value); }

    // ── Aparência do chrome (cores) ──────────────────────────────────────────────
    public static readonly BindableProperty ToolbarColorProperty =
        BindableProperty.Create(nameof(ToolbarColor), typeof(Color), typeof(PdfReaderView), Color.FromArgb("#FFFFFF"));
    public static readonly BindableProperty BottomBarColorProperty =
        BindableProperty.Create(nameof(BottomBarColor), typeof(Color), typeof(PdfReaderView), Color.FromArgb("#FFFFFF"));
    public static readonly BindableProperty IconColorProperty =
        BindableProperty.Create(nameof(IconColor), typeof(Color), typeof(PdfReaderView), Color.FromArgb("#44444A"));
    public static readonly BindableProperty CaptionColorProperty =
        BindableProperty.Create(nameof(CaptionColor), typeof(Color), typeof(PdfReaderView), Color.FromArgb("#2A2A2E"));

    /// <summary>Cor de fundo da barra superior (toolbar).</summary>
    public Color ToolbarColor   { get => (Color)GetValue(ToolbarColorProperty); set => SetValue(ToolbarColorProperty, value); }
    /// <summary>Cor de fundo da barra inferior.</summary>
    public Color BottomBarColor { get => (Color)GetValue(BottomBarColorProperty); set => SetValue(BottomBarColorProperty, value); }
    /// <summary>Cor dos ícones (fonte) e do contador de páginas da toolbar.</summary>
    public Color IconColor      { get => (Color)GetValue(IconColorProperty); set => SetValue(IconColorProperty, value); }
    /// <summary>Cor do título (toolbar) e dos textos de zoom (%) e página (1/20) da barra inferior.</summary>
    public Color CaptionColor   { get => (Color)GetValue(CaptionColorProperty); set => SetValue(CaptionColorProperty, value); }

    // ── Textos próprios do leitor (localizáveis, padrão inglês) ──────────────────
    public static readonly BindableProperty LoadingTextProperty =
        BindableProperty.Create(nameof(LoadingText), typeof(string), typeof(PdfReaderView), "Loading…");
    public static readonly BindableProperty SearchPlaceholderProperty =
        BindableProperty.Create(nameof(SearchPlaceholder), typeof(string), typeof(PdfReaderView), "Search…");
    public static readonly BindableProperty PageCountFormatProperty =
        BindableProperty.Create(nameof(PageCountFormat), typeof(string), typeof(PdfReaderView), "{0} pages");
    public static readonly BindableProperty LoadFailedTextProperty =
        BindableProperty.Create(nameof(LoadFailedText), typeof(string), typeof(PdfReaderView), "Failed to load");
    public static readonly BindableProperty SearchBarMaxWidthProperty =
        BindableProperty.Create(nameof(SearchBarMaxWidth), typeof(double), typeof(PdfReaderView),
            double.PositiveInfinity, propertyChanged: (b, _, _) => ((PdfReaderView)b).ApplySearchBarLayout());

    /// <summary>Texto do overlay de carregamento.</summary>
    public string LoadingText       { get => (string)GetValue(LoadingTextProperty); set => SetValue(LoadingTextProperty, value); }
    /// <summary>Placeholder do campo de busca.</summary>
    public string SearchPlaceholder { get => (string)GetValue(SearchPlaceholderProperty); set => SetValue(SearchPlaceholderProperty, value); }
    /// <summary>Formato do contador de páginas na toolbar ({0} = total). Ex.: "{0} páginas".</summary>
    public string PageCountFormat   { get => (string)GetValue(PageCountFormatProperty); set => SetValue(PageCountFormatProperty, value); }
    /// <summary>Texto exibido quando o documento falha ao carregar.</summary>
    public string LoadFailedText    { get => (string)GetValue(LoadFailedTextProperty); set => SetValue(LoadFailedTextProperty, value); }
    /// <summary>Largura maxima da barra de busca. No Windows o padrão é 408; no mobile preenche a toolbar.</summary>
    public double SearchBarMaxWidth { get => (double)GetValue(SearchBarMaxWidthProperty); set => SetValue(SearchBarMaxWidthProperty, value); }

    // ── Liga/desliga de chrome ───────────────────────────────────────────────────
    public static readonly BindableProperty ShowToolbarProperty          = Toggle(nameof(ShowToolbar));
    public static readonly BindableProperty ShowSearchProperty           = Toggle(nameof(ShowSearch));
    public static readonly BindableProperty ShowPrintProperty            = Toggle(nameof(ShowPrint));
    public static readonly BindableProperty ShowShareProperty            = Toggle(nameof(ShowShare));
    public static readonly BindableProperty ShowOrientationToggleProperty = Toggle(nameof(ShowOrientationToggle));
    public static readonly BindableProperty ShowBottomBarProperty         = Toggle(nameof(ShowBottomBar));

    private static BindableProperty Toggle(string name) =>
        BindableProperty.Create(name, typeof(bool), typeof(PdfReaderView), true,
            propertyChanged: (b, _, _) => ((PdfReaderView)b).ApplyChrome());

    public bool ShowToolbar          { get => (bool)GetValue(ShowToolbarProperty); set => SetValue(ShowToolbarProperty, value); }
    public bool ShowSearch           { get => (bool)GetValue(ShowSearchProperty); set => SetValue(ShowSearchProperty, value); }
    public bool ShowPrint            { get => (bool)GetValue(ShowPrintProperty); set => SetValue(ShowPrintProperty, value); }
    public bool ShowShare            { get => (bool)GetValue(ShowShareProperty); set => SetValue(ShowShareProperty, value); }
    public bool ShowOrientationToggle{ get => (bool)GetValue(ShowOrientationToggleProperty); set => SetValue(ShowOrientationToggleProperty, value); }
    public bool ShowBottomBar        { get => (bool)GetValue(ShowBottomBarProperty); set => SetValue(ShowBottomBarProperty, value); }

    private void ApplySearchBarLayout()
    {
        var maxWidth = SearchBarMaxWidth;
        if (double.IsNaN(maxWidth) || double.IsInfinity(maxWidth) || maxWidth <= 0)
        {
            SearchBar.MaximumWidthRequest = double.PositiveInfinity;
            SearchBar.WidthRequest = -1;
            SearchBar.HorizontalOptions = LayoutOptions.Fill;
            return;
        }

        var toolbarWidth = ToolbarHost.Width;
        var horizontalMargin = SearchBar.Margin.Left + SearchBar.Margin.Right;
        var availableWidth = toolbarWidth > horizontalMargin
            ? toolbarWidth - horizontalMargin
            : maxWidth;
        var targetWidth = Math.Min(maxWidth, availableWidth);

        SearchBar.MaximumWidthRequest = maxWidth;
        SearchBar.WidthRequest = targetWidth;
        SearchBar.HorizontalOptions = LayoutOptions.End;
    }

    private void ApplyChrome()
    {
        ToolbarHost.IsVisible    = ShowToolbar && !IsFullscreen;
        BottomBarHost.IsVisible  = ShowBottomBar && !IsFullscreen;
        SearchBtn.IsVisible      = ShowSearch;
        PrintBtn.IsVisible       = ShowPrint;
        ShareBtn.IsVisible       = ShowShare;
        OrientationBtn.IsVisible = ShowOrientationToggle;

        // Botão de miniaturas na barra inferior: visível só quando o drawer está habilitado
        // (ThumbnailBarPlacement != None); posicionado no MESMO lado do drawer.
        bool right = ThumbnailBarPlacement == PdfThumbnailPlacement.Right;
        ThumbsBtn.IsVisible         = ThumbnailBarPlacement != PdfThumbnailPlacement.None;
        Grid.SetColumn(ThumbsBtn, right ? 2 : 0);
        ThumbsBtn.HorizontalOptions = right ? LayoutOptions.End : LayoutOptions.Start;

        Viewer.EnableThumbnailBar = EnableThumbnailBar && !IsFullscreen;
        UpdateNavigationButton();
        UpdateFullscreenToggle();
    }

    private void OnFullscreenChanged()
    {
        if (IsFullscreen)
        {
            if (_searchOpen)
                CollapseSearch();

            IsThumbnailBarOpen = false;
        }

        ApplyChrome();
    }

    private void UpdateFullscreenToggle()
    {
        FullscreenToggleBtn.IsVisible = ShowFullscreenToggle;
        FullscreenToggleGlyph.Text = IsFullscreen
            ? PdfReaderIcons.FullscreenExit
            : PdfReaderIcons.Fullscreen;

        var bottom = FullscreenTogglePlacement == PdfReaderFullscreenTogglePlacement.Bottom;
        FullscreenToggleBtn.VerticalOptions = bottom ? LayoutOptions.End : LayoutOptions.Start;
        FullscreenToggleBtn.Margin = bottom
            ? new Thickness(0, 0, 14, 14)
            : new Thickness(0, 14, 14, 0);
    }

    private void OnFullscreenToggleClicked(object? sender, EventArgs e)
        => IsFullscreen = !IsFullscreen;

    // ── Eventos re-expostos ───────────────────────────────────────────────────────
    private void UpdateNavigationButton()
    {
        _activeNavigationButtonMode = ResolveNavigationButtonMode();
        NavigationBtn.IsVisible = ShowToolbar && _activeNavigationButtonMode != PdfReaderNavigationButtonMode.None;
        NavigationButtonGlyph.Text = _activeNavigationButtonMode == PdfReaderNavigationButtonMode.Back
            ? PdfReaderIcons.Back
            : PdfReaderIcons.Menu;
    }

    private PdfReaderNavigationButtonMode ResolveNavigationButtonMode()
    {
        if (NavigationButtonMode == PdfReaderNavigationButtonMode.None)
            return PdfReaderNavigationButtonMode.None;

        if (NavigationButtonMode == PdfReaderNavigationButtonMode.Back
            || NavigationButtonMode == PdfReaderNavigationButtonMode.Menu)
            return NavigationButtonMode;

        if (CanNavigateBack())
            return PdfReaderNavigationButtonMode.Back;

        if (CanOpenMenu() || NavigationButtonCommand is not null)
            return PdfReaderNavigationButtonMode.Menu;

        return PdfReaderNavigationButtonMode.None;
    }

    private async void OnNavigationButtonClicked(object? sender, EventArgs e)
    {
        var command = NavigationButtonCommand;
        var parameter = NavigationButtonCommandParameter ?? this;
        if (command?.CanExecute(parameter) == true)
        {
            command.Execute(parameter);
            return;
        }

        if (_activeNavigationButtonMode == PdfReaderNavigationButtonMode.Back)
            await NavigateBackAsync();
        else if (_activeNavigationButtonMode == PdfReaderNavigationButtonMode.Menu)
            OpenMenu();
    }

    private bool CanNavigateBack()
        => Navigation.ModalStack.Count > 0 || Navigation.NavigationStack.Count > 1;

    private async Task NavigateBackAsync()
    {
        try
        {
            if (Navigation.ModalStack.Count > 0)
            {
                await Navigation.PopModalAsync();
                return;
            }

            if (Navigation.NavigationStack.Count > 1)
            {
                await Navigation.PopAsync();
                return;
            }

            var shell = GetCurrentShell();
            if (shell is not null)
                await shell.GoToAsync("..");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PdfReaderView] Navigation back failed: {ex}");
        }
        finally
        {
            UpdateNavigationButton();
        }
    }

    private bool CanOpenMenu()
        => CanOpenShellFlyout() || FindFlyoutPage() is not null;

    private static bool CanOpenShellFlyout()
    {
        var shell = GetCurrentShell();
        return shell is not null && shell.FlyoutBehavior != FlyoutBehavior.Disabled;
    }

    private void OpenMenu()
    {
        try
        {
            var shell = GetCurrentShell();
            if (shell is not null && shell.FlyoutBehavior != FlyoutBehavior.Disabled)
            {
                shell.FlyoutIsPresented = true;
                return;
            }

            var flyout = FindFlyoutPage();
            if (flyout is not null)
                flyout.IsPresented = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PdfReaderView] Open menu failed: {ex}");
        }
    }

    private static Shell? GetCurrentShell()
    {
        try
        {
            return Shell.Current;
        }
        catch
        {
            return null;
        }
    }

    private FlyoutPage? FindFlyoutPage()
    {
        for (Element? current = this; current is not null; current = current.Parent)
        {
            if (current is FlyoutPage flyout)
                return flyout;
        }

        foreach (var window in Application.Current?.Windows ?? [])
        {
            var flyout = FindFlyoutPage(window.Page);
            if (flyout is not null)
                return flyout;
        }

        return null;
    }

    private static FlyoutPage? FindFlyoutPage(Page? page)
    {
        return page switch
        {
            FlyoutPage flyout => flyout,
            NavigationPage navigation => FindFlyoutPage(navigation.CurrentPage),
            TabbedPage tabbed => FindFlyoutPage(tabbed.CurrentPage),
            Shell => null,
            _ => null,
        };
    }

    public event EventHandler<PdfDocumentLoadedEventArgs>?     DocumentLoaded;
    public event EventHandler<PdfDocumentLoadFailedEventArgs>? DocumentLoadFailed;
    public event EventHandler<PdfPageChangedEventArgs>?        PageChanged;

    private void OnDocumentLoaded(object? sender, PdfDocumentLoadedEventArgs e)
    {
        LoadingOverlay.IsVisible = false;
        SetStatsText(string.Format(PageCountFormat ?? "{0}", e.PageCount));
        UpdatePageControls(0, e.PageCount);
        UpdateZoomLabel(Viewer.ZoomFactor);
        DocumentLoaded?.Invoke(this, e);
    }

    private void OnDocumentLoadFailed(object? sender, PdfDocumentLoadFailedEventArgs e)
    {
        LoadingOverlay.IsVisible = false;
        SetStatsText(LoadFailedText);
        DocumentLoadFailed?.Invoke(this, e);
    }

    private void SetStatsText(string? text)
    {
        StatsLabel.Text = string.IsNullOrWhiteSpace(text) ? " " : text;
        StatsLabel.InvalidateMeasure();
        ToolbarHost.InvalidateMeasure();
    }

    private void OnPageChanged(object? sender, PdfPageChangedEventArgs e)
    {
        UpdatePageControls(e.Page, Viewer.PageCount);
        PageChanged?.Invoke(this, e);
    }

    // ── Documento ─────────────────────────────────────────────────────────────────
    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == nameof(Source)) ApplySource();
        else if (propertyName == nameof(PdfStream))
        {
            _streamShareFilePath = null;
            if (PdfStream is not null) ShowLoading();
        }
    }

    // Resolve o Source: URL ou arquivo existente → usa direto; senão tenta como ASSET EMPACOTADO
    // (copia para o cache). Permite definir Source="arquivo.pdf" no XAML, mesmo sendo bundle.
    private async void ApplySource()
    {
        var s = Source;
        FileNameLabel.Text = ResolveName(s);
        _shareFilePath = null;
        if (string.IsNullOrEmpty(s)) { Viewer.Source = null; return; }

        ShowLoading();

        if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            Viewer.Source = s;
            return;
        }

        // Só é arquivo local de verdade quando o caminho é ABSOLUTO. No Windows MSIX o working
        // directory é a pasta de instalação (que contém os MauiAsset), então File.Exists("x.pdf")
        // retorna true para um asset relativo — mas esse caminho relativo quebra o Share
        // (StorageFile.GetFileFromPathAsync exige caminho absoluto). Caminhos não-rooted seguem
        // para o ramo de asset empacotado abaixo, que materializa no cache (caminho absoluto).
        if (Path.IsPathRooted(s) && File.Exists(s))
        {
            _shareFilePath = s;
            Viewer.Source = s;
            return;
        }

        try
        {
            // Asset empacotado (MauiAsset) → copia para um caminho de arquivo no cache.
            var dest = Path.Combine(FileSystem.CacheDirectory, Path.GetFileName(s));
            using (var src = await FileSystem.OpenAppPackageFileAsync(s))
            using (var fs  = File.Create(dest))
                await src.CopyToAsync(fs);
            if (Source != s) return;
            _shareFilePath = dest;
            Viewer.Source = dest;
        }
        catch
        {
            Viewer.Source = s;   // não é asset → deixa o handler reportar o erro
        }
    }

    private void ShowLoading() => LoadingOverlay.IsVisible = true;   // texto vem do binding LoadingText

    private static string ResolveName(string? pathOrUrl)
    {
        if (string.IsNullOrEmpty(pathOrUrl)) return "PDF";
        bool isUrl = pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        var name = Path.GetFileName(isUrl ? pathOrUrl.Split('?')[0] : pathOrUrl);
        return string.IsNullOrEmpty(name) ? "PDF" : name;
    }

    // ── Imprimir / Compartilhar ─────────────────────────────────────────────────
    private async void OnPrintClicked(object? sender, EventArgs e)
    {
        try { await Viewer.PrintAsync(); } catch { /* sem documento / cancelado */ }
    }

    private async void OnShareClicked(object? sender, EventArgs e)
    {
        try
        {
            var path = await ResolveShareFileAsync();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            // ShareFileRequest é cross-platform (Android/iOS/macOS/Windows): o painel
            // nativo de compartilhamento do sistema recebe o arquivo PDF em si.
            await Microsoft.Maui.ApplicationModel.DataTransfer.Share.RequestAsync(
                new Microsoft.Maui.ApplicationModel.DataTransfer.ShareFileRequest
                {
                    Title = FileNameLabel.Text,
                    File  = new Microsoft.Maui.ApplicationModel.DataTransfer.ShareFile(path, "application/pdf"),
                });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PdfReaderView] Compartilhamento falhou: {ex}");
        }
    }

    // Materializa o PDF atual em um caminho de arquivo local compartilhável, cobrindo os três
    // cenários de origem. Sempre compartilhamos o ARQUIVO (não o link) para que o destinatário
    // receba o PDF mesmo sem acesso à URL. Resultados são cacheados por Source/PdfStream.
    private async Task<string?> ResolveShareFileAsync()
    {
        var src = Source;

        // 1) Arquivo local ou asset empacotado já materializado em ApplySource (também serve
        //    de cache do download de URL — ver caso 3).
        if (!string.IsNullOrEmpty(_shareFilePath) && File.Exists(_shareFilePath))
            return _shareFilePath;

        // 2) Caminho de arquivo local informado diretamente em Source (precisa ser ABSOLUTO —
        //    ver nota em ApplySource sobre o working directory no Windows MSIX).
        if (!string.IsNullOrEmpty(src) && !IsUrl(src) && Path.IsPathRooted(src) && File.Exists(src))
            return _shareFilePath = src;

        // 3) URL → baixa o PDF para o cache uma única vez por Source.
        if (!string.IsNullOrEmpty(src) && IsUrl(src))
            return _shareFilePath = await DownloadToCacheAsync(src);

        // 4) Stream em memória → grava no cache.
        if (PdfStream is not null)
            return await EnsureStreamShareFileAsync();

        return null;
    }

    private static bool IsUrl(string s) => s.StartsWith("http", StringComparison.OrdinalIgnoreCase);

    private static readonly HttpClient _shareHttpClient = new();

    private async Task<string?> DownloadToCacheAsync(string url)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, EnsurePdfName(ResolveName(url)));

        using var resp = await _shareHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        using (var net = await resp.Content.ReadAsStreamAsync())
        using (var fs  = File.Create(path))
            await net.CopyToAsync(fs);

        return path;
    }

    private static string EnsurePdfName(string name)
        => name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? name : name + ".pdf";

    private async Task<string?> EnsureStreamShareFileAsync()
    {
        if (!string.IsNullOrEmpty(_streamShareFilePath) && File.Exists(_streamShareFilePath))
            return _streamShareFilePath;

        var stream = PdfStream;
        if (stream is null) return null;

        var path = Path.Combine(FileSystem.CacheDirectory, EnsurePdfName(ResolveName(Source)));
        if (stream.CanSeek)
            stream.Position = 0;

        using (var fs = File.Create(path))
            await stream.CopyToAsync(fs);

        if (stream.CanSeek)
            stream.Position = 0;

        _streamShareFilePath = path;
        return path;
    }

    // ── Orientação ────────────────────────────────────────────────────────────────
    private void OnOrientationClicked(object? sender, EventArgs e)
        => ScrollOrientation = ScrollOrientation == PdfScrollOrientation.Horizontal
            ? PdfScrollOrientation.Vertical : PdfScrollOrientation.Horizontal;

    private void UpdateOrientationIcon()
    {
        bool horizontal = ScrollOrientation == PdfScrollOrientation.Horizontal;
        // Mostra o ícone da AÇÃO: em vertical → ícone "horizontal"; em horizontal → ícone "vertical".
        IconToHorizontal.IsVisible = !horizontal;
        IconToVertical.IsVisible   = horizontal;
    }

    // ── Miniaturas ──────────────────────────────────────────────────────────────
    private void OnThumbnailsClicked(object? sender, EventArgs e)
    {
        // Desktop: barra lateral fixa (EnableThumbnailBar). Mobile: drawer sobreposto.
        if (DeviceInfo.Current.Idiom == DeviceIdiom.Desktop)
            Viewer.EnableThumbnailBar = !Viewer.EnableThumbnailBar;
        else
            IsThumbnailBarOpen = !IsThumbnailBarOpen;
    }

    // ── Zoom ──────────────────────────────────────────────────────────────────────
    private double ZoomStep => DeviceInfo.Current.Idiom == DeviceIdiom.Desktop ? 0.10 : 0.25;

    private void OnZoomInClicked(object? sender, EventArgs e)  => StepZoom(+1);
    private void OnZoomOutClicked(object? sender, EventArgs e) => StepZoom(-1);

    private void StepZoom(int dir)
    {
        double step   = ZoomStep;
        double target = Math.Round((Viewer.ZoomFactor + dir * step) / step) * step;
        Viewer.ZoomFactor = Math.Clamp(target, Viewer.MinZoom, Viewer.MaxZoom);
        UpdateZoomLabel(Viewer.ZoomFactor);
    }

    private void UpdateZoomLabel(double zoom) => ZoomLabel.Text = $"{zoom * 100:F0}%";

    // ── Navegação de páginas ──────────────────────────────────────────────────────
    private void OnPrevClicked(object? sender, EventArgs e) { if (Viewer.CurrentPage > 0) Viewer.CurrentPage--; }
    private void OnNextClicked(object? sender, EventArgs e) { if (Viewer.CurrentPage < Viewer.PageCount - 1) Viewer.CurrentPage++; }

    private void UpdatePageControls(int page, int count)
    {
        PageLabel.Text   = count > 0 ? $"{page + 1} / {count}" : "—";
        PrevBtn.Opacity  = page > 0 ? 1 : 0.3;
        NextBtn.Opacity  = (count > 0 && page < count - 1) ? 1 : 0.3;
    }

    // ── Busca ──────────────────────────────────────────────────────────────────────
    private bool _searchOpen;
    private bool _suppressSearchTextChanged;
    private string _lastSearchTerm = string.Empty;
    private CancellationTokenSource? _searchDebounceCts;

    private void OnSearchToggleClicked(object? sender, EventArgs e)
    {
        if (_searchOpen) { CollapseSearch(); return; }
        _searchOpen = true;
        SearchBar.IsVisible = true;
        SearchToolbarDismissOverlay.IsVisible = true;
        SearchDismissOverlay.IsVisible = true;
        SearchEntry.Focus();
    }

    private void CollapseSearch()
    {
        _searchOpen = false;
        _suppressSearchTextChanged = true;
        _searchDebounceCts?.Cancel();
        SearchToolbarDismissOverlay.IsVisible = false;
        SearchDismissOverlay.IsVisible = false;
        SearchEntry.Unfocus();
        SearchEntry.Text = string.Empty;
        SearchBar.IsVisible = false;
        SearchCountLabel.Text = string.Empty;
        _lastSearchTerm = string.Empty;
        Viewer.ClearSearch();
        _suppressSearchTextChanged = false;
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressSearchTextChanged) return;

        _searchDebounceCts?.Cancel();
        _searchDebounceCts = new CancellationTokenSource();
        var ct = _searchDebounceCts.Token;
        var term = e.NewTextValue ?? string.Empty;
        _ = Task.Delay(300, ct).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            MainThread.BeginInvokeOnMainThread(() => RunSearch(term));
        }, TaskScheduler.Default);
    }

    private void OnSearchCompleted(object? sender, EventArgs e)
    {
        var term = SearchEntry.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(term)) { Viewer.ClearSearch(); _lastSearchTerm = string.Empty; return; }
        if (term != _lastSearchTerm) RunSearch(term);
        else                          Viewer.FindNext();
    }

    private void OnSearchEntryFocused(object? sender, FocusEventArgs e)
    {
        if (_searchOpen)
        {
            SearchToolbarDismissOverlay.IsVisible = true;
            SearchDismissOverlay.IsVisible = true;
        }
    }

    private void OnSearchEntryUnfocused(object? sender, FocusEventArgs e)
    {
        SearchToolbarDismissOverlay.IsVisible = false;
        SearchDismissOverlay.IsVisible = false;
        if (_searchOpen && string.IsNullOrWhiteSpace(SearchEntry.Text))
            CollapseSearch();
    }

    private void OnViewerPageTapped(object? sender, EventArgs e)
    {
        if (!_searchOpen) return;

        DismissSearchFocus();
    }

    private void OnSearchDismissOverlayTapped(object? sender, TappedEventArgs e) => DismissSearchFocus();

    private void DismissSearchFocus()
    {
        SearchToolbarDismissOverlay.IsVisible = false;
        SearchDismissOverlay.IsVisible = false;
        SearchEntry.Unfocus();
        if (_searchOpen && string.IsNullOrWhiteSpace(SearchEntry.Text))
            CollapseSearch();
    }

    private void RunSearch(string term)
    {
        if (string.IsNullOrWhiteSpace(term)) { Viewer.ClearSearch(); _lastSearchTerm = string.Empty; return; }
        _lastSearchTerm = term;
        Viewer.Search(term);
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
        SearchPrevBtn.Opacity = has ? 1 : 0.3;
        SearchNextBtn.Opacity = has ? 1 : 0.3;
    }
}

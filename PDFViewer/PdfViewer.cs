// Controls/PdfViewer/PdfViewer.cs
using System.Windows.Input;

namespace Agile.Maui;

/// <summary>
/// Visualizador PDF multiplataforma com virtualização, cache LRU e zoom fluido.
/// Motor: PDFium via APIs nativas do SO (Android: PdfRenderer, iOS/Mac: CGPDFDocument,
/// Windows: Windows.Data.Pdf).
/// </summary>
public class PdfViewer : View
{
    // ── Source ───────────────────────────────────────────────────────────────────
    public static readonly BindableProperty SourceProperty =
        BindableProperty.Create(nameof(Source), typeof(string), typeof(PdfViewer));

    public static readonly BindableProperty PdfStreamProperty =
        BindableProperty.Create(nameof(PdfStream), typeof(Stream), typeof(PdfViewer));

    /// <summary>
    /// Senha para abrir PDFs protegidos. Aplicada na carga do documento (PDFium). Se a senha
    /// estiver incorreta/ausente num PDF protegido, dispara <see cref="DocumentLoadFailed"/>.
    /// Implementada no Android e Windows (PDFium) e no iOS/Mac (PdfDocument.Unlock).
    /// </summary>
    public static readonly BindableProperty PasswordProperty =
        BindableProperty.Create(nameof(Password), typeof(string), typeof(PdfViewer));

    // ── Estado ───────────────────────────────────────────────────────────────────
    public static readonly BindableProperty CurrentPageProperty =
        BindableProperty.Create(nameof(CurrentPage), typeof(int), typeof(PdfViewer), 0,
            BindingMode.TwoWay, validateValue: (_, v) => (int)v >= 0,
            coerceValue: CoerceCurrentPage);

    public static readonly BindableProperty PageCountProperty =
        BindableProperty.Create(nameof(PageCount), typeof(int), typeof(PdfViewer), 0,
            propertyChanged: (b, _, __) => ((PdfViewer)b).CoerceValue(CurrentPageProperty));

    // ── Zoom ─────────────────────────────────────────────────────────────────────
    public static readonly BindableProperty ZoomFactorProperty =
        BindableProperty.Create(nameof(ZoomFactor), typeof(double), typeof(PdfViewer), 1.0,
            BindingMode.TwoWay, validateValue: (_, v) => (double)v > 0,
            coerceValue: CoerceZoomFactor);

    // MinZoom default alinhado com a semântica do PdfZoomManager (0.5x–8x).
    public static readonly BindableProperty MinZoomProperty =
        BindableProperty.Create(nameof(MinZoom), typeof(double), typeof(PdfViewer), 0.5,
            validateValue: (_, v) => (double)v > 0,
            propertyChanged: OnZoomRangeChanged);

    public static readonly BindableProperty MaxZoomProperty =
        BindableProperty.Create(nameof(MaxZoom), typeof(double), typeof(PdfViewer), 8.0,
            validateValue: (_, v) => (double)v >= 1.0,
            propertyChanged: OnZoomRangeChanged);

    // ── Coerção ───────────────────────────────────────────────────────────────────
    // Garante que CurrentPage nunca saia de [0, PageCount-1]. Antes de carregar o
    // documento PageCount é 0; nesse caso não há limite superior conhecido, então
    // apenas o piso 0 (já garantido por validateValue) é aplicado.
    private static object CoerceCurrentPage(BindableObject bindable, object value)
    {
        var viewer = (PdfViewer)bindable;
        int page = (int)value;
        if (page < 0) page = 0;
        int count = viewer.PageCount;
        if (count > 0 && page > count - 1) page = count - 1;
        return page;
    }

    // Garante que ZoomFactor nunca saia de [MinZoom, MaxZoom], mesmo via binding direto.
    private static object CoerceZoomFactor(BindableObject bindable, object value)
    {
        var viewer = (PdfViewer)bindable;
        double min = viewer.MinZoom;
        double max = viewer.MaxZoom;
        if (max < min) max = min; // coerência defensiva: MinZoom <= MaxZoom
        return Math.Clamp((double)value, min, max);
    }

    // Quando MinZoom/MaxZoom mudam, re-coage ZoomFactor para o novo range.
    private static void OnZoomRangeChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var viewer = (PdfViewer)bindable;
        viewer.CoerceValue(ZoomFactorProperty);
    }

    // ── Funcionalidades ───────────────────────────────────────────────────────────
    /// <summary>
    /// Exibe uma barra lateral FIXA de miniaturas das páginas (clicáveis para navegar).
    /// Implementado no Windows. No Android/iOS a barra é um overlay sob demanda — ver
    /// <see cref="IsThumbnailBarOpen"/>.
    /// </summary>
    public static readonly BindableProperty EnableThumbnailBarProperty =
        BindableProperty.Create(nameof(EnableThumbnailBar), typeof(bool), typeof(PdfViewer), false);

    /// <summary>
    /// Abre/fecha a barra de miniaturas como um DRAWER sobreposto (desliza da direita, com scrim;
    /// toque fora fecha — o controle volta o valor para false). Pensado para ser ligado a um botão
    /// via binding TwoWay. Implementado no Android e iOS; no Windows a barra é fixa
    /// (<see cref="EnableThumbnailBar"/>) e esta propriedade não tem efeito.
    /// </summary>
    public static readonly BindableProperty IsThumbnailBarOpenProperty =
        BindableProperty.Create(nameof(IsThumbnailBarOpen), typeof(bool), typeof(PdfViewer), false,
            BindingMode.TwoWay);

    /// <summary>
    /// Habilita e posiciona o drawer de miniaturas (Android/iOS): <c>None</c> (padrão) desabilita;
    /// <c>Left</c>/<c>Right</c> habilitam o drawer naquele lado. Substitui o gate de habilitação no
    /// mobile (uma única propriedade). Sem efeito no Windows (barra fixa via EnableThumbnailBar).
    /// </summary>
    public static readonly BindableProperty ThumbnailBarPlacementProperty =
        BindableProperty.Create(nameof(ThumbnailBarPlacement), typeof(PdfThumbnailPlacement), typeof(PdfViewer),
            PdfThumbnailPlacement.None);

    /// <summary>
    /// Quando true (padrão), pré-carrega páginas vizinhas (PrefetchAbove/Below) para scroll
    /// suave. Quando false, desativa o prefetch — renderiza apenas o visível, sob demanda
    /// (menor uso de memória/CPU, ao custo de placeholders momentâneos ao rolar).
    /// </summary>
    public static readonly BindableProperty EnablePageCachingProperty =
        BindableProperty.Create(nameof(EnablePageCaching), typeof(bool), typeof(PdfViewer), true);

    public static readonly BindableProperty IsPinchZoomEnabledProperty =
        BindableProperty.Create(nameof(IsPinchZoomEnabled), typeof(bool), typeof(PdfViewer), true);

    // ── Aparência ─────────────────────────────────────────────────────────────────
    public static readonly BindableProperty PageBackgroundColorProperty =
        BindableProperty.Create(nameof(PageBackgroundColor), typeof(Color), typeof(PdfViewer), Colors.White);

    public static readonly BindableProperty PageSpacingProperty =
        BindableProperty.Create(nameof(PageSpacing), typeof(double), typeof(PdfViewer), 8.0,
            validateValue: (_, v) => (double)v >= 0);

    /// <summary>
    /// Direção do scroll entre páginas: vertical (padrão) ou horizontal (paginado, tipo livro —
    /// uma página por tela, fit-page centralizada, snap por página). Implementado no Android, Windows
    /// e iOS/Mac (PdfKit UsePageViewController).
    /// </summary>
    public static readonly BindableProperty ScrollOrientationProperty =
        BindableProperty.Create(nameof(ScrollOrientation), typeof(PdfScrollOrientation), typeof(PdfViewer),
            PdfScrollOrientation.Vertical);

    // ── Textos (localizáveis) ───────────────────────────────────────────────────
    // Todos os textos exibidos ao usuário têm padrão em INGLÊS e são bindáveis — o desenvolvedor
    // pode trocá-los para qualquer idioma.

    /// <summary>
    /// Texto do botão/menu de copiar exibido sobre a seleção. Padrão: "Copy". Usado no Android
    /// (pílula flutuante) e no Windows (item do menu de contexto da seleção).
    /// </summary>
    public static readonly BindableProperty CopyButtonTextProperty =
        BindableProperty.Create(nameof(CopyButtonText), typeof(string), typeof(PdfViewer), "Copy");

    /// <summary>Mensagem de confirmação ao copiar (toast). Padrão: "Copied". Usado no Android.</summary>
    public static readonly BindableProperty CopiedMessageTextProperty =
        BindableProperty.Create(nameof(CopiedMessageText), typeof(string), typeof(PdfViewer), "Copied");

    /// <summary>Título da barra/sidebar de miniaturas. Padrão: "Pages". Usado no Windows (sidebar fixa).</summary>
    public static readonly BindableProperty ThumbnailBarTitleTextProperty =
        BindableProperty.Create(nameof(ThumbnailBarTitleText), typeof(string), typeof(PdfViewer), "Pages");

    /// <summary>Nome do trabalho de impressão (quando não há nome de arquivo). Padrão: "Document".</summary>
    public static readonly BindableProperty PrintJobNameProperty =
        BindableProperty.Create(nameof(PrintJobName), typeof(string), typeof(PdfViewer), "Document");

    // ── Performance ───────────────────────────────────────────────────────────────
    /// <summary>Escala de renderização (DPI). 1.5 = 144 DPI. Padrão: screen density.</summary>
    public static readonly BindableProperty RenderScaleProperty =
        BindableProperty.Create(nameof(RenderScale), typeof(double), typeof(PdfViewer), 1.5,
            validateValue: (_, v) => (double)v > 0);

    /// <summary>Limite de memória para cache de páginas em MB. Padrão: 200 MB.</summary>
    public static readonly BindableProperty MaxCacheMBProperty =
        BindableProperty.Create(nameof(MaxCacheMB), typeof(int), typeof(PdfViewer), 200,
            validateValue: (_, v) => (int)v >= 10);

    /// <summary>Páginas a pré-carregar acima da visível. Padrão: 2.</summary>
    public static readonly BindableProperty PrefetchAboveProperty =
        BindableProperty.Create(nameof(PrefetchAbove), typeof(int), typeof(PdfViewer), 2,
            validateValue: (_, v) => (int)v >= 0);

    /// <summary>Páginas a pré-carregar abaixo da visível. Padrão: 3.</summary>
    public static readonly BindableProperty PrefetchBelowProperty =
        BindableProperty.Create(nameof(PrefetchBelow), typeof(int), typeof(PdfViewer), 3,
            validateValue: (_, v) => (int)v >= 0);

    // ── Comandos ──────────────────────────────────────────────────────────────────
    public static readonly BindableProperty DocumentLoadedCommandProperty =
        BindableProperty.Create(nameof(DocumentLoadedCommand), typeof(ICommand), typeof(PdfViewer));

    public static readonly BindableProperty DocumentLoadFailedCommandProperty =
        BindableProperty.Create(nameof(DocumentLoadFailedCommand), typeof(ICommand), typeof(PdfViewer));

    public static readonly BindableProperty PageChangedCommandProperty =
        BindableProperty.Create(nameof(PageChangedCommand), typeof(ICommand), typeof(PdfViewer));

    // ── Propriedades CLR ──────────────────────────────────────────────────────────
    public string? Source
    {
        get => (string?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Stream? PdfStream
    {
        get => (Stream?)GetValue(PdfStreamProperty);
        set => SetValue(PdfStreamProperty, value);
    }

    public string? Password
    {
        get => (string?)GetValue(PasswordProperty);
        set => SetValue(PasswordProperty, value);
    }

    public int CurrentPage
    {
        get => (int)GetValue(CurrentPageProperty);
        set => SetValue(CurrentPageProperty, value);
    }

    public int PageCount
    {
        get => (int)GetValue(PageCountProperty);
        private set => SetValue(PageCountProperty, value);
    }

    public double ZoomFactor
    {
        get => (double)GetValue(ZoomFactorProperty);
        set => SetValue(ZoomFactorProperty, value);
    }

    public double MinZoom
    {
        get => (double)GetValue(MinZoomProperty);
        set => SetValue(MinZoomProperty, value);
    }

    public double MaxZoom
    {
        get => (double)GetValue(MaxZoomProperty);
        set => SetValue(MaxZoomProperty, value);
    }

    public bool EnableThumbnailBar
    {
        get => (bool)GetValue(EnableThumbnailBarProperty);
        set => SetValue(EnableThumbnailBarProperty, value);
    }

    public bool IsThumbnailBarOpen
    {
        get => (bool)GetValue(IsThumbnailBarOpenProperty);
        set => SetValue(IsThumbnailBarOpenProperty, value);
    }

    public PdfThumbnailPlacement ThumbnailBarPlacement
    {
        get => (PdfThumbnailPlacement)GetValue(ThumbnailBarPlacementProperty);
        set => SetValue(ThumbnailBarPlacementProperty, value);
    }

    public bool EnablePageCaching
    {
        get => (bool)GetValue(EnablePageCachingProperty);
        set => SetValue(EnablePageCachingProperty, value);
    }

    public bool IsPinchZoomEnabled
    {
        get => (bool)GetValue(IsPinchZoomEnabledProperty);
        set => SetValue(IsPinchZoomEnabledProperty, value);
    }

    public Color PageBackgroundColor
    {
        get => (Color)GetValue(PageBackgroundColorProperty);
        set => SetValue(PageBackgroundColorProperty, value);
    }

    public double PageSpacing
    {
        get => (double)GetValue(PageSpacingProperty);
        set => SetValue(PageSpacingProperty, value);
    }

    public PdfScrollOrientation ScrollOrientation
    {
        get => (PdfScrollOrientation)GetValue(ScrollOrientationProperty);
        set => SetValue(ScrollOrientationProperty, value);
    }

    public string CopyButtonText
    {
        get => (string)GetValue(CopyButtonTextProperty);
        set => SetValue(CopyButtonTextProperty, value);
    }

    public string CopiedMessageText
    {
        get => (string)GetValue(CopiedMessageTextProperty);
        set => SetValue(CopiedMessageTextProperty, value);
    }

    public string ThumbnailBarTitleText
    {
        get => (string)GetValue(ThumbnailBarTitleTextProperty);
        set => SetValue(ThumbnailBarTitleTextProperty, value);
    }

    public string PrintJobName
    {
        get => (string)GetValue(PrintJobNameProperty);
        set => SetValue(PrintJobNameProperty, value);
    }

    public double RenderScale
    {
        get => (double)GetValue(RenderScaleProperty);
        set => SetValue(RenderScaleProperty, value);
    }

    public int MaxCacheMB
    {
        get => (int)GetValue(MaxCacheMBProperty);
        set => SetValue(MaxCacheMBProperty, value);
    }

    public int PrefetchAbove
    {
        get => (int)GetValue(PrefetchAboveProperty);
        set => SetValue(PrefetchAboveProperty, value);
    }

    public int PrefetchBelow
    {
        get => (int)GetValue(PrefetchBelowProperty);
        set => SetValue(PrefetchBelowProperty, value);
    }

    public ICommand? DocumentLoadedCommand
    {
        get => (ICommand?)GetValue(DocumentLoadedCommandProperty);
        set => SetValue(DocumentLoadedCommandProperty, value);
    }

    public ICommand? DocumentLoadFailedCommand
    {
        get => (ICommand?)GetValue(DocumentLoadFailedCommandProperty);
        set => SetValue(DocumentLoadFailedCommandProperty, value);
    }

    public ICommand? PageChangedCommand
    {
        get => (ICommand?)GetValue(PageChangedCommandProperty);
        set => SetValue(PageChangedCommandProperty, value);
    }

    // ── Eventos ───────────────────────────────────────────────────────────────────
    public event EventHandler<PdfDocumentLoadedEventArgs>?    DocumentLoaded;
    public event EventHandler<PdfDocumentLoadFailedEventArgs>? DocumentLoadFailed;
    public event EventHandler<PdfPageChangedEventArgs>?        PageChanged;
    internal event EventHandler?                               PageTapped;
    /// <summary>Disparado quando a busca produz/atualiza resultados (total e índice atual).</summary>
    public event EventHandler<PdfSearchResultEventArgs>?       SearchResultChanged;
    /// <summary>
    /// Disparado ao tocar num link do PDF. Defina <c>Handled = true</c> para suprimir a ação padrão
    /// (navegar à página de destino interna ou abrir a URI externa). Implementado no Android e no
    /// iOS/Mac (URLs externas via PdfKit; links internos de página são navegados pelo próprio PdfView).
    /// </summary>
    public event EventHandler<PdfLinkTappedEventArgs>?         LinkTapped;

    // ── API Pública ───────────────────────────────────────────────────────────────
    public Task GoToPageAsync(int page)
    {
        CurrentPage = Math.Clamp(page, 0, Math.Max(0, PageCount - 1));
        return Task.CompletedTask;
    }

    public Task ZoomInAsync()
    {
        ZoomFactor = Math.Min(MaxZoom, ZoomFactor * 1.25);
        return Task.CompletedTask;
    }

    public Task ZoomOutAsync()
    {
        ZoomFactor = Math.Max(MinZoom, ZoomFactor / 1.25);
        return Task.CompletedTask;
    }

    public Task ResetZoomAsync()
    {
        ZoomFactor = 1.0;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Envia o documento carregado para o sistema de impressão nativo da plataforma
    /// (Windows: <c>PrintManager</c>; Android: <c>PrintManager</c> + adapter;
    /// iOS/Mac: <c>UIPrintInteractionController</c>). A própria UI nativa de impressão é
    /// exibida ao usuário. Não tem efeito se nenhum documento estiver carregado.
    /// </summary>
    public Task PrintAsync()
    {
        Handler?.Invoke(nameof(PrintAsync));
        return Task.CompletedTask;
    }

    // ── Busca de texto ──────────────────────────────────────────────────────────
    /// <summary>
    /// Busca <paramref name="term"/> no documento (case-insensitive) e realça/rola até a 1ª
    /// ocorrência. O total e o índice atual são notificados por <see cref="SearchResultChanged"/>.
    /// Implementado no Android, Windows (PDFium) e iOS/Mac (PdfKit).
    /// </summary>
    public void Search(string term) => Handler?.Invoke(nameof(Search), term);

    /// <summary>Vai para a próxima ocorrência da busca atual (circular).</summary>
    public void FindNext() => Handler?.Invoke(nameof(FindNext));

    /// <summary>Vai para a ocorrência anterior da busca atual (circular).</summary>
    public void FindPrevious() => Handler?.Invoke(nameof(FindPrevious));

    /// <summary>Limpa a busca e o realce.</summary>
    public void ClearSearch() => Handler?.Invoke(nameof(ClearSearch));

    // ── Internos (chamados pelos handlers) ─────────────────────────────────────────
    // Guarda de reentrância: o set de CurrentPage em RaisePageChanged pode reentrar
    // (binding TwoWay → handler → RaisePageChanged) e causar loop infinito.
    private bool _suppressPageChanged;

    // Garante execução na main thread, pois eventos/Commands podem atualizar a UI
    // e os handlers podem dispará-los a partir de threads de render/background.
    private static void OnMainThread(Action action)
    {
        if (MainThread.IsMainThread)
            action();
        else
            MainThread.BeginInvokeOnMainThread(action);
    }

    internal void RaiseDocumentLoaded(int pageCount)
    {
        OnMainThread(() =>
        {
            PageCount = pageCount;
            var args = new PdfDocumentLoadedEventArgs(pageCount);
            DocumentLoaded?.Invoke(this, args);
            if (DocumentLoadedCommand?.CanExecute(args) == true)
                DocumentLoadedCommand.Execute(args);
        });
    }

    internal void RaiseDocumentLoadFailed(string message)
    {
        OnMainThread(() =>
        {
            var args = new PdfDocumentLoadFailedEventArgs(message);
            DocumentLoadFailed?.Invoke(this, args);
            if (DocumentLoadFailedCommand?.CanExecute(args) == true)
                DocumentLoadFailedCommand.Execute(args);
        });
    }

    // Disparado pelo handler na UI thread (origem: toque). Retorna os args para o handler decidir
    // se executa a ação padrão (quando Handled continua false).
    internal PdfLinkTappedEventArgs RaiseLinkTapped(string? uri, int destinationPage)
    {
        var args = new PdfLinkTappedEventArgs(uri, destinationPage);
        LinkTapped?.Invoke(this, args);
        return args;
    }

    internal void RaisePageTapped()
    {
        OnMainThread(() => PageTapped?.Invoke(this, EventArgs.Empty));
    }

    // matchCount = total de ocorrências; currentIndex = índice 0-based da atual (-1 se nenhuma).
    internal void RaiseSearchResult(int matchCount, int currentIndex)
    {
        OnMainThread(() =>
            SearchResultChanged?.Invoke(this, new PdfSearchResultEventArgs(matchCount, currentIndex)));
    }

    internal void RaisePageChanged(int page)
    {
        OnMainThread(() =>
        {
            if (_suppressPageChanged) return;
            _suppressPageChanged = true;
            try
            {
                CurrentPage = page;
                var args = new PdfPageChangedEventArgs(CurrentPage);
                PageChanged?.Invoke(this, args);
                if (PageChangedCommand?.CanExecute(args) == true)
                    PageChangedCommand.Execute(args);
            }
            finally
            {
                _suppressPageChanged = false;
            }
        });
    }
}

/// <summary>Direção do scroll entre páginas do <see cref="PdfViewer"/>.</summary>
public enum PdfScrollOrientation
{
    Vertical,
    Horizontal,
}

/// <summary>
/// Posição do drawer de miniaturas (Android/iOS). <see cref="None"/> desabilita as miniaturas
/// (o app esconde o botão); <see cref="Left"/>/<see cref="Right"/> habilitam o drawer naquele lado.
/// </summary>
public enum PdfThumbnailPlacement
{
    None,
    Left,
    Right,
}

// ── EventArgs ──────────────────────────────────────────────────────────────────
public sealed class PdfDocumentLoadedEventArgs : EventArgs
{
    public int PageCount { get; }
    public PdfDocumentLoadedEventArgs(int pageCount) => PageCount = pageCount;
}

public sealed class PdfDocumentLoadFailedEventArgs : EventArgs
{
    public string Message { get; }
    public PdfDocumentLoadFailedEventArgs(string message) => Message = message;
}

public sealed class PdfPageChangedEventArgs : EventArgs
{
    public int Page { get; }
    public PdfPageChangedEventArgs(int page) => Page = page;
}

public sealed class PdfLinkTappedEventArgs : EventArgs
{
    /// <summary>URI externa do link (http/mailto/etc.), ou null se for um link interno.</summary>
    public string? Uri { get; }
    /// <summary>Índice 0-based da página de destino (link interno), ou -1 se for URI externa.</summary>
    public int DestinationPage { get; }
    /// <summary>Defina true para suprimir a ação padrão (navegar/abrir).</summary>
    public bool Handled { get; set; }
    public PdfLinkTappedEventArgs(string? uri, int destinationPage)
    {
        Uri = uri;
        DestinationPage = destinationPage;
    }
}

public sealed class PdfSearchResultEventArgs : EventArgs
{
    /// <summary>Total de ocorrências encontradas.</summary>
    public int MatchCount { get; }
    /// <summary>Índice 0-based da ocorrência atual, ou -1 se não há nenhuma.</summary>
    public int CurrentIndex { get; }
    public PdfSearchResultEventArgs(int matchCount, int currentIndex)
    {
        MatchCount   = matchCount;
        CurrentIndex = currentIndex;
    }
}

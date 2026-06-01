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
    /// Exibe uma barra lateral de miniaturas das páginas (clicáveis para navegar).
    /// Implementado no Windows; nas demais plataformas a propriedade ainda não tem efeito.
    /// </summary>
    public static readonly BindableProperty EnableThumbnailBarProperty =
        BindableProperty.Create(nameof(EnableThumbnailBar), typeof(bool), typeof(PdfViewer), false);

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

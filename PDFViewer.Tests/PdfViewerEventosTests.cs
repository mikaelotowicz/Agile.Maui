using System.Windows.Input;
using Xunit;

namespace Agile.Maui.PdfTests;

/// <summary>
/// Eventos públicos e Commands disparados pelos métodos internos <c>Raise*</c> (o caminho que os
/// handlers de plataforma usam). O <c>MainThread</c> é roteado para o <see cref="TestDispatcher"/>
/// (ver <see cref="TestMainThreadInstaller"/>): com EnqueueMode desligado tudo roda inline.
/// </summary>
public class PdfViewerEventosTests
{
    private sealed class ComandoFake : ICommand
    {
        public bool PodeExecutar { get; set; } = true;
        public List<object?> Execucoes { get; } = new();
        public int ConsultasCanExecute { get; private set; }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) { ConsultasCanExecute++; return PodeExecutar; }
        public void Execute(object? parameter) => Execucoes.Add(parameter);
    }

    // ── DocumentLoaded ───────────────────────────────────────────────────────────
    [Fact]
    public void RaiseDocumentLoaded_define_PageCount_e_dispara_evento_com_args_corretos()
    {
        var v = new PdfViewer();
        PdfDocumentLoadedEventArgs? recebido = null;
        v.DocumentLoaded += (s, e) => recebido = e;

        v.RaiseDocumentLoaded(7);

        Assert.Equal(7, v.PageCount);
        Assert.NotNull(recebido);
        Assert.Equal(7, recebido!.PageCount);
    }

    [Fact]
    public void RaiseDocumentLoaded_executa_o_Command_com_os_MESMOS_args_do_evento()
    {
        var v = new PdfViewer();
        var cmd = new ComandoFake();
        v.DocumentLoadedCommand = cmd;
        PdfDocumentLoadedEventArgs? doEvento = null;
        v.DocumentLoaded += (s, e) => doEvento = e;

        v.RaiseDocumentLoaded(3);

        var parametro = Assert.Single(cmd.Execucoes);
        Assert.Same(doEvento, parametro);
        Assert.Equal(1, cmd.ConsultasCanExecute);
    }

    [Fact]
    public void RaiseDocumentLoaded_respeita_CanExecute_false_mas_ainda_dispara_o_evento()
    {
        var v = new PdfViewer();
        var cmd = new ComandoFake { PodeExecutar = false };
        v.DocumentLoadedCommand = cmd;
        int eventos = 0;
        v.DocumentLoaded += (s, e) => eventos++;

        v.RaiseDocumentLoaded(3);

        Assert.Empty(cmd.Execucoes);
        Assert.Equal(1, eventos);
    }

    // ── DocumentLoadFailed ───────────────────────────────────────────────────────
    [Fact]
    public void RaiseDocumentLoadFailed_dispara_evento_e_Command_com_a_mensagem()
    {
        var v = new PdfViewer();
        var cmd = new ComandoFake();
        v.DocumentLoadFailedCommand = cmd;
        PdfDocumentLoadFailedEventArgs? recebido = null;
        v.DocumentLoadFailed += (s, e) => recebido = e;

        v.RaiseDocumentLoadFailed("senha incorreta");

        Assert.NotNull(recebido);
        Assert.Equal("senha incorreta", recebido!.Message);
        Assert.Same(recebido, Assert.Single(cmd.Execucoes));
    }

    [Fact]
    public void RaiseDocumentLoadFailed_respeita_CanExecute_false()
    {
        var v = new PdfViewer();
        var cmd = new ComandoFake { PodeExecutar = false };
        v.DocumentLoadFailedCommand = cmd;

        v.RaiseDocumentLoadFailed("erro");

        Assert.Empty(cmd.Execucoes);
    }

    // ── PageChanged ──────────────────────────────────────────────────────────────
    [Fact]
    public void RaisePageChanged_define_CurrentPage_e_dispara_evento()
    {
        var v = new PdfViewer();
        v.RaiseDocumentLoaded(5);
        PdfPageChangedEventArgs? recebido = null;
        v.PageChanged += (s, e) => recebido = e;

        v.RaisePageChanged(2);

        Assert.Equal(2, v.CurrentPage);
        Assert.Equal(2, recebido!.Page);
    }

    [Fact]
    public void RaisePageChanged_reporta_a_pagina_JA_coagida_no_evento()
    {
        var v = new PdfViewer();
        v.RaiseDocumentLoaded(5);
        PdfPageChangedEventArgs? recebido = null;
        v.PageChanged += (s, e) => recebido = e;

        v.RaisePageChanged(99);   // acima do total → coagido a 4 antes de montar os args

        Assert.Equal(4, v.CurrentPage);
        Assert.Equal(4, recebido!.Page);
    }

    [Fact]
    public void RaisePageChanged_com_a_MESMA_pagina_dispara_de_novo()
    {
        // Contrato ATUAL do controle: não há dedup aqui — o filtro de página repetida vive nos
        // handlers de plataforma (_lastReportedPage). Se este teste quebrar porque o controle
        // passou a dedupar, revise os handlers para não perder o primeiro report.
        var v = new PdfViewer();
        v.RaiseDocumentLoaded(5);
        int eventos = 0;
        v.PageChanged += (s, e) => eventos++;

        v.RaisePageChanged(2);
        v.RaisePageChanged(2);

        Assert.Equal(2, eventos);
    }

    [Fact]
    public void RaisePageChanged_reentrante_e_suprimido()
    {
        // Guard _suppressPageChanged: um handler que re-dispara RaisePageChanged não causa loop.
        var v = new PdfViewer();
        v.RaiseDocumentLoaded(5);
        int eventos = 0;
        v.PageChanged += (s, e) =>
        {
            eventos++;
            if (eventos == 1) v.RaisePageChanged(3);   // reentrância → ignorada
        };

        v.RaisePageChanged(1);

        Assert.Equal(1, eventos);
        Assert.Equal(1, v.CurrentPage);
    }

    [Fact]
    public void RaisePageChanged_executa_o_PageChangedCommand()
    {
        var v = new PdfViewer();
        v.RaiseDocumentLoaded(5);
        var cmd = new ComandoFake();
        v.PageChangedCommand = cmd;

        v.RaisePageChanged(2);

        var args = Assert.IsType<PdfPageChangedEventArgs>(Assert.Single(cmd.Execucoes));
        Assert.Equal(2, args.Page);
    }

    // ── SearchResultChanged / PageTapped ─────────────────────────────────────────
    [Fact]
    public void RaiseSearchResult_dispara_evento_com_total_e_indice()
    {
        var v = new PdfViewer();
        PdfSearchResultEventArgs? recebido = null;
        v.SearchResultChanged += (s, e) => recebido = e;

        v.RaiseSearchResult(12, 4);

        Assert.Equal(12, recebido!.MatchCount);
        Assert.Equal(4, recebido.CurrentIndex);
    }

    [Fact]
    public void RaisePageTapped_dispara_o_evento_interno()
    {
        var v = new PdfViewer();
        int toques = 0;
        v.PageTapped += (s, e) => toques++;

        v.RaisePageTapped();

        Assert.Equal(1, toques);
    }

    // ── LinkTapped (síncrono, retorna os args ao handler de plataforma) ─────────
    [Fact]
    public void RaiseLinkTapped_retorna_args_com_uri_e_destino_e_Handled_false_por_padrao()
    {
        var v = new PdfViewer();

        var args = v.RaiseLinkTapped("https://exemplo.com", -1);

        Assert.Equal("https://exemplo.com", args.Uri);
        Assert.Equal(-1, args.DestinationPage);
        Assert.False(args.Handled);
    }

    [Fact]
    public void RaiseLinkTapped_entrega_os_mesmos_args_ao_assinante_que_pode_marcar_Handled()
    {
        var v = new PdfViewer();
        PdfLinkTappedEventArgs? doEvento = null;
        v.LinkTapped += (s, e) => { doEvento = e; e.Handled = true; };

        var retorno = v.RaiseLinkTapped(null, 7);

        Assert.Same(doEvento, retorno);
        Assert.Null(retorno.Uri);
        Assert.Equal(7, retorno.DestinationPage);
        Assert.True(retorno.Handled);   // o handler de plataforma usa isto p/ suprimir a ação padrão
    }

    // ── Despacho (OnMainThread) ──────────────────────────────────────────────────
    [Fact]
    public void Raise_fora_da_main_thread_e_despachado_e_NAO_coalescido()
    {
        // Com EnqueueMode, "não é main thread": cada Raise vira UMA action pendente no
        // dispatcher. Documenta que o controle não coalesce reports — cada chamada dispara.
        var v = new PdfViewer();
        v.RaiseDocumentLoaded(5);
        int eventos = 0;
        v.PageChanged += (s, e) => eventos++;

        var dispatcher = TestDispatcherProvider.ForCurrentThread();
        dispatcher.EnqueueMode = true;
        try
        {
            v.RaisePageChanged(1);
            v.RaisePageChanged(2);
            Assert.Equal(0, eventos);               // nada roda antes do "frame" da main thread
            Assert.Equal(2, dispatcher.PendingCount);

            dispatcher.ProcessQueue();
        }
        finally
        {
            dispatcher.EnqueueMode = false;         // dispatcher é ThreadStatic — não vazar estado
            dispatcher.ProcessQueue();
        }

        Assert.Equal(2, eventos);
        Assert.Equal(2, v.CurrentPage);
    }
}

/// <summary>Contrato dos EventArgs públicos (passthrough de construtor → propriedades).</summary>
public class PdfEventArgsTests
{
    [Fact]
    public void PdfDocumentLoadedEventArgs_expoe_PageCount()
        => Assert.Equal(9, new PdfDocumentLoadedEventArgs(9).PageCount);

    [Fact]
    public void PdfDocumentLoadFailedEventArgs_expoe_Message()
        => Assert.Equal("x", new PdfDocumentLoadFailedEventArgs("x").Message);

    [Fact]
    public void PdfPageChangedEventArgs_expoe_Page()
        => Assert.Equal(5, new PdfPageChangedEventArgs(5).Page);

    [Fact]
    public void PdfLinkTappedEventArgs_expoe_uri_destino_e_Handled_mutavel()
    {
        var args = new PdfLinkTappedEventArgs("mailto:a@b.c", -1);
        Assert.Equal("mailto:a@b.c", args.Uri);
        Assert.Equal(-1, args.DestinationPage);
        Assert.False(args.Handled);
        args.Handled = true;
        Assert.True(args.Handled);
    }

    [Fact]
    public void PdfSearchResultEventArgs_expoe_total_e_indice()
    {
        var args = new PdfSearchResultEventArgs(3, -1);
        Assert.Equal(3, args.MatchCount);
        Assert.Equal(-1, args.CurrentIndex);
    }
}

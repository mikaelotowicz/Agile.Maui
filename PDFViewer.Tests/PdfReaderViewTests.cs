using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Agile.Maui.PdfTests;

/// <summary>
/// <see cref="PdfReaderView"/> no host neutro (o XAML SourceGen infla sem Application; a
/// plataforma reporta Unknown — logo, os ramos específicos de Windows NÃO rodam aqui).
/// Os elementos internos são lidos por <c>FindByName</c> (name scope do XAML).
/// </summary>
public class PdfReaderViewDefaultsTests
{
    private readonly PdfReaderView _r = new();

    [Fact] public void E_instanciavel_no_host() => Assert.NotNull(_r);

    [Fact] public void ViewerControl_expoe_o_PdfViewer_interno()
        => Assert.IsType<PdfViewer>(_r.ViewerControl);

    [Fact] public void Source_PdfStream_Password_padrao_sao_nulos()
    {
        Assert.Null(_r.Source);
        Assert.Null(_r.PdfStream);
        Assert.Null(_r.Password);
    }

    [Fact] public void ScrollOrientation_padrao_e_vertical()
        => Assert.Equal(PdfScrollOrientation.Vertical, _r.ScrollOrientation);

    [Fact] public void ThumbnailBarPlacement_padrao_do_leitor_e_Right()
        => Assert.Equal(PdfThumbnailPlacement.Right, _r.ThumbnailBarPlacement);   // difere do PdfViewer (None)

    [Fact] public void Zoom_padrao_do_leitor()
    {
        Assert.Equal(1.0, _r.ZoomFactor);
        Assert.Equal(0.5, _r.MinZoom);
        Assert.Equal(8.0, _r.MaxZoom);
        Assert.True(_r.IsPinchZoomEnabled);
    }

    [Fact] public void Render_e_cache_padrao_do_leitor()
    {
        Assert.Equal(1.5, _r.RenderScale);
        Assert.Equal(200, _r.MaxCacheMB);
        Assert.Equal(2, _r.PrefetchAbove);
        Assert.Equal(3, _r.PrefetchBelow);
    }

    [Fact] public void Textos_padrao_em_ingles()
    {
        Assert.Equal("Copy", _r.CopyButtonText);
        Assert.Equal("Copied", _r.CopiedMessageText);
        Assert.Equal("Pages", _r.ThumbnailBarTitleText);
        Assert.Equal("Document", _r.PrintJobName);
        Assert.Equal("Loading…", _r.LoadingText);
        Assert.Equal("Search…", _r.SearchPlaceholder);
        Assert.Equal("{0} pages", _r.PageCountFormat);
        Assert.Equal("Failed to load", _r.LoadFailedText);
    }

    [Fact] public void Chrome_padrao_todo_ligado()
    {
        Assert.True(_r.ShowToolbar);
        Assert.True(_r.ShowSearch);
        Assert.True(_r.ShowPrint);
        Assert.True(_r.ShowShare);
        Assert.True(_r.ShowOrientationToggle);
        Assert.True(_r.ShowBottomBar);
    }

    [Fact] public void Fullscreen_padrao_desligado()
    {
        Assert.False(_r.IsFullscreen);
        Assert.False(_r.ShowFullscreenToggle);
        Assert.Equal(PdfReaderFullscreenTogglePlacement.Top, _r.FullscreenTogglePlacement);
    }

    [Fact] public void NavigationButtonMode_padrao_e_None()
        => Assert.Equal(PdfReaderNavigationButtonMode.None, _r.NavigationButtonMode);

    [Fact] public void Fora_do_Windows_EnableThumbnailBar_e_false_e_busca_sem_teto_de_largura()
    {
        // No host a plataforma é Unknown → o ramo do ctor exclusivo de WinUI não roda.
        Assert.False(_r.EnableThumbnailBar);
        Assert.True(double.IsPositiveInfinity(_r.SearchBarMaxWidth));
    }
}

public class PdfReaderViewComportamentoTests
{
    private static T Elemento<T>(PdfReaderView r, string nome) where T : Element
        => r.FindByName<T>(nome) ?? throw new Xunit.Sdk.XunitException($"Elemento '{nome}' não encontrado no name scope");

    // ── Pass-through de propriedades para o PdfViewer interno (bindings do XAML) ──
    [Fact]
    public void Propriedades_fluem_para_o_ViewerControl()
    {
        var r = new PdfReaderView
        {
            MinZoom        = 1.0,
            MaxZoom        = 4.0,
            PageSpacing    = 16.0,
            CopyButtonText = "Copiar",
            RenderScale    = 2.0,
            MaxCacheMB     = 64,
            Password       = "segredo",
            PageBackgroundColor = Colors.Beige,
            ScrollOrientation   = PdfScrollOrientation.Horizontal,
            ThumbnailBarPlacement = PdfThumbnailPlacement.Left,
        };

        var v = r.ViewerControl;
        Assert.Equal(1.0, v.MinZoom);
        Assert.Equal(4.0, v.MaxZoom);
        Assert.Equal(16.0, v.PageSpacing);
        Assert.Equal("Copiar", v.CopyButtonText);
        Assert.Equal(2.0, v.RenderScale);
        Assert.Equal(64, v.MaxCacheMB);
        Assert.Equal("segredo", v.Password);
        Assert.Equal(Colors.Beige, v.PageBackgroundColor);
        Assert.Equal(PdfScrollOrientation.Horizontal, v.ScrollOrientation);
        Assert.Equal(PdfThumbnailPlacement.Left, v.ThumbnailBarPlacement);
    }

    [Fact]
    public void ZoomFactor_e_TwoWay_do_viewer_para_o_leitor()
    {
        var r = new PdfReaderView();
        r.ViewerControl.ZoomFactor = 2.0;   // como um gesto de pinch reportado pelo handler
        Assert.Equal(2.0, r.ZoomFactor);
    }

    [Fact]
    public void Gesto_de_zoom_no_viewer_atualiza_o_rotulo_de_porcentagem()
    {
        var r = new PdfReaderView();
        r.ViewerControl.ZoomFactor = 2.0;
        Assert.Equal("200%", Elemento<Label>(r, "ZoomLabel").Text);
    }

    // ── Fullscreen ───────────────────────────────────────────────────────────────
    [Fact]
    public void Fullscreen_esconde_toolbar_e_barra_inferior_e_fecha_o_drawer()
    {
        var r = new PdfReaderView { IsThumbnailBarOpen = true };

        r.IsFullscreen = true;

        Assert.False(Elemento<Grid>(r, "ToolbarHost").IsVisible);
        Assert.False(Elemento<Grid>(r, "BottomBarHost").IsVisible);
        Assert.False(r.IsThumbnailBarOpen);

        r.IsFullscreen = false;
        Assert.True(Elemento<Grid>(r, "ToolbarHost").IsVisible);
        Assert.True(Elemento<Grid>(r, "BottomBarHost").IsVisible);
    }

    [Fact]
    public void Toggle_de_fullscreen_mostra_o_glifo_correspondente()
    {
        var r = new PdfReaderView { ShowFullscreenToggle = true };
        var btn   = Elemento<Border>(r, "FullscreenToggleBtn");
        var glifo = Elemento<Label>(r, "FullscreenToggleGlyph");

        Assert.True(btn.IsVisible);
        Assert.Equal(PdfReaderIcons.Fullscreen, glifo.Text);

        r.IsFullscreen = true;
        Assert.Equal(PdfReaderIcons.FullscreenExit, glifo.Text);
    }

    [Fact]
    public void FullscreenTogglePlacement_Bottom_ancora_o_botao_embaixo()
    {
        var r = new PdfReaderView
        {
            ShowFullscreenToggle = true,
            FullscreenTogglePlacement = PdfReaderFullscreenTogglePlacement.Bottom,
        };
        var btn = Elemento<Border>(r, "FullscreenToggleBtn");
        Assert.Equal(LayoutAlignment.End, btn.VerticalOptions.Alignment);
    }

    // ── Chrome liga/desliga ──────────────────────────────────────────────────────
    [Fact]
    public void ShowToolbar_false_esconde_a_toolbar()
    {
        var r = new PdfReaderView { ShowToolbar = false };
        Assert.False(Elemento<Grid>(r, "ToolbarHost").IsVisible);
    }

    [Fact]
    public void Botoes_da_toolbar_respeitam_os_toggles()
    {
        var r = new PdfReaderView { ShowSearch = false, ShowPrint = false, ShowShare = false, ShowOrientationToggle = false };
        Assert.False(Elemento<Border>(r, "SearchBtn").IsVisible);
        Assert.False(Elemento<Border>(r, "PrintBtn").IsVisible);
        Assert.False(Elemento<Border>(r, "ShareBtn").IsVisible);
        Assert.False(Elemento<Border>(r, "OrientationBtn").IsVisible);
    }

    // ── Botão de miniaturas segue o placement ────────────────────────────────────
    [Fact]
    public void ThumbnailBarPlacement_posiciona_ou_esconde_o_botao_de_miniaturas()
    {
        var r = new PdfReaderView();                       // padrão Right
        var btn = Elemento<Border>(r, "ThumbsBtn");
        Assert.True(btn.IsVisible);
        Assert.Equal(2, Grid.GetColumn(btn));

        r.ThumbnailBarPlacement = PdfThumbnailPlacement.Left;
        Assert.True(btn.IsVisible);
        Assert.Equal(0, Grid.GetColumn(btn));

        r.ThumbnailBarPlacement = PdfThumbnailPlacement.None;
        Assert.False(btn.IsVisible);
    }

    // ── Ícone de orientação mostra a AÇÃO (não o estado) ─────────────────────────
    [Fact]
    public void Icone_de_orientacao_alterna_com_o_estado()
    {
        var r = new PdfReaderView();
        Assert.True(Elemento<Image>(r, "IconToHorizontal").IsVisible);
        Assert.False(Elemento<Image>(r, "IconToVertical").IsVisible);

        r.ScrollOrientation = PdfScrollOrientation.Horizontal;
        Assert.False(Elemento<Image>(r, "IconToHorizontal").IsVisible);
        Assert.True(Elemento<Image>(r, "IconToVertical").IsVisible);
    }

    // ── Botão de navegação ──────────────────────────────────────────────────────
    [Fact]
    public void NavigationButtonMode_Menu_e_Back_mostram_o_glifo_correspondente()
    {
        var r = new PdfReaderView();
        var btn   = Elemento<Border>(r, "NavigationBtn");
        var glifo = Elemento<Label>(r, "NavigationButtonGlyph");

        Assert.False(btn.IsVisible);                       // None (padrão)

        r.NavigationButtonMode = PdfReaderNavigationButtonMode.Menu;
        Assert.True(btn.IsVisible);
        Assert.Equal(PdfReaderIcons.Menu, glifo.Text);

        r.NavigationButtonMode = PdfReaderNavigationButtonMode.Back;
        Assert.True(btn.IsVisible);
        Assert.Equal(PdfReaderIcons.Back, glifo.Text);
    }

    [Fact]
    public void NavigationButtonMode_Auto_sem_navegacao_nem_comando_fica_oculto()
    {
        var r = new PdfReaderView { NavigationButtonMode = PdfReaderNavigationButtonMode.Auto };
        Assert.False(Elemento<Border>(r, "NavigationBtn").IsVisible);
    }

    [Fact]
    public void NavigationButtonMode_Auto_com_comando_vira_menu()
    {
        var r = new PdfReaderView
        {
            NavigationButtonCommand = new Command(() => { }),
            NavigationButtonMode    = PdfReaderNavigationButtonMode.Auto,
        };
        Assert.True(Elemento<Border>(r, "NavigationBtn").IsVisible);
        Assert.Equal(PdfReaderIcons.Menu, Elemento<Label>(r, "NavigationButtonGlyph").Text);
    }

    // ── Barra de busca ──────────────────────────────────────────────────────────
    [Fact]
    public void SearchBarMaxWidth_limita_a_largura_da_barra()
    {
        var r = new PdfReaderView { SearchBarMaxWidth = 300 };
        var barra = Elemento<Border>(r, "SearchBar");
        Assert.Equal(300, barra.MaximumWidthRequest);
        Assert.Equal(300, barra.WidthRequest);
        Assert.Equal(LayoutAlignment.End, barra.HorizontalOptions.Alignment);
    }

    // ── Textos bindados ─────────────────────────────────────────────────────────
    [Fact]
    public void LoadingText_flui_para_o_overlay()
    {
        var r = new PdfReaderView { LoadingText = "Carregando…" };
        Assert.Equal("Carregando…", Elemento<Label>(r, "LoadingLabel").Text);
    }
}

/// <summary>Resolução do Source no leitor (sem rede e sem plataforma — só os ramos do host).</summary>
public class PdfReaderViewSourceTests
{
    private static T Elemento<T>(PdfReaderView r, string nome) where T : Element
        => r.FindByName<T>(nome) ?? throw new Xunit.Sdk.XunitException($"Elemento '{nome}' não encontrado");

    [Fact]
    public void Url_vai_direto_para_o_viewer_com_nome_e_overlay_de_carga()
    {
        var r = new PdfReaderView();
        r.Source = "https://exemplo.com/docs/arquivo.pdf?token=1";

        Assert.Equal("https://exemplo.com/docs/arquivo.pdf?token=1", r.ViewerControl.Source);
        Assert.Equal("arquivo.pdf", Elemento<Label>(r, "FileNameLabel").Text);   // query removida
        Assert.True(Elemento<Grid>(r, "LoadingOverlay").IsVisible);
    }

    [Fact]
    public void Arquivo_local_absoluto_existente_vai_direto_para_o_viewer()
    {
        var caminho = Path.Combine(Path.GetTempPath(), $"agile-teste-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(caminho, "%PDF-fake");
        try
        {
            var r = new PdfReaderView();
            r.Source = caminho;

            Assert.Equal(caminho, r.ViewerControl.Source);
            Assert.Equal(Path.GetFileName(caminho), Elemento<Label>(r, "FileNameLabel").Text);
        }
        finally
        {
            File.Delete(caminho);
        }
    }

    [Fact]
    public void Caminho_relativo_sem_asset_cai_no_fallback_para_o_viewer()
    {
        // No host não há pacote de app (FileSystem lança) → o catch entrega o valor cru ao
        // viewer, que reportará o erro. Garante que o leitor não explode com asset inexistente.
        var r = new PdfReaderView();
        r.Source = "pasta/apostila.pdf";

        Assert.Equal("pasta/apostila.pdf", r.ViewerControl.Source);
        Assert.Equal("apostila.pdf", Elemento<Label>(r, "FileNameLabel").Text);
    }

    [Fact]
    public void Source_vazio_limpa_o_viewer_e_volta_o_titulo_padrao()
    {
        var r = new PdfReaderView();
        r.Source = "https://exemplo.com/a.pdf";
        r.Source = "";

        Assert.Null(r.ViewerControl.Source);
        Assert.Equal("PDF", Elemento<Label>(r, "FileNameLabel").Text);
    }
}

/// <summary>Reação do leitor aos eventos do viewer (o caminho que os handlers reais disparam).</summary>
public class PdfReaderViewEventosTests
{
    private static T Elemento<T>(PdfReaderView r, string nome) where T : Element
        => r.FindByName<T>(nome) ?? throw new Xunit.Sdk.XunitException($"Elemento '{nome}' não encontrado");

    [Fact]
    public void DocumentLoaded_esconde_o_overlay_e_preenche_stats_e_paginacao()
    {
        var r = new PdfReaderView();
        r.Source = "https://exemplo.com/a.pdf";              // liga o overlay
        PdfDocumentLoadedEventArgs? reexposto = null;
        r.DocumentLoaded += (s, e) => reexposto = e;

        r.ViewerControl.RaiseDocumentLoaded(3);

        Assert.False(Elemento<Grid>(r, "LoadingOverlay").IsVisible);
        Assert.Equal("3 pages", Elemento<Label>(r, "StatsLabel").Text);
        Assert.Equal("1 / 3", Elemento<Label>(r, "PageLabel").Text);
        Assert.Equal(0.3, Elemento<Border>(r, "PrevBtn").Opacity, 3);   // na 1ª página não há "anterior"
        Assert.Equal(1.0, Elemento<Border>(r, "NextBtn").Opacity, 3);
        Assert.Equal(3, reexposto?.PageCount);
    }

    [Fact]
    public void PageChanged_atualiza_o_contador_e_reexpoe_o_evento()
    {
        var r = new PdfReaderView();
        r.ViewerControl.RaiseDocumentLoaded(3);
        PdfPageChangedEventArgs? reexposto = null;
        r.PageChanged += (s, e) => reexposto = e;

        r.ViewerControl.RaisePageChanged(2);                 // última página

        Assert.Equal("3 / 3", Elemento<Label>(r, "PageLabel").Text);
        Assert.Equal(1.0, Elemento<Border>(r, "PrevBtn").Opacity, 3);
        Assert.Equal(0.3, Elemento<Border>(r, "NextBtn").Opacity, 3);
        Assert.Equal(2, reexposto?.Page);
    }

    [Fact]
    public void DocumentLoadFailed_mostra_o_texto_de_falha_e_reexpoe_o_evento()
    {
        var r = new PdfReaderView { LoadFailedText = "Falhou" };
        r.Source = "https://exemplo.com/a.pdf";
        PdfDocumentLoadFailedEventArgs? reexposto = null;
        r.DocumentLoadFailed += (s, e) => reexposto = e;

        r.ViewerControl.RaiseDocumentLoadFailed("404");

        Assert.False(Elemento<Grid>(r, "LoadingOverlay").IsVisible);
        Assert.Equal("Falhou", Elemento<Label>(r, "StatsLabel").Text);
        Assert.Equal("404", reexposto?.Message);
    }

    [Fact]
    public void SearchResultChanged_atualiza_o_contador_da_busca()
    {
        var r = new PdfReaderView();

        r.ViewerControl.RaiseSearchResult(5, 2);

        Assert.Equal("3/5", Elemento<Label>(r, "SearchCountLabel").Text);
        Assert.Equal(1.0, Elemento<Border>(r, "SearchPrevBtn").Opacity, 3);
        Assert.Equal(1.0, Elemento<Border>(r, "SearchNextBtn").Opacity, 3);
    }

    [Fact]
    public void SearchResultChanged_sem_ocorrencias_esvazia_o_contador_com_busca_vazia()
    {
        var r = new PdfReaderView();

        r.ViewerControl.RaiseSearchResult(0, -1);

        Assert.Equal(string.Empty, Elemento<Label>(r, "SearchCountLabel").Text);
        Assert.Equal(0.3, Elemento<Border>(r, "SearchPrevBtn").Opacity, 3);
    }
}

// Regressão (E2E no app, 09/10): com a toolbar colorida o app define IconColor branco. Os botões da barra de
// busca e o de tela cheia ficam sobre fundo branco fixo e seguiam o IconColor — ícones invisíveis.
public class PdfReaderViewIconesSobreSuperficieTests
{
    private static readonly Color CorDeIconeDaSuperficie = Color.FromArgb("#44444A");

    [Theory]
    [InlineData("SearchPrevBtn")]
    [InlineData("SearchNextBtn")]
    [InlineData("SearchCloseBtn")]
    public void Botoes_da_barra_de_busca_nao_seguem_o_IconColor_da_toolbar(string nome)
    {
        var r = new PdfReaderView { IconColor = Colors.White };

        var botao = r.FindByName<Border>(nome);
        Assert.NotNull(botao);
        var icone = Assert.IsType<FontImageSource>(Assert.IsType<Image>(botao!.Content).Source);

        Assert.Equal(CorDeIconeDaSuperficie, icone.Color);
    }

    [Fact]
    public void Glifo_do_botao_de_tela_cheia_nao_segue_o_IconColor_da_toolbar()
    {
        var r = new PdfReaderView { IconColor = Colors.White };

        var glifo = r.FindByName<Label>("FullscreenToggleGlyph");
        Assert.NotNull(glifo);

        Assert.Equal(CorDeIconeDaSuperficie, glifo!.TextColor);
    }
}

// Pedido do E2E no app (09/10): o Voltar do sistema com a busca aberta saía da página; e a barra de busca
// passou a ser uma pílula.
public class PdfReaderViewBarraDeBuscaTests
{
    [Fact]
    public void Voltar_com_a_busca_aberta_fecha_a_busca_e_consome_o_Voltar()
    {
        var r = new PdfReaderView();
        r.OpenSearch();

        Assert.True(r.HandleBackPressed());
        Assert.False(r.FindByName<Border>("SearchBar")!.IsVisible);
    }

    [Fact]
    public void Voltar_sem_busca_aberta_nao_e_consumido()
    {
        var r = new PdfReaderView();

        Assert.False(r.HandleBackPressed());
    }

    [Fact]
    public void Segundo_Voltar_depois_de_fechar_a_busca_segue_para_a_pagina()
    {
        var r = new PdfReaderView();
        r.OpenSearch();
        r.HandleBackPressed();

        Assert.False(r.HandleBackPressed());
    }

    [Fact]
    public void Barra_de_busca_tem_formato_de_pilula()
    {
        var r = new PdfReaderView();
        var toolbar = r.FindByName<Grid>("ToolbarHost")!;
        var barra = r.FindByName<Border>("SearchBar")!;

        var altura = toolbar.HeightRequest - barra.Margin.Top - barra.Margin.Bottom;
        var forma = Assert.IsType<Microsoft.Maui.Controls.Shapes.RoundRectangle>(barra.StrokeShape);

        Assert.Equal(new CornerRadius(altura / 2), forma.CornerRadius);
    }
}

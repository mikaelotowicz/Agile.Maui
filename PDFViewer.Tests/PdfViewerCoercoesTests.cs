using Xunit;

namespace Agile.Maui.PdfTests;

/// <summary>
/// Clamps/coerções de <c>CurrentPage</c> e <c>ZoomFactor</c> e as validações das demais
/// propriedades numéricas. O "documento carregado" é simulado por
/// <see cref="PdfViewer.RaiseDocumentLoaded"/> (interno), que define <c>PageCount</c> como o
/// handler real faz.
///
/// CONTRATOS COBERTOS AQUI (verificados empiricamente):
///  1. validateValue que retorna false NÃO lança no MAUI 10 — o valor inválido é IGNORADO em
///     silêncio e a propriedade mantém o valor anterior.
///  2. BindableObject.CoerceValue(bp) é INERTE no MAUI 10 (não reaplica a coerção ao valor
///     armazenado); por isso a lib reaplica o clamp explicitamente (ReapplyCurrentPageClamp /
///     OnZoomRangeChanged com set direto) quando PageCount ou MinZoom/MaxZoom mudam — e estes
///     testes garantem que essa re-coerção manual FUNCIONA.
/// </summary>
public class PdfViewerCoercoesTests
{
    // ── CurrentPage ───────────────────────────────────────────────────────────────
    [Fact]
    public void CurrentPage_negativo_e_ignorado_em_silencio()
    {
        var v = new PdfViewer { CurrentPage = 3 };
        v.CurrentPage = -1;                 // validateValue false → ignorado, sem exceção
        Assert.Equal(3, v.CurrentPage);
    }

    [Fact]
    public void CurrentPage_sem_documento_nao_tem_teto()
    {
        // Contrato atual: com PageCount 0 o limite superior é desconhecido — o valor fica como
        // pedido e será coagido num PRÓXIMO set depois que o documento carregar.
        var v = new PdfViewer { CurrentPage = 42 };
        Assert.Equal(42, v.CurrentPage);
    }

    [Fact]
    public void CurrentPage_acima_do_total_e_coagido_para_a_ultima_pagina()
    {
        var v = new PdfViewer();
        v.RaiseDocumentLoaded(5);
        v.CurrentPage = 10;
        Assert.Equal(4, v.CurrentPage);
    }

    [Fact]
    public void Carregar_documento_re_coage_pagina_pendente_para_o_range_do_documento()
    {
        // CoerceValue é inerte no MAUI 10, então a lib reaplica o clamp explicitamente
        // quando PageCount muda (ReapplyCurrentPageClamp) — a página pendente volta ao range.
        var v = new PdfViewer { CurrentPage = 42 };
        v.RaiseDocumentLoaded(5);

        Assert.Equal(4, v.CurrentPage);     // re-coerção explícita → clampado a PageCount-1

        v.CurrentPage = 41;
        Assert.Equal(4, v.CurrentPage);     // novo set → clampado a PageCount-1
    }

    [Fact]
    public void Documento_menor_re_coage_a_pagina_atual_automaticamente()
    {
        var v = new PdfViewer();
        v.RaiseDocumentLoaded(10);
        v.CurrentPage = 7;

        v.RaiseDocumentLoaded(3);

        Assert.Equal(2, v.CurrentPage);     // re-coerção explícita ao novo PageCount-1
        v.CurrentPage = 8;
        Assert.Equal(2, v.CurrentPage);     // novo set respeita o documento menor
    }

    // ── ZoomFactor ────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData(100.0, 8.0)]   // acima do MaxZoom → MaxZoom
    [InlineData(0.1, 0.5)]     // abaixo do MinZoom → MinZoom
    [InlineData(2.0, 2.0)]     // dentro do range → inalterado
    public void ZoomFactor_e_coagido_ao_range_no_set(double pedido, double esperado)
    {
        var v = new PdfViewer { ZoomFactor = pedido };
        Assert.Equal(esperado, v.ZoomFactor);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void ZoomFactor_nao_positivo_e_ignorado_em_silencio(double invalido)
    {
        var v = new PdfViewer { ZoomFactor = 2.0 };
        v.ZoomFactor = invalido;            // validateValue false → ignorado
        Assert.Equal(2.0, v.ZoomFactor);
    }

    [Fact]
    public void Aumentar_MinZoom_re_coage_o_zoom_atual_para_o_novo_piso()
    {
        var v = new PdfViewer();            // ZoomFactor 1.0
        v.MinZoom = 2.0;

        Assert.Equal(2.0, v.ZoomFactor);    // re-coerção explícita em OnZoomRangeChanged

        v.ZoomFactor = 1.5;
        Assert.Equal(2.0, v.ZoomFactor);    // novo set → piso MinZoom = 2
    }

    [Fact]
    public void Reduzir_MaxZoom_re_coage_o_zoom_atual_para_o_novo_teto()
    {
        var v = new PdfViewer { ZoomFactor = 5.0 };
        v.MaxZoom = 2.0;

        Assert.Equal(2.0, v.ZoomFactor);    // re-coerção explícita em OnZoomRangeChanged

        v.ZoomFactor = 4.0;
        Assert.Equal(2.0, v.ZoomFactor);    // novo set → teto MaxZoom = 2
    }

    [Fact]
    public void MinZoom_maior_que_MaxZoom_usa_a_defensiva_max_igual_min_em_novos_sets()
    {
        // Coerência defensiva de CoerceZoomFactor: com Min 10 > Max 8, o range vira [10, 10].
        var v = new PdfViewer();
        v.MinZoom = 10.0;
        v.ZoomFactor = 3.0;
        Assert.Equal(10.0, v.ZoomFactor);
    }

    [Fact]
    public void MinZoom_nao_positivo_e_ignorado_em_silencio()
    {
        var v = new PdfViewer();
        v.MinZoom = 0.0;
        Assert.Equal(0.5, v.MinZoom);
    }

    [Fact]
    public void MaxZoom_abaixo_de_1_e_ignorado_em_silencio()
    {
        var v = new PdfViewer();
        v.MaxZoom = 0.9;
        Assert.Equal(8.0, v.MaxZoom);
    }

    // ── Validações das demais propriedades numéricas (inválido = ignorado) ──────
    [Fact]
    public void PageSpacing_negativo_e_ignorado_e_zero_e_aceito()
    {
        var v = new PdfViewer();
        v.PageSpacing = -1.0;
        Assert.Equal(8.0, v.PageSpacing);
        v.PageSpacing = 0.0;
        Assert.Equal(0.0, v.PageSpacing);
    }

    [Fact]
    public void RenderScale_nao_positivo_e_ignorado()
    {
        var v = new PdfViewer();
        v.RenderScale = 0.0;
        Assert.Equal(1.5, v.RenderScale);
    }

    [Fact]
    public void MaxCacheMB_abaixo_de_10_e_ignorado_e_10_e_aceito()
    {
        var v = new PdfViewer();
        v.MaxCacheMB = 9;
        Assert.Equal(200, v.MaxCacheMB);
        v.MaxCacheMB = 10;
        Assert.Equal(10, v.MaxCacheMB);
    }

    [Fact]
    public void Prefetch_negativo_e_ignorado_e_zero_e_aceito()
    {
        var v = new PdfViewer();
        v.PrefetchAbove = -1;
        v.PrefetchBelow = -1;
        Assert.Equal(2, v.PrefetchAbove);
        Assert.Equal(3, v.PrefetchBelow);
        v.PrefetchAbove = 0;
        v.PrefetchBelow = 0;
        Assert.Equal(0, v.PrefetchAbove);
        Assert.Equal(0, v.PrefetchBelow);
    }

    // ── API pública de navegação/zoom ─────────────────────────────────────────────
    [Fact]
    public async Task GoToPageAsync_clampa_aos_limites_do_documento()
    {
        var v = new PdfViewer();
        v.RaiseDocumentLoaded(5);

        await v.GoToPageAsync(99);
        Assert.Equal(4, v.CurrentPage);

        await v.GoToPageAsync(-7);
        Assert.Equal(0, v.CurrentPage);
    }

    [Fact]
    public async Task ZoomInAsync_multiplica_por_1_25_ate_o_MaxZoom()
    {
        var v = new PdfViewer();
        await v.ZoomInAsync();
        Assert.Equal(1.25, v.ZoomFactor);

        v.ZoomFactor = 7.0;
        await v.ZoomInAsync();               // 7 × 1.25 = 8.75 → teto 8
        Assert.Equal(8.0, v.ZoomFactor);
    }

    [Fact]
    public async Task ZoomOutAsync_divide_por_1_25_ate_o_MinZoom()
    {
        var v = new PdfViewer();
        await v.ZoomOutAsync();
        Assert.Equal(0.8, v.ZoomFactor, 10);

        v.ZoomFactor = 0.5;
        await v.ZoomOutAsync();              // 0.5 ÷ 1.25 = 0.4 → piso 0.5
        Assert.Equal(0.5, v.ZoomFactor);
    }

    [Fact]
    public async Task ResetZoomAsync_volta_para_1()
    {
        var v = new PdfViewer { ZoomFactor = 4.0 };
        await v.ResetZoomAsync();
        Assert.Equal(1.0, v.ZoomFactor);
    }

    [Fact]
    public async Task ResetZoomAsync_respeita_MinZoom_maior_que_1()
    {
        // Contrato atual: o reset pede 1.0, mas a coerção do set vence — com MinZoom 2 vira 2.
        var v = new PdfViewer { MinZoom = 2.0 };
        await v.ResetZoomAsync();
        Assert.Equal(2.0, v.ZoomFactor);
    }
}

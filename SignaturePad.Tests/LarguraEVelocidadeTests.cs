using Xunit;

namespace Agile.Maui.SignaturePadTests;

public class LarguraEVelocidadeTests
{
    [Fact]
    public void Primeiro_ponto_usa_a_largura_media()
    {
        var pad = new SignaturePad(); // min 1.0, max 3.5
        pad.Assinar(0, (0, 0), (10, 10));

        var larguras = pad.LargurasDoStroke(0);
        Assert.Equal(2.25f, larguras[0], 3);
    }

    [Fact]
    public void Primeiro_ponto_respeita_min_max_customizados()
    {
        var pad = new SignaturePad { MinStrokeWidth = 2, MaxStrokeWidth = 8 };
        pad.Assinar(0, (0, 0), (10, 10));

        Assert.Equal(5f, pad.LargurasDoStroke(0)[0], 3);
    }

    [Fact]
    public void Movimento_lento_gera_traco_mais_largo_que_movimento_rapido()
    {
        var lentoPad = new SignaturePad();
        lentoPad.OnTouchDown(0, 0, 0f, false, 0);
        lentoPad.OnTouchMove(1, 0, 0f, false, 100); // 0.01 DIP/ms
        lentoPad.OnTouchUp(2, 0, 0f, false, 200);

        var rapidoPad = new SignaturePad();
        rapidoPad.OnTouchDown(0, 0, 0f, false, 0);
        rapidoPad.OnTouchMove(50, 0, 0f, false, 10); // 5 DIP/ms
        rapidoPad.OnTouchUp(100, 0, 0f, false, 20);

        var lento = lentoPad.LargurasDoStroke(0)[1];
        var rapido = rapidoPad.LargurasDoStroke(0)[1];

        Assert.True(lento > rapido, $"lento={lento} deveria ser maior que rapido={rapido}");
    }

    [Fact]
    public void Velocidade_acima_do_limiar_atinge_a_largura_minima()
    {
        var pad = new SignaturePad();
        pad.OnTouchDown(0, 0, 0f, false, 0);
        pad.OnTouchMove(50, 0, 0f, false, 10); // v filtrada = 3.5 >> 1.0 DIP/ms
        pad.OnTouchUp(100, 0, 0f, false, 20);

        Assert.Equal((float)pad.MinStrokeWidth, pad.LargurasDoStroke(0)[1], 3);
    }

    [Fact]
    public void Pressao_alta_gera_traco_mais_largo_que_pressao_baixa()
    {
        var altaPad = new SignaturePad();
        altaPad.OnTouchDown(0, 0, 1f, true, 0);
        altaPad.OnTouchMove(1, 0, 1f, true, 100);
        altaPad.OnTouchUp(2, 0, 1f, true, 200);

        var baixaPad = new SignaturePad();
        baixaPad.OnTouchDown(0, 0, 0.2f, true, 0);
        baixaPad.OnTouchMove(1, 0, 0.2f, true, 100);
        baixaPad.OnTouchUp(2, 0, 0.2f, true, 200);

        var alta = altaPad.LargurasDoStroke(0)[1];
        var baixa = baixaPad.LargurasDoStroke(0)[1];

        Assert.True(alta > baixa, $"alta={alta} deveria ser maior que baixa={baixa}");
    }

    [Fact]
    public void Pressao_zero_com_suporte_cai_no_caminho_de_velocidade()
    {
        // pressure == 0 com suporte não pode zerar a largura: usa só a velocidade.
        var comZero = new SignaturePad();
        comZero.OnTouchDown(0, 0, 0f, true, 0);
        comZero.OnTouchMove(1, 0, 0f, true, 100);
        comZero.OnTouchUp(2, 0, 0f, true, 200);

        var semSuporte = new SignaturePad();
        semSuporte.OnTouchDown(0, 0, 0f, false, 0);
        semSuporte.OnTouchMove(1, 0, 0f, false, 100);
        semSuporte.OnTouchUp(2, 0, 0f, false, 200);

        Assert.Equal(semSuporte.LargurasDoStroke(0)[1], comZero.LargurasDoStroke(0)[1], 3);
    }

    [Fact]
    public void Todas_as_larguras_ficam_entre_min_e_max()
    {
        var pad = new SignaturePad();
        pad.OnTouchDown(0, 0, 0.9f, true, 0);

        // Zigue-zague determinístico com velocidades e pressões variadas.
        var t = 0.0;
        for (var i = 1; i <= 30; i++)
        {
            t += i % 3 == 0 ? 2 : 40;
            var x = i * ((i % 2 == 0) ? 12f : 0.5f);
            var pressao = (i % 5) / 4f;
            pad.OnTouchMove(x, i, pressao, i % 2 == 0, t);
        }

        pad.OnTouchUp(400, 31, 1f, true, t + 10);

        var min = (float)pad.MinStrokeWidth;
        var max = (float)pad.MaxStrokeWidth;
        Assert.All(pad.LargurasDoStroke(0), w => Assert.InRange(w, min, max));
    }

    [Fact]
    public void Larguras_recalculadas_no_load_tambem_ficam_entre_min_e_max()
    {
        var origem = new SignaturePad();
        origem.OnTouchDown(0, 0, 0.8f, true, 0);
        origem.OnTouchMove(30, 10, 0.3f, true, 15);
        origem.OnTouchMove(31, 11, 0.9f, true, 120);
        origem.OnTouchUp(60, 20, 0.1f, true, 130);

        var pad = new SignaturePad();
        pad.LoadSignatureJson(origem.GetSignatureJson());

        var min = (float)pad.MinStrokeWidth;
        var max = (float)pad.MaxStrokeWidth;
        Assert.All(pad.LargurasDoStroke(0), w => Assert.InRange(w, min, max));
    }

    [Fact]
    public void Ha_uma_largura_por_ponto_capturado()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 5), (20, 10), (30, 15), (40, 20));

        var stroke = pad.AllStrokesForRender[0];
        Assert.Equal(stroke.Points.Count, stroke.Widths.Count);
    }
}

using Xunit;

namespace Agile.Maui.SignaturePadTests;

public class TimestampTests
{
    [Fact]
    public void Timestamps_sao_normalizados_para_comecar_em_zero()
    {
        var pad = new SignaturePad();

        // Base arbitrária da plataforma (ex.: uptime) não pode vazar para os dados.
        pad.OnTouchDown(0, 0, 0f, false, 123_456_789);
        pad.OnTouchMove(5, 5, 0f, false, 123_456_839);
        pad.OnTouchUp(10, 10, 0f, false, 123_456_889);

        var pontos = pad.GetStrokes()[0].Points;
        Assert.Equal(0, pontos[0].TimestampMs);
        Assert.Equal(50, pontos[1].TimestampMs);
        Assert.Equal(100, pontos[2].TimestampMs);
    }

    [Fact]
    public void Segundo_stroke_continua_a_base_da_sessao()
    {
        var pad = new SignaturePad();

        pad.Assinar(1000, (0, 0), (10, 10));   // ts 0..10
        pad.Assinar(5000, (20, 20), (30, 30)); // mesmo relógio, 4s depois

        var strokes = pad.GetStrokes();
        Assert.Equal(0, strokes[0].Points[0].TimestampMs);
        Assert.Equal(4000, strokes[1].Points[0].TimestampMs);
        Assert.True(pad.GetSignatureData().TotalDurationMs >= 0);
    }

    [Fact]
    public void Novos_strokes_continuam_apos_o_maior_timestamp_carregado_por_LoadSignatureJson()
    {
        // Regressão: a base de tempo reiniciava em zero após o load e a linha do
        // tempo colidia com os strokes restaurados (TotalDurationMs negativo).
        var origem = new SignaturePad();
        origem.OnTouchDown(0, 0, 0f, false, 1000);
        origem.OnTouchMove(5, 5, 0f, false, 1050);
        origem.OnTouchUp(10, 10, 0f, false, 1100); // ts carregados: 0..100

        var pad = new SignaturePad();
        pad.LoadSignatureJson(origem.GetSignatureJson());

        // Novo stroke com base de relógio completamente diferente.
        pad.OnTouchDown(20, 20, 0f, false, 987_654_321);
        pad.OnTouchUp(30, 30, 0f, false, 987_654_371);

        var strokes = pad.GetStrokes();
        var ultimoCarregado = strokes[0].Points[^1].TimestampMs;
        var primeiroNovo = strokes[1].Points[0].TimestampMs;

        Assert.Equal(100, ultimoCarregado);
        Assert.True(primeiroNovo >= ultimoCarregado);
        Assert.Equal(150, strokes[1].Points[^1].TimestampMs);
        Assert.True(pad.GetSignatureData().TotalDurationMs >= 0);
    }

    [Fact]
    public void Novos_strokes_continuam_apos_o_maior_timestamp_carregado_por_LoadStrokes()
    {
        var origem = new SignaturePad();
        origem.Assinar(0, (0, 0), (10, 10), (20, 20)); // ts 0..20

        var pad = new SignaturePad();
        pad.LoadStrokes(origem.GetStrokes());

        pad.OnTouchDown(50, 50, 0f, false, 777_000);
        pad.OnTouchUp(60, 60, 0f, false, 777_010);

        var strokes = pad.GetStrokes();
        Assert.Equal(20, strokes[1].Points[0].TimestampMs);
        Assert.Equal(30, strokes[1].Points[^1].TimestampMs);
        Assert.True(pad.GetSignatureData().TotalDurationMs >= 0);
    }

    [Fact]
    public void Linha_do_tempo_e_monotonica_apos_load_e_novas_assinaturas()
    {
        var origem = new SignaturePad();
        origem.Assinar(100, (0, 0), (10, 10));

        var pad = new SignaturePad();
        pad.LoadSignatureJson(origem.GetSignatureJson());
        pad.Assinar(900_000, (20, 20), (30, 30));
        pad.Assinar(905_000, (40, 40), (50, 50));

        double anterior = double.MinValue;
        foreach (var stroke in pad.GetStrokes())
        {
            foreach (var ponto in stroke.Points)
            {
                Assert.True(ponto.TimestampMs >= anterior,
                    $"Timestamp {ponto.TimestampMs} regrediu (anterior {anterior}).");
                anterior = ponto.TimestampMs;
            }
        }
    }

    [Fact]
    public void Clear_reseta_a_base_de_tempo()
    {
        var pad = new SignaturePad();
        pad.Assinar(10_000, (0, 0), (10, 10));

        pad.Clear();
        pad.Assinar(99_000, (20, 20), (30, 30));

        Assert.Equal(0, pad.GetStrokes()[0].Points[0].TimestampMs);
    }

    [Fact]
    public void DurationMs_do_stroke_usa_primeiro_e_ultimo_ponto()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10), (20, 20), (30, 30)); // 4 pontos, 10ms entre cada

        var stroke = pad.GetStrokes()[0];
        Assert.Equal(30, stroke.DurationMs);
    }
}

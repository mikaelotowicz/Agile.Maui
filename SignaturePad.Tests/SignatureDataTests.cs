using Microsoft.Maui.Graphics;
using Xunit;

namespace Agile.Maui.SignaturePadTests;

public class SignatureDataTests
{
    [Fact]
    public void TotalPoints_soma_os_pontos_de_todos_os_strokes()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10), (20, 20)); // 3 pontos
        pad.Assinar(100, (30, 30), (40, 40));       // 2 pontos

        var data = pad.GetSignatureData();

        Assert.Equal(2, data.Strokes.Count);
        Assert.Equal(5, data.TotalPoints);
    }

    [Fact]
    public void Pad_vazio_tem_data_vazia()
    {
        var pad = new SignaturePad();

        var data = pad.GetSignatureData();

        Assert.Empty(data.Strokes);
        Assert.Equal(0, data.TotalPoints);
        Assert.Equal(0, data.TotalDurationMs);
        Assert.False(data.HasRealPressure);
    }

    [Fact]
    public void CanvasSize_nunca_e_negativo_sem_layout()
    {
        // Regressão: Width/Height valem -1 antes do layout e o snapshot gravava (-1,-1).
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10));

        var data = pad.GetSignatureData();

        Assert.True(data.CanvasSize.Width >= 0);
        Assert.True(data.CanvasSize.Height >= 0);
    }

    [Fact]
    public void HasRealPressure_e_false_quando_nenhum_ponto_tem_pressao_de_hardware()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10)); // helper envia pressureSupported: false

        Assert.False(pad.GetSignatureData().HasRealPressure);
    }

    [Fact]
    public void HasRealPressure_e_true_quando_algum_ponto_tem_pressao_de_hardware()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10));

        pad.OnTouchDown(20, 20, 0.5f, true, 100);
        pad.OnTouchUp(30, 30, 0.5f, true, 110);

        Assert.True(pad.GetSignatureData().HasRealPressure);
    }

    [Fact]
    public void TotalDurationMs_cobre_do_primeiro_ao_ultimo_ponto_da_sessao()
    {
        var pad = new SignaturePad();
        pad.Assinar(1000, (0, 0), (10, 10)); // ts 0..10
        pad.Assinar(2000, (20, 20), (30, 30)); // ts 1000..1010

        Assert.Equal(1010, pad.GetSignatureData().TotalDurationMs);
    }

    [Fact]
    public void GetStrokes_e_um_snapshot_imune_a_Clear()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10));

        var snapshot = pad.GetStrokes();
        pad.Clear();

        Assert.Single(snapshot);
        Assert.Equal(2, snapshot[0].Points.Count);
        Assert.Empty(pad.GetStrokes());
    }

    [Fact]
    public void GetSignatureData_nao_inclui_o_stroke_ativo()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10));
        pad.OnTouchDown(50, 50, 0f, false, 500);

        var data = pad.GetSignatureData();

        Assert.Single(data.Strokes);
    }

    [Fact]
    public void SignatureStroke_com_menos_de_dois_pontos_tem_duracao_zero()
    {
        var umPonto = new SignatureStroke(
            new[] { new SignaturePoint(1, 2, 500, 0f, false) }, Colors.Black);
        var semPontos = new SignatureStroke(Array.Empty<SignaturePoint>(), Colors.Black);

        Assert.Equal(0, umPonto.DurationMs);
        Assert.Equal(0, semPontos.DurationMs);
    }

    [Fact]
    public void SignatureStroke_com_points_null_vira_lista_vazia()
    {
        var stroke = new SignatureStroke(null!, Colors.Black);

        Assert.Empty(stroke.Points);
    }

    [Fact]
    public void SignatureData_com_strokes_null_vira_lista_vazia()
    {
        var data = new SignatureData(null!, new Size(10, 10));

        Assert.Empty(data.Strokes);
        Assert.Equal(0, data.TotalPoints);
    }

    [Fact]
    public void SignatureData_tolera_strokes_sem_pontos_no_TotalDurationMs()
    {
        var vazio = new SignatureStroke(Array.Empty<SignaturePoint>(), Colors.Black);
        var data = new SignatureData(new[] { vazio }, new Size(10, 10));

        Assert.Equal(0, data.TotalDurationMs);
    }
}

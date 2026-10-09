using Microsoft.Maui.Graphics;
using Xunit;

namespace Agile.Maui.SignaturePadTests;

public class CicloDeStrokeTests
{
    [Fact]
    public void Down_moves_up_produz_um_stroke_com_todos_os_pontos()
    {
        var pad = new SignaturePad();

        pad.Assinar(1000, (0, 0), (10, 5), (20, 10), (30, 15), (40, 20), (50, 25), (60, 30));

        var strokes = pad.GetStrokes();
        Assert.Single(strokes);
        Assert.Equal(7, strokes[0].Points.Count);
        Assert.False(pad.IsEmpty);
    }

    [Fact]
    public void Tap_produz_stroke_valido()
    {
        var pad = new SignaturePad();

        pad.Assinar(1000, (42, 24));

        var strokes = pad.GetStrokes();
        Assert.Single(strokes);
        // Down e Up amostram cada um, então o tap tem 2 pontos no mesmo lugar.
        Assert.Equal(2, strokes[0].Points.Count);
        Assert.All(strokes[0].Points, p =>
        {
            Assert.Equal(42f, p.X);
            Assert.Equal(24f, p.Y);
        });
        Assert.False(pad.IsEmpty);
    }

    [Fact]
    public void Cancel_descarta_stroke_em_andamento()
    {
        var pad = new SignaturePad();

        pad.OnTouchDown(0, 0, 0f, false, 0);
        pad.OnTouchMove(10, 10, 0f, false, 10);
        pad.OnTouchCancel();

        Assert.Empty(pad.GetStrokes());
        Assert.True(pad.IsEmpty);
        Assert.False(pad.HasActiveStroke);
    }

    [Fact]
    public void Cancel_sem_stroke_ativo_e_noop()
    {
        var pad = new SignaturePad();

        pad.OnTouchCancel();

        Assert.True(pad.IsEmpty);
    }

    [Fact]
    public void Move_sem_down_e_ignorado()
    {
        var pad = new SignaturePad();

        pad.OnTouchMove(10, 10, 0f, false, 10);

        Assert.Empty(pad.GetStrokes());
        Assert.False(pad.HasActiveStroke);
    }

    [Fact]
    public void Up_sem_down_e_ignorado()
    {
        var pad = new SignaturePad();

        pad.OnTouchUp(10, 10, 0f, false, 10);

        Assert.Empty(pad.GetStrokes());
        Assert.True(pad.IsEmpty);
    }

    [Fact]
    public void HasActiveStroke_verdadeiro_durante_e_falso_depois()
    {
        var pad = new SignaturePad();

        pad.OnTouchDown(0, 0, 0f, false, 0);
        Assert.True(pad.HasActiveStroke);

        pad.OnTouchMove(5, 5, 0f, false, 10);
        Assert.True(pad.HasActiveStroke);

        pad.OnTouchUp(10, 10, 0f, false, 20);
        Assert.False(pad.HasActiveStroke);
    }

    [Fact]
    public void AllStrokesForRender_inclui_o_stroke_ativo()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10));

        pad.OnTouchDown(50, 50, 0f, false, 1000);

        Assert.Equal(2, pad.AllStrokesForRender.Count);
        Assert.Single(pad.GetStrokes()); // só o completado

        pad.OnTouchUp(60, 60, 0f, false, 1010);
        Assert.Equal(2, pad.GetStrokes().Count);
    }

    [Fact]
    public void Dois_strokes_sequenciais_ficam_separados()
    {
        var pad = new SignaturePad();

        pad.Assinar(0, (0, 0), (10, 10), (20, 20));
        pad.Assinar(1000, (100, 0), (110, 10));

        var strokes = pad.GetStrokes();
        Assert.Equal(2, strokes.Count);
        Assert.Equal(3, strokes[0].Points.Count);
        Assert.Equal(2, strokes[1].Points.Count);
    }

    [Fact]
    public void Pressao_negativa_e_armazenada_como_zero()
    {
        var pad = new SignaturePad();

        pad.OnTouchDown(0, 0, -0.5f, true, 0);
        pad.OnTouchUp(10, 10, -1f, true, 10);

        var pontos = pad.GetStrokes()[0].Points;
        Assert.All(pontos, p => Assert.Equal(0f, p.Pressure));
    }

    [Fact]
    public void Pressao_acima_de_um_e_armazenada_como_um()
    {
        var pad = new SignaturePad();

        pad.OnTouchDown(0, 0, 3f, true, 0);
        pad.OnTouchUp(10, 10, 1.5f, true, 10);

        var pontos = pad.GetStrokes()[0].Points;
        Assert.All(pontos, p => Assert.Equal(1f, p.Pressure));
    }

    [Fact]
    public void Pressao_sem_suporte_vira_proxy_derivado_da_largura_entre_zero_e_um()
    {
        var pad = new SignaturePad();

        pad.Assinar(0, (0, 0), (10, 5), (20, 10), (30, 15));

        var pontos = pad.GetStrokes()[0].Points;
        Assert.All(pontos, p =>
        {
            Assert.False(p.PressureSupported);
            Assert.InRange(p.Pressure, 0f, 1f);
        });

        // Primeiro ponto tem largura média (min+max)/2 => proxy 0.5.
        Assert.Equal(0.5f, pontos[0].Pressure, 3);
    }

    [Fact]
    public void PressureSupported_e_registrado_por_ponto()
    {
        var pad = new SignaturePad();

        pad.OnTouchDown(0, 0, 0.8f, true, 0);
        pad.OnTouchMove(5, 5, 0f, false, 10);
        pad.OnTouchUp(10, 10, 0.6f, true, 20);

        var pontos = pad.GetStrokes()[0].Points;
        Assert.True(pontos[0].PressureSupported);
        Assert.False(pontos[1].PressureSupported);
        Assert.True(pontos[2].PressureSupported);
    }

    [Fact]
    public void Cor_do_stroke_captura_StrokeColor_no_momento_do_down()
    {
        var pad = new SignaturePad { StrokeColor = Colors.Red };

        pad.OnTouchDown(0, 0, 0f, false, 0);
        pad.StrokeColor = Colors.Blue; // troca no meio não afeta o stroke em andamento
        pad.OnTouchUp(10, 10, 0f, false, 10);

        pad.Assinar(1000, (0, 0), (5, 5));

        var strokes = pad.GetStrokes();
        Assert.Equal(Colors.Red, strokes[0].Color);
        Assert.Equal(Colors.Blue, strokes[1].Color);
    }

    [Fact]
    public void Coordenadas_sao_preservadas_exatamente()
    {
        var pad = new SignaturePad();

        pad.Assinar(0, (10.5f, 20.25f), (30.75f, 40.125f));

        var pontos = pad.GetStrokes()[0].Points;
        Assert.Equal(10.5f, pontos[0].X);
        Assert.Equal(20.25f, pontos[0].Y);
        Assert.Equal(30.75f, pontos[1].X);
        Assert.Equal(40.125f, pontos[1].Y);
    }
}

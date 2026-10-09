using Microsoft.Maui.Graphics;
using Xunit;

namespace Agile.Maui.SignaturePadTests;

public class DrawableTests
{
    private static RenderStroke Stroke(Color cor, params (float X, float Y, float W)[] pontos)
    {
        var stroke = new RenderStroke(cor);
        var t = 0.0;
        foreach (var p in pontos)
        {
            stroke.Points.Add(new SignaturePoint(p.X, p.Y, t, 0f, false));
            stroke.Widths.Add(p.W);
            t += 10;
        }

        return stroke;
    }

    [Fact]
    public void Stroke_vazio_nao_desenha_nada()
    {
        var canvas = new FakeCanvas();

        SignaturePadDrawable.DrawStrokes(canvas, new[] { Stroke(Colors.Black) }, null);

        Assert.Empty(canvas.Linhas);
        Assert.Empty(canvas.ElipsesPreenchidas);
        Assert.Empty(canvas.CaminhosDesenhados);
    }

    [Fact]
    public void Lista_vazia_de_strokes_nao_desenha_nada()
    {
        var canvas = new FakeCanvas();

        SignaturePadDrawable.DrawStrokes(canvas, Array.Empty<RenderStroke>(), null);

        Assert.DoesNotContain(canvas.Chamadas, c => c.StartsWith("Draw") || c.StartsWith("Fill"));
    }

    [Fact]
    public void Um_ponto_desenha_um_circulo_preenchido()
    {
        var canvas = new FakeCanvas();

        SignaturePadDrawable.DrawStrokes(canvas, new[] { Stroke(Colors.Black, (10, 20, 2f)) }, null);

        // FillCircle(10, 20, r=1) chega como FillEllipse(cx-r, cy-r, 2r, 2r).
        var elipse = Assert.Single(canvas.ElipsesPreenchidas);
        Assert.Equal(9f, elipse.X);
        Assert.Equal(19f, elipse.Y);
        Assert.Equal(2f, elipse.W);
        Assert.Equal(2f, elipse.H);
        Assert.Empty(canvas.Linhas);
        Assert.Empty(canvas.CaminhosDesenhados);
    }

    [Fact]
    public void Um_ponto_com_largura_minuscula_usa_o_piso_de_meio_dip()
    {
        var canvas = new FakeCanvas();

        SignaturePadDrawable.DrawStrokes(canvas, new[] { Stroke(Colors.Black, (10, 20, 0.1f)) }, null);

        // Largura vira max(0.1, 0.5) = 0.5 => raio 0.25.
        var elipse = Assert.Single(canvas.ElipsesPreenchidas);
        Assert.Equal(9.75f, elipse.X, 3);
        Assert.Equal(19.75f, elipse.Y, 3);
        Assert.Equal(0.5f, elipse.W, 3);
    }

    [Fact]
    public void Dois_pontos_desenham_uma_linha_com_largura_media()
    {
        var canvas = new FakeCanvas();

        SignaturePadDrawable.DrawStrokes(canvas,
            new[] { Stroke(Colors.Black, (0, 0, 1f), (10, 0, 3f)) }, null);

        var linha = Assert.Single(canvas.Linhas);
        Assert.Equal((0f, 0f, 10f, 0f), (linha.X1, linha.Y1, linha.X2, linha.Y2));
        Assert.Equal(2f, linha.StrokeSize); // média de 1 e 3
        Assert.Empty(canvas.CaminhosDesenhados);
    }

    [Fact]
    public void Tres_pontos_geram_caps_de_linha_e_um_quad_entre_midpoints()
    {
        var canvas = new FakeCanvas();

        SignaturePadDrawable.DrawStrokes(canvas,
            new[] { Stroke(Colors.Black, (0, 0, 2f), (10, 0, 2f), (20, 0, 2f)) }, null);

        // Cap inicial: p0 -> mid(p0,p1); cap final: mid(p1,p2) -> p2.
        Assert.Equal(2, canvas.Linhas.Count);
        Assert.Equal((0f, 0f, 5f, 0f), (canvas.Linhas[0].X1, canvas.Linhas[0].Y1, canvas.Linhas[0].X2, canvas.Linhas[0].Y2));
        Assert.Equal((15f, 0f, 20f, 0f), (canvas.Linhas[1].X1, canvas.Linhas[1].Y1, canvas.Linhas[1].X2, canvas.Linhas[1].Y2));

        // Trecho do meio: 1 quadrática (MoveTo + QuadTo = 3 pontos no PathF).
        var caminho = Assert.Single(canvas.CaminhosDesenhados);
        Assert.Equal(3, caminho.Caminho.Count);
    }

    [Fact]
    public void Cinco_pontos_geram_um_quad_por_ponto_intermediario()
    {
        var canvas = new FakeCanvas();

        SignaturePadDrawable.DrawStrokes(canvas, new[]
        {
            Stroke(Colors.Black, (0, 0, 2f), (10, 5, 2f), (20, 0, 2f), (30, 5, 2f), (40, 0, 2f)),
        }, null);

        Assert.Equal(3, canvas.CaminhosDesenhados.Count); // n-2 quads
        Assert.Equal(2, canvas.Linhas.Count);             // caps inicial e final
    }

    [Fact]
    public void Largura_por_amostra_e_aplicada_em_cada_segmento()
    {
        var canvas = new FakeCanvas();

        SignaturePadDrawable.DrawStrokes(canvas, new[]
        {
            Stroke(Colors.Black, (0, 0, 1f), (10, 0, 2f), (20, 0, 3f)),
        }, null);

        Assert.Equal(1f, canvas.Linhas[0].StrokeSize);                 // cap inicial usa widths[0]
        Assert.Equal(2f, canvas.CaminhosDesenhados[0].StrokeSize);     // quad usa widths[1]
        Assert.Equal(3f, canvas.Linhas[1].StrokeSize);                 // cap final usa widths[^1]
    }

    [Fact]
    public void Override_de_cor_substitui_a_cor_do_stroke()
    {
        var canvas = new FakeCanvas();

        SignaturePadDrawable.DrawStrokes(canvas,
            new[] { Stroke(Colors.Red, (0, 0, 2f), (10, 0, 2f)) }, Colors.Blue);

        Assert.DoesNotContain(Colors.Red, canvas.CoresDeTraco);
        Assert.Contains(Colors.Blue, canvas.CoresDeTraco);
    }

    [Fact]
    public void Sem_override_usa_a_cor_de_cada_stroke()
    {
        var canvas = new FakeCanvas();

        SignaturePadDrawable.DrawStrokes(canvas, new[]
        {
            Stroke(Colors.Red, (0, 0, 2f), (10, 0, 2f)),
            Stroke(Colors.Green, (20, 0, 2f), (30, 0, 2f)),
        }, null);

        Assert.Equal(new[] { Colors.Red, Colors.Green }, canvas.CoresDeTraco);
    }

    [Fact]
    public void Configura_caps_e_joins_redondos_com_antialias()
    {
        var canvas = new FakeCanvas();

        SignaturePadDrawable.DrawStrokes(canvas,
            new[] { Stroke(Colors.Black, (0, 0, 2f), (10, 0, 2f)) }, null);

        Assert.Equal(LineCap.Round, canvas.StrokeLineCapAtual);
        Assert.Equal(LineJoin.Round, canvas.StrokeLineJoinAtual);
        Assert.True(canvas.AntialiasAtual);
    }

    [Fact]
    public void Draw_desenha_guias_quando_o_pad_esta_vazio()
    {
        var pad = new SignaturePad { ShowSignatureLine = true, PromptText = "Assine aqui" };
        var drawable = (SignaturePadDrawable)pad.Drawable;
        var canvas = new FakeCanvas();

        drawable.Draw(canvas, new RectF(0, 0, 300, 150));

        Assert.Single(canvas.Linhas);          // linha-guia
        Assert.Contains("X", canvas.Textos);   // marca do lado esquerdo
        Assert.Contains("Assine aqui", canvas.Textos);
    }

    [Fact]
    public void Draw_sem_guias_configuradas_nao_desenha_nada_no_pad_vazio()
    {
        var pad = new SignaturePad();
        var drawable = (SignaturePadDrawable)pad.Drawable;
        var canvas = new FakeCanvas();

        drawable.Draw(canvas, new RectF(0, 0, 300, 150));

        Assert.Empty(canvas.Linhas);
        Assert.Empty(canvas.Textos);
        Assert.Empty(canvas.ElipsesPreenchidas);
    }

    [Fact]
    public void Draw_oculta_as_guias_durante_o_stroke_ativo()
    {
        var pad = new SignaturePad { ShowSignatureLine = true, PromptText = "Assine aqui" };
        pad.OnTouchDown(10, 10, 0f, false, 0);

        var drawable = (SignaturePadDrawable)pad.Drawable;
        var canvas = new FakeCanvas();
        drawable.Draw(canvas, new RectF(0, 0, 300, 150));

        Assert.Empty(canvas.Textos); // guias somem assim que o traço começa
        Assert.Single(canvas.ElipsesPreenchidas); // o ponto do down é desenhado
    }

    [Fact]
    public void Draw_oculta_as_guias_depois_de_assinado()
    {
        var pad = new SignaturePad { ShowSignatureLine = true, PromptText = "Assine aqui" };
        pad.Assinar(0, (0, 0), (10, 10));

        var drawable = (SignaturePadDrawable)pad.Drawable;
        var canvas = new FakeCanvas();
        drawable.Draw(canvas, new RectF(0, 0, 300, 150));

        Assert.Empty(canvas.Textos);
        Assert.Single(canvas.Linhas); // apenas o stroke (2 pontos = linha)
    }

    [Fact]
    public void Draw_desenha_o_stroke_ativo_junto_com_os_completados()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10));      // completado: linha
        pad.OnTouchDown(50, 50, 0f, false, 100); // ativo: ponto

        var drawable = (SignaturePadDrawable)pad.Drawable;
        var canvas = new FakeCanvas();
        drawable.Draw(canvas, new RectF(0, 0, 300, 150));

        Assert.Single(canvas.Linhas);
        Assert.Single(canvas.ElipsesPreenchidas);
    }
}

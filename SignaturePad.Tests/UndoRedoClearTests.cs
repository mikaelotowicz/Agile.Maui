using Xunit;

namespace Agile.Maui.SignaturePadTests;

public class UndoRedoClearTests
{
    [Fact]
    public void Clear_remove_todos_os_strokes_e_dispara_Cleared()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10));
        pad.Assinar(100, (20, 20), (30, 30));

        var clearedDisparado = 0;
        pad.Cleared += (_, _) => clearedDisparado++;

        pad.Clear();

        Assert.True(pad.IsEmpty);
        Assert.Empty(pad.GetStrokes());
        Assert.Equal(1, clearedDisparado);
    }

    [Fact]
    public void Clear_durante_stroke_ativo_descarta_o_stroke_e_ignora_o_resto_do_gesto()
    {
        var pad = new SignaturePad();
        var strokeCompletado = 0;
        pad.StrokeCompleted += (_, _) => strokeCompletado++;

        pad.OnTouchDown(0, 0, 0f, false, 0);
        pad.OnTouchMove(10, 10, 0f, false, 10);

        pad.Clear();

        Assert.False(pad.HasActiveStroke);
        Assert.True(pad.IsEmpty);

        // O resto do gesto (moves/up do dedo ainda encostado) não pode criar stroke.
        pad.OnTouchMove(20, 20, 0f, false, 20);
        pad.OnTouchUp(30, 30, 0f, false, 30);

        Assert.True(pad.IsEmpty);
        Assert.Empty(pad.GetStrokes());
        Assert.Equal(0, strokeCompletado);
    }

    [Fact]
    public void Undo_remove_o_ultimo_stroke()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10));
        pad.Assinar(100, (20, 20), (30, 30));

        pad.Undo();

        var strokes = pad.GetStrokes();
        Assert.Single(strokes);
        Assert.Equal(0f, strokes[0].Points[0].X);
        Assert.False(pad.IsEmpty);
    }

    [Fact]
    public void Undo_ate_vazio_e_undo_extra_e_noop()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10));
        pad.Assinar(100, (20, 20), (30, 30));

        pad.Undo();
        pad.Undo();
        Assert.True(pad.IsEmpty);
        Assert.Empty(pad.GetStrokes());

        pad.Undo(); // não lança e não muda nada
        Assert.True(pad.IsEmpty);
    }

    [Fact]
    public void Redo_restaura_na_ordem_inversa_do_undo()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10));
        pad.Assinar(100, (20, 20), (30, 30));

        pad.Undo();
        pad.Undo();
        pad.Redo();

        var strokes = pad.GetStrokes();
        Assert.Single(strokes);
        Assert.Equal(0f, strokes[0].Points[0].X); // o primeiro desfeito por último volta primeiro

        pad.Redo();
        Assert.Equal(2, pad.GetStrokes().Count);
        Assert.False(pad.IsEmpty);
    }

    [Fact]
    public void Redo_sem_undo_e_noop()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10));

        pad.Redo();

        Assert.Single(pad.GetStrokes());
    }

    [Fact]
    public void Novo_stroke_invalida_o_redo()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10));
        pad.Assinar(100, (20, 20), (30, 30));

        pad.Undo();
        pad.Assinar(200, (50, 50), (60, 60)); // down limpa a pilha de redo

        pad.Redo(); // noop

        var strokes = pad.GetStrokes();
        Assert.Equal(2, strokes.Count);
        Assert.Equal(50f, strokes[1].Points[0].X);
    }

    [Fact]
    public void Undo_durante_stroke_ativo_e_noop()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10));

        pad.OnTouchDown(20, 20, 0f, false, 100);
        pad.Undo();

        Assert.Single(pad.GetStrokes()); // nada removido
        Assert.True(pad.HasActiveStroke);

        pad.OnTouchUp(30, 30, 0f, false, 110);
        Assert.Equal(2, pad.GetStrokes().Count);
    }

    [Fact]
    public void Redo_durante_stroke_ativo_e_noop()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10));
        pad.Undo();

        pad.OnTouchDown(20, 20, 0f, false, 100);
        // O down já limpou o redo; o Redo com stroke ativo também é guardado.
        pad.Redo();

        Assert.Empty(pad.GetStrokes());
        Assert.True(pad.HasActiveStroke);
    }

    [Fact]
    public void Clear_tambem_limpa_as_pilhas_de_undo_e_redo()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10));
        pad.Undo();

        pad.Clear();
        pad.Redo(); // pilha de redo foi limpa

        Assert.True(pad.IsEmpty);
        Assert.Empty(pad.GetStrokes());
    }
}

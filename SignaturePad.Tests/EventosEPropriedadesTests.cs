using System.Windows.Input;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Agile.Maui.SignaturePadTests;

public class EventosEPropriedadesTests
{
    private sealed class ComandoFake : ICommand
    {
        public List<object?> Execucoes { get; } = new();
        public bool PodeExecutar { get; set; } = true;

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => PodeExecutar;
        public void Execute(object? parameter) => Execucoes.Add(parameter);
    }

    [Fact]
    public void StrokeCompleted_dispara_com_o_stroke_certo()
    {
        var pad = new SignaturePad();
        StrokeCompletedEventArgs? args = null;
        pad.StrokeCompleted += (_, e) => args = e;

        pad.Assinar(0, (1, 2), (3, 4), (5, 6));

        Assert.NotNull(args);
        Assert.Equal(3, args!.Stroke.Points.Count);
        Assert.Equal(1f, args.Stroke.Points[0].X);
        Assert.Equal(6f, args.Stroke.Points[^1].Y);
        Assert.False(args.IsEmpty);
    }

    [Fact]
    public void StrokeCompleted_dispara_uma_vez_por_stroke()
    {
        var pad = new SignaturePad();
        var disparos = 0;
        pad.StrokeCompleted += (_, _) => disparos++;

        pad.Assinar(0, (0, 0), (10, 10));
        pad.Assinar(100, (20, 20), (30, 30));

        Assert.Equal(2, disparos);
    }

    [Fact]
    public void StrokeCompletedCommand_executa_com_os_mesmos_args()
    {
        var comando = new ComandoFake();
        var pad = new SignaturePad { StrokeCompletedCommand = comando };

        pad.Assinar(0, (0, 0), (10, 10));

        var arg = Assert.Single(comando.Execucoes);
        var args = Assert.IsType<StrokeCompletedEventArgs>(arg);
        Assert.Equal(2, args.Stroke.Points.Count);
    }

    [Fact]
    public void StrokeCompletedCommand_nao_executa_quando_CanExecute_e_false()
    {
        var comando = new ComandoFake { PodeExecutar = false };
        var pad = new SignaturePad { StrokeCompletedCommand = comando };

        pad.Assinar(0, (0, 0), (10, 10));

        Assert.Empty(comando.Execucoes);
    }

    [Fact]
    public void Cancel_nao_dispara_StrokeCompleted()
    {
        var pad = new SignaturePad();
        var disparos = 0;
        pad.StrokeCompleted += (_, _) => disparos++;

        pad.OnTouchDown(0, 0, 0f, false, 0);
        pad.OnTouchCancel();

        Assert.Equal(0, disparos);
    }

    [Fact]
    public void IsEmpty_reflete_o_ciclo_completo()
    {
        var pad = new SignaturePad();
        Assert.True(pad.IsEmpty);

        pad.OnTouchDown(0, 0, 0f, false, 0);
        Assert.True(pad.IsEmpty); // só muda quando o stroke completa

        pad.OnTouchUp(5, 5, 0f, false, 10);
        Assert.False(pad.IsEmpty);

        pad.Undo();
        Assert.True(pad.IsEmpty);

        pad.Redo();
        Assert.False(pad.IsEmpty);

        pad.Clear();
        Assert.True(pad.IsEmpty);
    }

    [Fact]
    public void Valores_default_das_propriedades()
    {
        var pad = new SignaturePad();

        Assert.Equal(Colors.Black, pad.StrokeColor);
        // Fundo "papel" default para a tinta preta continuar visível em temas escuros.
        Assert.Equal(Colors.White, pad.BackgroundColor);
        Assert.Equal(1.0, pad.MinStrokeWidth);
        Assert.Equal(3.5, pad.MaxStrokeWidth);
        Assert.Equal(0.7, pad.VelocityFilterWeight);
        Assert.False(pad.ShowSignatureLine);
        Assert.Equal(Colors.Gray, pad.SignatureLineColor);
        Assert.Null(pad.PromptText);
        Assert.Equal(Colors.Gray, pad.PromptTextColor);
        Assert.Null(pad.StrokeCompletedCommand);
        Assert.True(pad.IsEmpty);
        Assert.False(pad.HasActiveStroke);
    }

    [Fact]
    public void Propriedades_de_aparencia_sao_bindable_e_aceitam_escrita()
    {
        var pad = new SignaturePad
        {
            StrokeColor = Colors.Navy,
            MinStrokeWidth = 2,
            MaxStrokeWidth = 8,
            VelocityFilterWeight = 0.5,
            ShowSignatureLine = true,
            SignatureLineColor = Colors.Silver,
            PromptText = "Assine aqui",
            PromptTextColor = Colors.DarkGray,
        };

        Assert.Equal(Colors.Navy, pad.StrokeColor);
        Assert.Equal(2.0, pad.MinStrokeWidth);
        Assert.Equal(8.0, pad.MaxStrokeWidth);
        Assert.Equal(0.5, pad.VelocityFilterWeight);
        Assert.True(pad.ShowSignatureLine);
        Assert.Equal(Colors.Silver, pad.SignatureLineColor);
        Assert.Equal("Assine aqui", pad.PromptText);
        Assert.Equal(Colors.DarkGray, pad.PromptTextColor);
    }

    [Fact]
    public void Drawable_do_pad_e_o_SignaturePadDrawable()
    {
        var pad = new SignaturePad();

        Assert.IsType<SignaturePadDrawable>(pad.Drawable);
    }
}

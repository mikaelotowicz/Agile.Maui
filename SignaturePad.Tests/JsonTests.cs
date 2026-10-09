using System.Globalization;
using System.Text.Json;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Agile.Maui.SignaturePadTests;

public class JsonTests
{
    private static SignaturePad PadComAssinatura()
    {
        var pad = new SignaturePad { StrokeColor = Colors.Red };
        pad.OnTouchDown(10.5f, 20.25f, 0.75f, true, 1000);
        pad.OnTouchMove(30.75f, 40.5f, 0.5f, true, 1050);
        pad.OnTouchUp(50.25f, 60.75f, 0.25f, true, 1100);

        pad.StrokeColor = Colors.Blue;
        pad.Assinar(2000, (1.5f, 2.5f), (3.5f, 4.5f));
        return pad;
    }

    private static void VerificarRoundTrip(SignaturePad original, SignaturePad carregado)
    {
        var d1 = original.GetSignatureData();
        var d2 = carregado.GetSignatureData();

        Assert.Equal(d1.Strokes.Count, d2.Strokes.Count);
        Assert.Equal(d1.CanvasSize, d2.CanvasSize);
        Assert.Equal(d1.TotalPoints, d2.TotalPoints);

        for (var s = 0; s < d1.Strokes.Count; s++)
        {
            var a = d1.Strokes[s];
            var b = d2.Strokes[s];
            Assert.Equal(a.Color, b.Color);
            Assert.Equal(a.Points.Count, b.Points.Count);

            for (var p = 0; p < a.Points.Count; p++)
            {
                Assert.Equal(a.Points[p].X, b.Points[p].X);
                Assert.Equal(a.Points[p].Y, b.Points[p].Y);
                Assert.Equal(a.Points[p].TimestampMs, b.Points[p].TimestampMs);
                Assert.Equal(a.Points[p].Pressure, b.Points[p].Pressure);
                Assert.Equal(a.Points[p].PressureSupported, b.Points[p].PressureSupported);
            }
        }
    }

    [Fact]
    public void RoundTrip_preserva_strokes_pontos_pressao_timestamps_e_cores()
    {
        var pad = PadComAssinatura();

        var json = pad.GetSignatureJson();
        var pad2 = new SignaturePad();
        pad2.LoadSignatureJson(json);

        VerificarRoundTrip(pad, pad2);
    }

    [Fact]
    public void RoundTrip_funciona_em_cultura_ptBR()
    {
        // Regressão: floats com vírgula decimal na cultura não podem afetar o JSON.
        var culturaAnterior = CultureInfo.CurrentCulture;
        var culturaUiAnterior = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            CultureInfo.CurrentUICulture = new CultureInfo("pt-BR");

            var pad = PadComAssinatura();
            var json = pad.GetSignatureJson();

            // O JSON precisa usar ponto decimal, nunca vírgula.
            Assert.Contains("10.5", json);
            Assert.DoesNotContain("10,5", json);

            var pad2 = new SignaturePad();
            pad2.LoadSignatureJson(json);

            VerificarRoundTrip(pad, pad2);
        }
        finally
        {
            CultureInfo.CurrentCulture = culturaAnterior;
            CultureInfo.CurrentUICulture = culturaUiAnterior;
        }
    }

    [Fact]
    public void Json_indentado_e_compacto_carregam_os_mesmos_dados()
    {
        var pad = PadComAssinatura();

        var compacto = pad.GetSignatureJson(indented: false);
        var indentado = pad.GetSignatureJson(indented: true);

        Assert.DoesNotContain('\n', compacto);
        Assert.Contains('\n', indentado);

        var pad2 = new SignaturePad();
        pad2.LoadSignatureJson(indentado);
        VerificarRoundTrip(pad, pad2);
    }

    [Fact]
    public void Json_gerado_tem_versao_1_e_schema_esperado()
    {
        var pad = PadComAssinatura();

        using var doc = JsonDocument.Parse(pad.GetSignatureJson());
        var root = doc.RootElement;

        Assert.Equal(1, root.GetProperty("Version").GetInt32());
        Assert.True(root.GetProperty("CanvasWidth").GetDouble() >= 0);
        Assert.True(root.GetProperty("CanvasHeight").GetDouble() >= 0);

        var strokes = root.GetProperty("Strokes");
        Assert.Equal(2, strokes.GetArrayLength());
        var ponto = strokes[0].GetProperty("Points")[0];
        Assert.True(ponto.TryGetProperty("X", out _));
        Assert.True(ponto.TryGetProperty("Y", out _));
        Assert.True(ponto.TryGetProperty("TimestampMs", out _));
        Assert.True(ponto.TryGetProperty("Pressure", out _));
        Assert.True(ponto.TryGetProperty("PressureSupported", out _));
    }

    [Fact]
    public void Serializacao_usa_o_contexto_source_generated()
    {
        // O contrato gerado precisa resolver o tipo raiz (garantia de trimming/AOT)...
        Assert.NotNull(SignatureJsonContext.Default.SignatureJsonDocument);
        Assert.NotNull(SignatureJsonContext.Default.GetTypeInfo(typeof(SignatureJsonDocument)));

        // ...e reproduzir byte a byte o que o pad serializa.
        var pad = PadComAssinatura();
        var json = pad.GetSignatureJson();

        var dto = JsonSerializer.Deserialize(json, SignatureJsonContext.Default.SignatureJsonDocument);
        Assert.NotNull(dto);
        var reserializado = JsonSerializer.Serialize(dto!, SignatureJsonContext.Default.SignatureJsonDocument);

        Assert.Equal(json, reserializado);
    }

    [Fact]
    public void Contexto_indentado_compartilhado_gera_json_indentado()
    {
        Assert.NotNull(SignatureJsonContext.Indented.SignatureJsonDocument);
        Assert.True(SignatureJsonContext.Indented.Options.WriteIndented);
        Assert.False(SignatureJsonContext.Default.Options.WriteIndented);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Json_vazio_ou_em_branco_lanca_ArgumentException(string json)
    {
        var pad = new SignaturePad();

        Assert.Throws<ArgumentException>(() => pad.LoadSignatureJson(json));
    }

    [Fact]
    public void Json_malformado_lanca_JsonException()
    {
        var pad = new SignaturePad();

        Assert.ThrowsAny<JsonException>(() => pad.LoadSignatureJson("{ isso nao e json"));
    }

    [Fact]
    public void Json_null_literal_lanca_InvalidOperationException()
    {
        var pad = new SignaturePad();

        Assert.Throws<InvalidOperationException>(() => pad.LoadSignatureJson("null"));
    }

    [Fact]
    public void Versao_desconhecida_lanca_NotSupportedException()
    {
        var pad = new SignaturePad();

        Assert.Throws<NotSupportedException>(() =>
            pad.LoadSignatureJson("""{"Version":2,"CanvasWidth":10,"CanvasHeight":10,"Strokes":[]}"""));
    }

    [Fact]
    public void Strokes_null_lanca_excecao_controlada_e_nao_NRE()
    {
        var pad = new SignaturePad();

        // Regressão: null explícito no payload causava NullReferenceException.
        var ex = Record.Exception(() =>
            pad.LoadSignatureJson("""{"Version":1,"CanvasWidth":10,"CanvasHeight":10,"Strokes":null}"""));

        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public void Points_null_lanca_excecao_controlada_e_nao_NRE()
    {
        var pad = new SignaturePad();

        var ex = Record.Exception(() => pad.LoadSignatureJson(
            """{"Version":1,"CanvasWidth":10,"CanvasHeight":10,"Strokes":[{"Color":"#FF000000","Points":null}]}"""));

        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public void Ponto_null_na_lista_lanca_excecao_controlada_e_nao_NRE()
    {
        var pad = new SignaturePad();

        var ex = Record.Exception(() => pad.LoadSignatureJson(
            """{"Version":1,"CanvasWidth":10,"CanvasHeight":10,"Strokes":[{"Color":"#FF000000","Points":[null]}]}"""));

        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public void Cor_com_alpha_sobrevive_ao_round_trip()
    {
        var pad = new SignaturePad { StrokeColor = Color.FromRgba(1f, 0.5f, 0.25f, 0.5f) };
        pad.Assinar(0, (0, 0), (10, 10));

        var pad2 = new SignaturePad();
        pad2.LoadSignatureJson(pad.GetSignatureJson());

        var cor = pad2.GetStrokes()[0].Color;
        Assert.Equal(1f, cor.Red, 2);
        Assert.Equal(0.5f, cor.Green, 2);
        Assert.Equal(0.25f, cor.Blue, 2);
        Assert.Equal(0.5f, cor.Alpha, 2);
    }

    [Fact]
    public void Cor_com_6_digitos_carrega_com_alpha_cheio()
    {
        var pad = new SignaturePad();
        pad.LoadSignatureJson(
            """{"Version":1,"CanvasWidth":10,"CanvasHeight":10,"Strokes":[{"Color":"#FF0000","Points":[{"X":1,"Y":2,"TimestampMs":0,"Pressure":0,"PressureSupported":false}]}]}""");

        var cor = pad.GetStrokes()[0].Color;
        Assert.Equal(1f, cor.Alpha, 2);
        Assert.Equal(1f, cor.Red, 2);
        Assert.Equal(0f, cor.Green, 2);
    }

    [Fact]
    public void Cor_invalida_lanca_FormatException()
    {
        var pad = new SignaturePad();

        Assert.Throws<FormatException>(() => pad.LoadSignatureJson(
            """{"Version":1,"CanvasWidth":10,"CanvasHeight":10,"Strokes":[{"Color":"#ZZZ","Points":[{"X":1,"Y":2,"TimestampMs":0,"Pressure":0,"PressureSupported":false}]}]}"""));
    }

    [Fact]
    public void Cor_ausente_carrega_como_preto()
    {
        var pad = new SignaturePad();
        pad.LoadSignatureJson(
            """{"Version":1,"CanvasWidth":10,"CanvasHeight":10,"Strokes":[{"Color":"","Points":[{"X":1,"Y":2,"TimestampMs":0,"Pressure":0,"PressureSupported":false}]}]}""");

        Assert.Equal(Colors.Black, pad.GetStrokes()[0].Color);
    }

    [Fact]
    public void LoadSignatureJson_recalcula_uma_largura_por_ponto()
    {
        var pad = PadComAssinatura();

        var pad2 = new SignaturePad();
        pad2.LoadSignatureJson(pad.GetSignatureJson());

        for (var i = 0; i < pad2.AllStrokesForRender.Count; i++)
        {
            var stroke = pad2.AllStrokesForRender[i];
            Assert.Equal(stroke.Points.Count, stroke.Widths.Count);
            Assert.All(stroke.Widths, w => Assert.True(w > 0));
        }
    }

    [Fact]
    public void LoadStrokes_substitui_o_conteudo_anterior()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10));

        var origem = new SignaturePad();
        origem.Assinar(0, (1, 1), (2, 2), (3, 3));

        pad.LoadStrokes(origem.GetStrokes());

        var strokes = pad.GetStrokes();
        Assert.Single(strokes);
        Assert.Equal(3, strokes[0].Points.Count);
        Assert.Equal(1f, strokes[0].Points[0].X);
    }

    [Fact]
    public void LoadStrokes_com_lista_vazia_deixa_o_pad_vazio()
    {
        var pad = new SignaturePad();
        pad.Assinar(0, (0, 0), (10, 10));

        pad.LoadStrokes(Array.Empty<SignatureStroke>());

        Assert.True(pad.IsEmpty);
        Assert.Empty(pad.GetStrokes());
    }

    [Fact]
    public void LoadStrokes_descarta_strokes_sem_pontos()
    {
        var pad = new SignaturePad();

        pad.LoadStrokes(new[] { new SignatureStroke(Array.Empty<SignaturePoint>(), Colors.Black) });

        Assert.True(pad.IsEmpty);
        Assert.Empty(pad.GetStrokes());
    }
}

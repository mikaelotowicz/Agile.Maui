using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Agile.Maui.PdfGen.Api;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;
using Agile.Maui.PdfGen.Text;
using Xunit;

namespace Agile.Maui.PdfGen.Tests;

/// <summary>
/// Decorações (fundo/borda) sobre conteúdo paginável: devem ser desenhadas uma única vez por página
/// sobre o trecho contíguo, e não uma vez por fatia do fluxo (linha de texto, espaçador de padding...).
/// Regressão: Background(cor, cornerRadius).Padding(12).Text(parágrafo longo) gerava vários
/// retângulos arredondados empilhados, um por fatia.
/// </summary>
public class FlowDecorationTests
{
    const float Margin = 30f;

    const string LongParagraph =
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore " +
        "et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut " +
        "aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum " +
        "dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui " +
        "officia deserunt mollit anim id est laborum.";

    // ---- renderer de gravação ----

    abstract record Op;
    sealed record Fill(PdfRect Rect, float Radius) : Op;
    sealed record Gradient(PdfRect Rect, float Radius) : Op;
    sealed record Stroke(PdfRect Rect, float Radius) : Op;
    sealed record TextOp(PdfPoint Baseline) : Op;

    sealed class RecordingRenderer : IPdfRenderer
    {
        public List<List<Op>> Pages { get; } = new();

        public void BeginDocument() { }
        public IRenderContext BeginPage(PdfSize size)
        {
            var ops = new List<Op>();
            Pages.Add(ops);
            return new RecordingContext(ops);
        }
        public void EndPage() { }
        public byte[] EndDocument() => [];
    }

    sealed class RecordingContext : IRenderContext
    {
        readonly List<Op> _ops;
        public RecordingContext(List<Op> ops) => _ops = ops;

        public void DrawText(string text, PdfPoint baselineOrigin, TextStyle style) => _ops.Add(new TextOp(baselineOrigin));
        public void DrawImage(PdfImage image, PdfRect destination) { }
        public void DrawLine(PdfPoint from, PdfPoint to, PdfColor color, float thickness) { }
        public void DrawRectangle(PdfRect rect, PdfColor color, float thickness, float cornerRadius = 0f) => _ops.Add(new Stroke(rect, cornerRadius));
        public void FillRectangle(PdfRect rect, PdfColor color, float cornerRadius = 0f) => _ops.Add(new Fill(rect, cornerRadius));
        public void FillGradient(PdfRect rect, GradientBrush brush, float cornerRadius = 0f) => _ops.Add(new Gradient(rect, cornerRadius));
        public void StrokeGradient(PdfRect rect, GradientBrush brush, float thickness, float cornerRadius = 0f) => _ops.Add(new Stroke(rect, cornerRadius));
        public void SaveState() { }
        public void RestoreState() { }
        public void ClipRectangle(PdfRect rect) { }
    }

    static List<List<Op>> Record(System.Action<IContainer> content)
    {
        PdfDocument doc = PdfDocument.Create(d => d.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(Margin);
            content(page.Content());
        }));

        var renderer = new RecordingRenderer();
        doc.Render(renderer);
        return renderer.Pages;
    }

    static float ContentWidth => PageSizes.A4.Width - 2f * Margin;

    // ---- uma página ----

    [Fact]
    public void Fundo_arredondado_de_paragrafo_longo_e_um_unico_retangulo()
    {
        List<List<Op>> pages = Record(c => c.Column(col =>
            col.Item().Background(Colors.LightGray, cornerRadius: 8f).Padding(12f).Text(LongParagraph)));

        List<Op> ops = Assert.Single(pages);
        Fill fill = Assert.Single(ops.OfType<Fill>());
        List<TextOp> texts = ops.OfType<TextOp>().ToList();

        Assert.True(texts.Count >= 3, $"o parágrafo deveria quebrar em várias linhas (linhas: {texts.Count})");
        Assert.Equal(8f, fill.Radius);
        Assert.Equal(Margin, fill.Rect.Left, 2);
        Assert.Equal(Margin, fill.Rect.Top, 2);
        Assert.Equal(ContentWidth, fill.Rect.Width, 2);

        // O retângulo único cobre o padding superior, todas as linhas e o padding inferior.
        Assert.All(texts, t =>
        {
            Assert.InRange(t.Baseline.Y, fill.Rect.Top + 12f, fill.Rect.Bottom - 12f);
            Assert.Equal(Margin + 12f, t.Baseline.X, 2);
        });

        // Fundo pintado antes do texto.
        Assert.Equal(0, ops.IndexOf(fill));
    }

    [Fact]
    public void Borda_arredondada_de_paragrafo_longo_e_um_unico_contorno_sobre_o_texto()
    {
        List<List<Op>> pages = Record(c =>
            c.Border(1f, Colors.Gray, cornerRadius: 8f).Padding(12f).Text(LongParagraph));

        List<Op> ops = Assert.Single(pages);
        Stroke stroke = Assert.Single(ops.OfType<Stroke>());

        Assert.Equal(8f, stroke.Radius);
        Assert.Equal(ContentWidth - 1f, stroke.Rect.Width, 2);   // contorno na meia-espessura
        Assert.Equal(ops.Count - 1, ops.IndexOf(stroke));          // pintado depois do conteúdo
    }

    [Fact]
    public void Fundo_em_gradiente_e_um_unico_preenchimento()
    {
        List<List<Op>> pages = Record(c =>
            c.Background(GradientBrush.Linear(Colors.Blue, Colors.White, 90f), 8f).Padding(12f).Text(LongParagraph));

        Gradient g = Assert.Single(Assert.Single(pages).OfType<Gradient>());
        Assert.Equal(8f, g.Radius);
    }

    [Fact]
    public void Fundo_e_borda_aninhados_mantem_a_ordem_de_pintura()
    {
        List<List<Op>> pages = Record(c => c
            .Background(Colors.LightGray, 8f)
            .Padding(4f)
            .Border(1f, Colors.Gray, 6f)
            .Padding(8f)
            .Text(LongParagraph));

        List<Op> ops = Assert.Single(pages);
        Fill fill = Assert.Single(ops.OfType<Fill>());
        Stroke stroke = Assert.Single(ops.OfType<Stroke>());

        Assert.Equal(0, ops.IndexOf(fill));
        Assert.Equal(ops.Count - 1, ops.IndexOf(stroke));

        // A borda interna fica deslocada pelo padding externo.
        Assert.Equal(Margin + 4f + 0.5f, stroke.Rect.Left, 2);
        Assert.Equal(Margin + 4f + 0.5f, stroke.Rect.Top, 2);
        Assert.True(stroke.Rect.Bottom < fill.Rect.Bottom);
    }

    [Fact]
    public void Blocos_irmaos_decorados_geram_retangulos_separados()
    {
        List<List<Op>> pages = Record(c => c.Column(col =>
        {
            col.Item().Background(Colors.LightGray, 8f).Padding(12f).Text(LongParagraph);
            col.Item().Background(Colors.LightGray, 8f).Padding(12f).Text(LongParagraph);
        }));

        List<Fill> fills = Assert.Single(pages).OfType<Fill>().ToList();
        Assert.Equal(2, fills.Count);
        Assert.Equal(fills[0].Rect.Bottom, fills[1].Rect.Top, 2);
    }

    // ---- paginação ----

    [Fact]
    public void Fundo_partido_entre_paginas_fecha_e_reabre_os_cantos_na_quebra()
    {
        const int lines = 200;
        List<List<Op>> pages = Record(c => c
            .Background(Colors.LightGray, 8f)
            .Padding(12f)
            .Column(col =>
            {
                for (int i = 0; i < lines; i++)
                    col.Item().Text($"linha decorada {i}");
            }));

        Assert.True(pages.Count > 1);
        Assert.Equal(lines, pages.Sum(p => p.OfType<TextOp>().Count()));

        float contentBottom = PageSizes.A4.Height - Margin;
        for (int i = 0; i < pages.Count; i++)
        {
            List<Op> ops = pages[i];
            Fill fill = Assert.Single(ops.OfType<Fill>());

            // Um retângulo arredondado completo por página, no topo da área de conteúdo, pintado antes do texto.
            Assert.Equal(8f, fill.Radius);
            Assert.Equal(Margin, fill.Rect.Top, 2);
            Assert.Equal(0, ops.IndexOf(fill));
            Assert.True(fill.Rect.Bottom <= contentBottom + 0.01f);
            Assert.All(ops.OfType<TextOp>(), t => Assert.InRange(t.Baseline.Y, fill.Rect.Top, fill.Rect.Bottom));
        }

        // Só a primeira página começa com o padding superior; só a última termina com o inferior.
        float firstBaselineP1 = pages[0].OfType<TextOp>().First().Baseline.Y;
        float firstBaselineP2 = pages[1].OfType<TextOp>().First().Baseline.Y;
        Assert.Equal(12f, firstBaselineP1 - firstBaselineP2, 2);

        Fill lastFill = pages[^1].OfType<Fill>().Single();
        float lastBaseline = pages[^1].OfType<TextOp>().Last().Baseline.Y;
        Assert.True(lastFill.Rect.Bottom - lastBaseline > 12f);
    }

    [Fact]
    public void Borda_partida_entre_paginas_desenha_um_contorno_por_pagina()
    {
        List<List<Op>> pages = Record(c => c
            .Border(1f, Colors.Gray, 8f)
            .Padding(8f)
            .Column(col =>
            {
                for (int i = 0; i < 200; i++)
                    col.Item().Text($"linha com borda {i}");
            }));

        Assert.True(pages.Count > 1);
        Assert.All(pages, ops =>
        {
            Stroke stroke = Assert.Single(ops.OfType<Stroke>());
            Assert.Equal(ops.Count - 1, ops.IndexOf(stroke));
        });
    }

    // ---- ponta a ponta ----

    [Fact]
    public void Svg_emite_um_unico_rect_arredondado_para_o_paragrafo()
    {
        string svg = Encoding.UTF8.GetString(PdfDocument.Create(d => d.Page(page =>
        {
            page.Margin(Margin);
            page.Content().Column(col =>
                col.Item().Background(Colors.LightGray, cornerRadius: 8f).Padding(12f).Text(LongParagraph));
        })).GenerateSvg());

        Assert.Single(Regex.Matches(svg, "<rect [^>]*rx=\"8\""));
    }
}

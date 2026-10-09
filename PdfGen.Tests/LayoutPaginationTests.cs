using System.Collections.Generic;
using System.Linq;
using Agile.Maui.PdfGen.Api;
using Agile.Maui.PdfGen.Layout;
using Agile.Maui.PdfGen.Layout.Elements;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;
using Xunit;

namespace Agile.Maui.PdfGen.Tests;

/// <summary>
/// Paginação e posicionamento: itens atômicos maiores que a página (sem loop infinito),
/// padding/espaçamento/constrained no fluxo, Align/Row/Stack e seções múltiplas.
/// </summary>
public class LayoutPaginationTests
{
    /// <summary>Elemento de sonda: registra os bounds recebidos no Arrange.</summary>
    sealed class ProbeElement : ILayoutElement
    {
        readonly PdfSize _desired;
        public PdfRect Arranged;

        public ProbeElement(float width, float height) => _desired = new PdfSize(width, height);
        public PdfSize Measure(PdfSize available) => _desired;
        public void Arrange(PdfRect bounds) => Arranged = bounds;
        public void Render(IRenderContext context) { }
    }

    // ---- itens maiores que a página ----

    [Fact]
    public void Item_atomico_maior_que_a_pagina_avanca_sem_loop()
    {
        PdfDocument doc = PdfDocument.Create(d =>
        {
            d.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30f);
                page.Content().Column(col =>
                {
                    for (int i = 0; i < 3; i++)
                        col.Item().Height(2000f);   // bem maior que a área útil (~782pt)
                });
            });
        });

        List<PlannedPage> pages = LayoutEngine.Plan(doc.Model);

        // Cada item gigante consome exatamente uma página — termina, não entra em loop.
        Assert.Equal(3, pages.Count);
        Assert.All(pages, p => Assert.Single(p.Items));
        Assert.All(pages, p => Assert.Equal(2000f, p.Items[0].Bounds.Height, 2));
    }

    [Fact]
    public void Item_unico_gigante_ocupa_uma_so_pagina()
    {
        PdfDocument doc = PdfDocument.Create(d =>
        {
            d.Page(page => page.Content().Height(5000f));
        });

        Assert.Single(LayoutEngine.Plan(doc.Model));
    }

    [Fact]
    public void Pagina_sem_conteudo_gera_uma_pagina_fisica_vazia()
    {
        PdfDocument doc = PdfDocument.Create(d => d.Page(page => page.Margin(20f)));
        List<PlannedPage> pages = LayoutEngine.Plan(doc.Model);

        Assert.Single(pages);
        Assert.Empty(pages[0].Items);
    }

    [Fact]
    public void Duas_secoes_concatenam_as_paginas()
    {
        PdfDocument doc = PdfDocument.Create(d =>
        {
            d.Page(page => page.Content().Text("seção 1"));
            d.Page(page => page.Content().Text("seção 2"));
        });

        Assert.Equal(2, LayoutEngine.Plan(doc.Model).Count);
        Assert.Contains("/Count 2", PdfMiniParser.AsLatin1(doc.GeneratePdf()));
    }

    // ---- decoradores no fluxo ----

    [Fact]
    public void Padding_vira_espacador_e_desloca_itens_do_fluxo()
    {
        PdfDocument doc = PdfDocument.Create(d =>
        {
            d.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30f);
                page.Content().Padding(20f).Column(col =>
                {
                    col.Item().Text("um");
                    col.Item().Text("dois");
                });
            });
        });

        List<PlannedPage> pages = LayoutEngine.Plan(doc.Model);
        var items = pages[0].Items;

        // Primeiro item: espaçador do padding superior.
        Assert.IsType<SpacerElement>(items[0].Element);
        Assert.Equal(20f, items[0].Bounds.Height, 2);

        // Linhas de texto deslocadas 20pt à direita e estreitadas em 40pt.
        var textItems = items.Where(i => i.Element is SingleLineElement).ToList();
        Assert.Equal(2, textItems.Count);
        Assert.All(textItems, i => Assert.Equal(50f, i.Bounds.Left, 2));           // 30 margem + 20 padding
        Assert.All(textItems, i => Assert.Equal(495f, i.Bounds.Width, 2));         // 535 - 40

        // Último item: espaçador do padding inferior.
        Assert.IsType<SpacerElement>(items[^1].Element);
    }

    [Fact]
    public void Espacamento_da_coluna_vira_espacadores_no_fluxo()
    {
        PdfDocument doc = PdfDocument.Create(d =>
        {
            d.Page(page =>
            {
                page.Margin(30f);
                page.Content().Column(col =>
                {
                    col.Spacing(7f);
                    col.Item().Text("a");
                    col.Item().Text("b");
                    col.Item().Text("c");
                });
            });
        });

        var items = LayoutEngine.Plan(doc.Model)[0].Items;
        var spacers = items.Where(i => i.Element is SpacerElement).ToList();

        Assert.Equal(2, spacers.Count);
        Assert.All(spacers, s => Assert.Equal(7f, s.Bounds.Height, 2));

        // Itens empilhados sem lacunas nem sobreposição: cada topo coincide com o fundo anterior.
        for (int i = 1; i < items.Count; i++)
            Assert.Equal(items[i - 1].Bounds.Bottom, items[i].Bounds.Top, 2);
    }

    [Fact]
    public void Width_e_height_fixos_viram_bounds_do_item()
    {
        PdfDocument doc = PdfDocument.Create(d =>
        {
            d.Page(page =>
            {
                page.Margin(30f);
                page.Content().Width(100f).Height(40f);
            });
        });

        var item = Assert.Single(LayoutEngine.Plan(doc.Model)[0].Items);
        Assert.Equal(30f, item.Bounds.Left, 2);
        Assert.Equal(100f, item.Bounds.Width, 2);
        Assert.Equal(40f, item.Bounds.Height, 2);
    }

    [Fact]
    public void Wrappers_de_gradiente_paginam_o_fluxo_interno()
    {
        PdfDocument doc = PdfDocument.Create(d =>
        {
            d.Page(page =>
            {
                page.Margin(30f);
                page.Content()
                    .Background(GradientBrush.Linear(Colors.Blue, Colors.White, 90f))
                    .Border(1f, GradientBrush.Linear(Colors.Red, Colors.Yellow, 0f))
                    .Column(col =>
                    {
                        for (int i = 0; i < 300; i++)
                            col.Item().Text($"linha gradiente {i}");
                    });
            });
        });

        Assert.True(LayoutEngine.Plan(doc.Model).Count > 1);
    }

    // ---- Align / Row / Stack ----

    [Fact]
    public void Align_centro_meio_posiciona_o_filho()
    {
        var probe = new ProbeElement(10f, 10f);
        var align = new AlignElement(probe, HorizontalAlignment.Center, VerticalAlignment.Middle);

        align.Arrange(new PdfRect(0f, 0f, 100f, 50f));

        Assert.Equal(45f, probe.Arranged.Left, 2);
        Assert.Equal(20f, probe.Arranged.Top, 2);
        Assert.Equal(10f, probe.Arranged.Width, 2);
        Assert.Equal(10f, probe.Arranged.Height, 2);
    }

    [Fact]
    public void Align_inferior_direito_encosta_nas_bordas()
    {
        var probe = new ProbeElement(10f, 10f);
        var align = new AlignElement(probe, HorizontalAlignment.Right, VerticalAlignment.Bottom);

        align.Arrange(new PdfRect(0f, 0f, 100f, 50f));

        Assert.Equal(90f, probe.Arranged.Left, 2);
        Assert.Equal(40f, probe.Arranged.Top, 2);
    }

    [Fact]
    public void Row_distribui_fixos_e_pesos_com_espacamento()
    {
        var p1 = new ProbeElement(1f, 1f);
        var p2 = new ProbeElement(1f, 1f);
        var p3 = new ProbeElement(1f, 1f);
        var row = new RowElement(new List<RowItem>
        {
            new(p1, 100f, 0f),
            new(p2, null, 1f),
            new(p3, null, 3f),
        }, spacing: 10f);

        row.Measure(new PdfSize(500f, 100f));
        row.Arrange(new PdfRect(0f, 0f, 500f, 50f));

        // Restante = 500 - 100 fixo - 2x10 espaçamento = 380 → pesos 1:3 → 95 e 285.
        Assert.Equal(0f, p1.Arranged.Left, 2);
        Assert.Equal(100f, p1.Arranged.Width, 2);
        Assert.Equal(110f, p2.Arranged.Left, 2);
        Assert.Equal(95f, p2.Arranged.Width, 2);
        Assert.Equal(215f, p3.Arranged.Left, 2);
        Assert.Equal(285f, p3.Arranged.Width, 2);
    }

    [Fact]
    public void Stack_sobrepoe_todos_os_filhos_na_mesma_area()
    {
        var p1 = new ProbeElement(10f, 10f);
        var p2 = new ProbeElement(30f, 5f);
        var stack = new StackElement(new List<ILayoutElement> { p1, p2 });

        var bounds = new PdfRect(5f, 7f, 200f, 90f);
        stack.Arrange(bounds);

        foreach (ProbeElement p in new[] { p1, p2 })
        {
            Assert.Equal(bounds.Left, p.Arranged.Left, 2);
            Assert.Equal(bounds.Top, p.Arranged.Top, 2);
            Assert.Equal(bounds.Width, p.Arranged.Width, 2);
            Assert.Equal(bounds.Height, p.Arranged.Height, 2);
        }
    }

    [Fact]
    public void Tabela_sem_colunas_declaradas_infere_pelo_maior_numero_de_celulas()
    {
        PdfDocument doc = PdfDocument.Create(d =>
        {
            d.Page(page => page.Content().Table(t =>
            {
                t.Row(r => { r.Cell().Text("a"); r.Cell().Text("b"); r.Cell().Text("c"); });
                t.Row(r => { r.Cell().Text("d"); r.Cell().Text("e"); r.Cell().Text("f"); });
            }));
        });

        List<PlannedPage> pages = LayoutEngine.Plan(doc.Model);
        Assert.Single(pages);
        Assert.Equal(2, pages[0].Items.Count(i => i.Element is TableRowElement));
        Assert.Contains("(a) Tj", PdfMiniParser.AllContent(doc.GeneratePdf()));
    }
}

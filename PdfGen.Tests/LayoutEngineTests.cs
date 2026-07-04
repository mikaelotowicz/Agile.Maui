using System.Collections.Generic;
using Agile.Maui.PdfGen.Api;
using Agile.Maui.PdfGen.Layout;
using Agile.Maui.PdfGen.Layout.Elements;
using Agile.Maui.PdfGen.Primitives;
using Xunit;

namespace Agile.Maui.PdfGen.Tests;

public class LayoutEngineTests
{
    static PdfDocument BuildLongColumn(int items)
    {
        return PdfDocument.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30f);
                page.Content().Column(col =>
                {
                    col.Spacing(5f);
                    for (int i = 0; i < items; i++)
                        col.Item().Text($"Item {i}").FontSize(12f);
                });
            });
        });
    }

    [Fact]
    public void Single_short_page_produces_one_page()
    {
        PdfDocument doc = BuildLongColumn(3);
        List<PlannedPage> pages = LayoutEngine.Plan(doc.Model);
        Assert.Single(pages);
    }

    [Fact]
    public void Long_content_breaks_into_multiple_pages()
    {
        PdfDocument doc = BuildLongColumn(500);
        List<PlannedPage> pages = LayoutEngine.Plan(doc.Model);
        Assert.True(pages.Count > 1, $"esperava múltiplas páginas, obteve {pages.Count}");
    }

    [Fact]
    public void Header_and_footer_repeat_on_every_page()
    {
        PdfDocument doc = PdfDocument.Create(d =>
        {
            d.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30f);
                page.Header().Text("CABEÇALHO");
                page.Footer().Text("RODAPÉ");
                page.Content().Column(col =>
                {
                    for (int i = 0; i < 400; i++)
                        col.Item().Text($"linha {i}");
                });
            });
        });

        List<PlannedPage> pages = LayoutEngine.Plan(doc.Model);
        Assert.True(pages.Count > 1);

        // Cada página tem ao menos 2 itens fixos (header + footer) além do conteúdo.
        foreach (PlannedPage p in pages)
            Assert.True(p.Items.Count >= 3);
    }

    [Fact]
    public void Table_header_repeats_across_pages()
    {
        PdfDocument doc = PdfDocument.Create(d =>
        {
            d.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30f);
                page.Content().Table(t =>
                {
                    t.Columns(c => { c.RelativeColumn(); c.RelativeColumn(); });
                    t.Header(h => { h.Cell().Text("Produto"); h.Cell().Text("Preço"); });
                    for (int i = 0; i < 400; i++)
                        t.Row(r => { r.Cell().Text($"Produto {i}"); r.Cell().Text($"{i},00"); });
                });
            });
        });

        List<PlannedPage> pages = LayoutEngine.Plan(doc.Model);
        Assert.True(pages.Count > 1);

        // Toda página deve conter linhas de tabela (o cabeçalho é repetido junto).
        int pagesWithTableRows = 0;

        foreach (PlannedPage p in pages)
        {
            bool hasRow = false;
            foreach (PlacedItem item in p.Items)
            {
                if (item.Element is TableRowElement)
                    hasRow = true;
            }
            if (hasRow)
                pagesWithTableRows++;
        }

        Assert.True(pagesWithTableRows == pages.Count);
    }

    [Fact]
    public void Background_wrapper_allows_inner_flow_to_paginate()
    {
        PdfDocument doc = PdfDocument.Create(d =>
        {
            d.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30f);
                page.Content()
                    .Background(Colors.LightGray, 4f)
                    .Padding(8f)
                    .Column(col =>
                    {
                        for (int i = 0; i < 300; i++)
                            col.Item().Text($"linha decorada {i}");
                    });
            });
        });

        List<PlannedPage> pages = LayoutEngine.Plan(doc.Model);
        Assert.True(pages.Count > 1);
    }

    [Fact]
    public void Border_wrapper_allows_inner_flow_to_paginate()
    {
        PdfDocument doc = PdfDocument.Create(d =>
        {
            d.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30f);
                page.Content()
                    .Border(1f, Colors.Gray, 4f)
                    .Padding(8f)
                    .Column(col =>
                    {
                        for (int i = 0; i < 300; i++)
                            col.Item().Text($"linha com borda {i}");
                    });
            });
        });

        List<PlannedPage> pages = LayoutEngine.Plan(doc.Model);
        Assert.True(pages.Count > 1);
    }
}

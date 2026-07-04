using System.Collections.Generic;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;

namespace Agile.Maui.PdfGen.Layout;

/// <summary>Um elemento já posicionado em uma página física.</summary>
public readonly struct PlacedItem
{
    public readonly ILayoutElement Element;
    public readonly PdfRect Bounds;

    public PlacedItem(ILayoutElement element, PdfRect bounds)
    {
        Element = element;
        Bounds = bounds;
    }
}

/// <summary>Uma página física planejada (a "Render Tree").</summary>
public sealed class PlannedPage
{
    public PdfSize Size { get; }
    public PdfColor? Background { get; }
    public List<PlacedItem> Items { get; } = new();

    public PlannedPage(PdfSize size, PdfColor? background)
    {
        Size = size;
        Background = background;
    }
}

/// <summary>
/// Motor de layout independente de plataforma. Faz duas passagens: (1) planeja todas as páginas
/// físicas — quebra de página automática, cabeçalho/rodapé repetidos e cabeçalho de tabela repetido;
/// (2) renderiza cada página no backend. Todo o layout é calculado antes da renderização.
/// </summary>
public static class LayoutEngine
{
    const float Epsilon = 0.01f;

    public static byte[] Render(DocumentModel model, IPdfRenderer renderer)
    {
        List<PlannedPage> planned = Plan(model);

        model.Context.TotalPages = planned.Count == 0 ? 1 : planned.Count;

        renderer.BeginDocument();
        for (int i = 0; i < planned.Count; i++)
        {
            model.Context.PageNumber = i + 1;
            PlannedPage page = planned[i];

            IRenderContext ctx = renderer.BeginPage(page.Size);

            if (page.Background is PdfColor bg && !bg.IsTransparent)
                ctx.FillRectangle(new PdfRect(0f, 0f, page.Size.Width, page.Size.Height), bg);

            foreach (PlacedItem item in page.Items)
            {
                item.Element.Arrange(item.Bounds);
                item.Element.Render(ctx);
            }

            renderer.EndPage();
        }

        return renderer.EndDocument();
    }

    /// <summary>Passagem 1: calcula as páginas físicas e as posições de tudo.</summary>
    public static List<PlannedPage> Plan(DocumentModel model)
    {
        var output = new List<PlannedPage>();
        foreach (PageModel page in model.Pages)
            PlanSection(page, output);
        return output;
    }

    static void PlanSection(PageModel page, List<PlannedPage> output)
    {
        var pageRect = new PdfRect(0f, 0f, page.Size.Width, page.Size.Height);
        PdfRect area = pageRect.Deflate(page.Margin);

        float headerHeight = page.Header?.Measure(new PdfSize(area.Width, PdfSize.Infinity)).Height ?? 0f;
        float footerHeight = page.Footer?.Measure(new PdfSize(area.Width, PdfSize.Infinity)).Height ?? 0f;

        float contentTop = area.Top + headerHeight;
        float contentBottom = area.Bottom - footerHeight;
        float contentWidth = area.Width;
        float contentLeft = area.Left;

        var headerBounds = new PdfRect(area.Left, area.Top, area.Width, headerHeight);
        var footerBounds = new PdfRect(area.Left, contentBottom, area.Width, footerHeight);

        List<FlowItem> items = Flatten(page.Content, contentWidth);

        PlannedPage current = NewPage(page, headerBounds, footerBounds);
        output.Add(current);

        float y = contentTop;
        var activeHeaders = new List<FlowItem>();
        int activeGroupId = 0;

        foreach (FlowItem item in items)
        {
            // Rastreia cabeçalho de tabela ativo (para repetição).
            if (item.Kind == FlowItemKind.TableHeader)
            {
                if (item.GroupId != activeGroupId)
                {
                    activeHeaders.Clear();
                    activeGroupId = item.GroupId;
                }
                activeHeaders.Add(item);
            }
            else if (item.GroupId != activeGroupId)
            {
                activeHeaders.Clear();
                activeGroupId = 0;
            }

            bool fits = y + item.Height <= contentBottom + Epsilon;
            bool pageHasContent = y > contentTop + Epsilon;

            if (!fits && pageHasContent)
            {
                current = NewPage(page, headerBounds, footerBounds);
                output.Add(current);
                y = contentTop;

                // Repete os cabeçalhos da tabela em curso no topo da nova página.
                if (item.Kind == FlowItemKind.TableRow && activeHeaders.Count > 0)
                {
                    foreach (FlowItem hdr in activeHeaders)
                    {
                        current.Items.Add(new PlacedItem(hdr.Element, ItemBounds(hdr, contentLeft, y, contentWidth)));
                        y += hdr.Height;
                    }
                }
            }

            current.Items.Add(new PlacedItem(item.Element, ItemBounds(item, contentLeft, y, contentWidth)));
            y += item.Height;
        }
    }

    static PdfRect ItemBounds(FlowItem item, float contentLeft, float y, float contentWidth)
    {
        float left = contentLeft + item.LeftInset;
        float w = item.Width > 0f ? item.Width : MathF.Max(0f, contentWidth - item.LeftInset);
        return new PdfRect(left, y, w, item.Height);
    }

    static PlannedPage NewPage(PageModel page, PdfRect headerBounds, PdfRect footerBounds)
    {
        var pp = new PlannedPage(page.Size, page.Background);
        if (page.Header is not null && headerBounds.Height > 0f)
            pp.Items.Add(new PlacedItem(page.Header, headerBounds));
        if (page.Footer is not null && footerBounds.Height > 0f)
            pp.Items.Add(new PlacedItem(page.Footer, footerBounds));
        return pp;
    }

    static List<FlowItem> Flatten(ILayoutElement? content, float width)
    {
        var list = new List<FlowItem>();
        if (content is null)
            return list;

        if (content is IFlowContainer flow)
        {
            foreach (FlowItem item in flow.Flatten(width))
                list.Add(item);
        }
        else
        {
            float h = content.Measure(new PdfSize(width, PdfSize.Infinity)).Height;
            list.Add(new FlowItem(content, h));
        }

        return list;
    }
}

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
        var openRuns = new List<DecorationRun>();
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
                // Decorações abertas terminam nesta página e reabrem (com cantos novos) na próxima.
                CloseRuns(current, openRuns, 0);
                current = NewPage(page, headerBounds, footerBounds);
                output.Add(current);
                y = contentTop;

                // Repete os cabeçalhos da tabela em curso no topo da nova página.
                if (item.Kind == FlowItemKind.TableRow && activeHeaders.Count > 0)
                {
                    foreach (FlowItem hdr in activeHeaders)
                    {
                        Place(current, openRuns, hdr, ItemBounds(hdr, contentLeft, y, contentWidth), contentLeft);
                        y += hdr.Height;
                    }
                }
            }

            Place(current, openRuns, item, ItemBounds(item, contentLeft, y, contentWidth), contentLeft);
            y += item.Height;
        }

        CloseRuns(current, openRuns, 0);
    }

    /// <summary>Trecho contíguo de uma decoração na página corrente, ainda aberto.</summary>
    sealed class DecorationRun
    {
        public required FlowDecoration Decoration;
        /// <summary>Índice reservado em Items para fundos (pintados antes do conteúdo); -1 para bordas.</summary>
        public required int PlaceholderIndex;
        public required float Left;
        public required float Width;
        public required float Top;
        public float Bottom;
    }

    /// <summary>
    /// Posiciona um item, abrindo e fechando os trechos de decoração conforme a cadeia de decorações
    /// do item. Cada decoração é desenhada uma única vez por página sobre a união das fatias contíguas.
    /// </summary>
    static void Place(PlannedPage page, List<DecorationRun> open, FlowItem item, PdfRect bounds, float contentLeft)
    {
        IReadOnlyList<FlowDecorationFrame>? chain = item.Decorations;
        int count = chain?.Count ?? 0;

        int common = 0;
        while (common < open.Count && common < count && ReferenceEquals(open[common].Decoration, chain![common].Decoration))
            common++;

        CloseRuns(page, open, common);

        for (int i = common; i < count; i++)
        {
            FlowDecorationFrame frame = chain![i];
            int placeholder = -1;
            if (!frame.Decoration.DrawOverContent)
            {
                placeholder = page.Items.Count;
                page.Items.Add(new PlacedItem(frame.Decoration.Element, default));
            }

            open.Add(new DecorationRun
            {
                Decoration = frame.Decoration,
                PlaceholderIndex = placeholder,
                Left = contentLeft + frame.LeftInset,
                Width = frame.Width,
                Top = bounds.Top,
            });
        }

        page.Items.Add(new PlacedItem(item.Element, bounds));

        foreach (DecorationRun run in open)
            run.Bottom = bounds.Bottom;
    }

    /// <summary>Fecha os trechos abertos a partir de <paramref name="keep"/>, do mais interno ao mais externo.</summary>
    static void CloseRuns(PlannedPage page, List<DecorationRun> open, int keep)
    {
        for (int i = open.Count - 1; i >= keep; i--)
        {
            DecorationRun run = open[i];
            var rect = new PdfRect(run.Left, run.Top, run.Width, MathF.Max(0f, run.Bottom - run.Top));
            var placed = new PlacedItem(run.Decoration.Element, rect);

            if (run.PlaceholderIndex >= 0)
                page.Items[run.PlaceholderIndex] = placed;
            else
                page.Items.Add(placed);

            open.RemoveAt(i);
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

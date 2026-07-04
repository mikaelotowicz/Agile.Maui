using System.Collections.Generic;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;

namespace Agile.Maui.PdfGen.Layout.Elements;

/// <summary>Conteúdo de uma célula: elemento + fundo opcional.</summary>
public sealed class TableCell
{
    public ILayoutElement Content { get; }
    public PdfColor? Background { get; }

    public TableCell(ILayoutElement content, PdfColor? background = null)
    {
        Content = content;
        Background = background;
    }
}

/// <summary>Uma linha da tabela desenhada com larguras de coluna pré-calculadas, padding, fundo e grade.</summary>
public sealed class TableRowElement : Element
{
    readonly IReadOnlyList<TableCell> _cells;
    readonly float[] _columnWidths;
    readonly Edges _cellPadding;
    readonly float _borderThickness;
    readonly PdfColor _borderColor;

    public TableRowElement(
        IReadOnlyList<TableCell> cells,
        float[] columnWidths,
        Edges cellPadding,
        float borderThickness,
        PdfColor borderColor)
    {
        _cells = cells;
        _columnWidths = columnWidths;
        _cellPadding = cellPadding;
        _borderThickness = borderThickness;
        _borderColor = borderColor;
    }

    public override PdfSize Measure(PdfSize available)
    {
        float contentHeight = 0f;
        int count = System.Math.Min(_cells.Count, _columnWidths.Length);
        for (int i = 0; i < count; i++)
        {
            float innerWidth = MathF.Max(0f, _columnWidths[i] - _cellPadding.Horizontal);
            PdfSize size = _cells[i].Content.Measure(new PdfSize(innerWidth, PdfSize.Infinity));
            if (size.Height > contentHeight)
                contentHeight = size.Height;
        }

        float totalWidth = 0f;
        foreach (float w in _columnWidths)
            totalWidth += w;

        return new PdfSize(totalWidth, contentHeight + _cellPadding.Vertical);
    }

    protected override void ArrangeCore(PdfRect bounds)
    {
        float x = bounds.Left;
        int count = System.Math.Min(_cells.Count, _columnWidths.Length);
        for (int i = 0; i < count; i++)
        {
            var cellRect = new PdfRect(x, bounds.Top, _columnWidths[i], bounds.Height);
            _cells[i].Content.Arrange(cellRect.Deflate(_cellPadding));
            x += _columnWidths[i];
        }
    }

    public override void Render(IRenderContext context)
    {
        float x = Bounds.Left;
        int count = System.Math.Min(_cells.Count, _columnWidths.Length);

        for (int i = 0; i < count; i++)
        {
            var cellRect = new PdfRect(x, Bounds.Top, _columnWidths[i], Bounds.Height);

            if (_cells[i].Background is PdfColor bg && !bg.IsTransparent)
                context.FillRectangle(cellRect, bg);

            if (_borderThickness > 0f && !_borderColor.IsTransparent)
                context.DrawRectangle(cellRect, _borderColor, _borderThickness);

            _cells[i].Content.Render(context);
            x += _columnWidths[i];
        }
    }
}

using System.Collections.Generic;
using System.Threading;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;

namespace Agile.Maui.PdfGen.Layout.Elements;

/// <summary>Largura de uma coluna: constante (pontos) ou relativa (peso do espaço restante).</summary>
public readonly struct TableColumnWidth
{
    public readonly float? FixedWidth;
    public readonly float Weight;

    private TableColumnWidth(float? fixedWidth, float weight)
    {
        FixedWidth = fixedWidth;
        Weight = weight;
    }

    public static TableColumnWidth Constant(float points) => new(points, 0f);
    public static TableColumnWidth Relative(float weight) => new(null, weight);
}

/// <summary>Tabela com cabeçalho repetível, células com padding/borda/fundo e larguras fixas ou relativas.</summary>
public sealed class TableElement : Element, IFlowContainer
{
    static int _nextGroupId = 1;

    readonly List<TableColumnWidth> _columns;
    readonly List<List<TableCell>> _headerRows;
    readonly List<List<TableCell>> _bodyRows;
    readonly Edges _cellPadding;
    readonly float _borderThickness;
    readonly PdfColor _borderColor;
    readonly int _groupId;

    float[] _widths = System.Array.Empty<float>();
    List<TableRowElement>? _allRows;

    public TableElement(
        List<TableColumnWidth> columns,
        List<List<TableCell>> headerRows,
        List<List<TableCell>> bodyRows,
        Edges cellPadding,
        float borderThickness,
        PdfColor borderColor)
    {
        _columns = columns;
        _headerRows = headerRows;
        _bodyRows = bodyRows;
        _cellPadding = cellPadding;
        _borderThickness = borderThickness;
        _borderColor = borderColor;
        _groupId = Interlocked.Increment(ref _nextGroupId);
    }

    void ComputeWidths(float availableWidth)
    {
        _widths = new float[_columns.Count];
        float fixedTotal = 0f;
        float weightTotal = 0f;

        foreach (TableColumnWidth col in _columns)
        {
            if (col.FixedWidth is float c)
                fixedTotal += c;
            else
                weightTotal += col.Weight;
        }

        float remaining = MathF.Max(0f, availableWidth - fixedTotal);
        for (int i = 0; i < _columns.Count; i++)
        {
            TableColumnWidth col = _columns[i];
            if (col.FixedWidth is float c)
                _widths[i] = c;
            else
                _widths[i] = weightTotal > 0f ? remaining * (col.Weight / weightTotal) : 0f;
        }
    }

    TableRowElement MakeRow(List<TableCell> cells) =>
        new(cells, _widths, _cellPadding, _borderThickness, _borderColor);

    public override PdfSize Measure(PdfSize available)
    {
        ComputeWidths(available.IsWidthConstrained ? available.Width : SumConstant());
        _allRows = new List<TableRowElement>(_headerRows.Count + _bodyRows.Count);

        float height = 0f;
        foreach (List<TableCell> row in _headerRows)
        {
            TableRowElement e = MakeRow(row);
            height += e.Measure(available).Height;
            _allRows.Add(e);
        }
        foreach (List<TableCell> row in _bodyRows)
        {
            TableRowElement e = MakeRow(row);
            height += e.Measure(available).Height;
            _allRows.Add(e);
        }

        float totalWidth = 0f;
        foreach (float w in _widths)
            totalWidth += w;

        return new PdfSize(totalWidth, height);
    }

    float SumConstant()
    {
        float total = 0f;
        foreach (TableColumnWidth col in _columns)
            total += col.FixedWidth ?? 0f;
        return total;
    }

    protected override void ArrangeCore(PdfRect bounds)
    {
        if (_allRows is null)
            Measure(new PdfSize(bounds.Width, PdfSize.Infinity));

        float y = bounds.Top;
        foreach (TableRowElement row in _allRows!)
        {
            float h = row.Measure(new PdfSize(bounds.Width, PdfSize.Infinity)).Height;
            row.Arrange(new PdfRect(bounds.Left, y, bounds.Width, h));
            y += h;
        }
    }

    public override void Render(IRenderContext context)
    {
        if (_allRows is null)
            return;
        foreach (TableRowElement row in _allRows)
            row.Render(context);
    }

    public IEnumerable<FlowItem> Flatten(float width)
    {
        ComputeWidths(width);

        foreach (List<TableCell> row in _headerRows)
        {
            TableRowElement e = MakeRow(row);
            float h = e.Measure(new PdfSize(width, PdfSize.Infinity)).Height;
            yield return new FlowItem(e, h, FlowItemKind.TableHeader, _groupId, width: width);
        }

        foreach (List<TableCell> row in _bodyRows)
        {
            TableRowElement e = MakeRow(row);
            float h = e.Measure(new PdfSize(width, PdfSize.Infinity)).Height;
            yield return new FlowItem(e, h, FlowItemKind.TableRow, _groupId, width: width);
        }
    }
}

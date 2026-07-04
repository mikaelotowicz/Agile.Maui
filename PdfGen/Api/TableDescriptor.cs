using System.Collections.Generic;
using Agile.Maui.PdfGen.Layout;
using Agile.Maui.PdfGen.Layout.Elements;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Text;

namespace Agile.Maui.PdfGen.Api;

public interface ITableColumnsBuilder
{
    /// <summary>Coluna que divide o espaço restante conforme o peso.</summary>
    void RelativeColumn(float weight = 1f);
    /// <summary>Coluna de largura fixa em pontos.</summary>
    void ConstantColumn(float width);
}

public interface ITableRowBuilder
{
    /// <summary>Adiciona uma célula na próxima coluna e devolve seu container.</summary>
    IContainer Cell();
    /// <summary>Célula com cor de fundo.</summary>
    IContainer Cell(PdfColor background);
}

public interface ITableDescriptor
{
    void Columns(System.Action<ITableColumnsBuilder> build);
    /// <summary>Define uma linha de cabeçalho (repetida em cada página). Pode ser chamado várias vezes.</summary>
    void Header(System.Action<ITableRowBuilder> build);
    /// <summary>Adiciona uma linha de corpo.</summary>
    void Row(System.Action<ITableRowBuilder> build);
    void CellPadding(float padding);
    void Border(float thickness, PdfColor color);
}

internal sealed class TableColumnsBuilder : ITableColumnsBuilder
{
    public List<TableColumnWidth> Columns { get; } = new();
    public void RelativeColumn(float weight = 1f) => Columns.Add(TableColumnWidth.Relative(weight));
    public void ConstantColumn(float width) => Columns.Add(TableColumnWidth.Constant(width));
}

internal sealed class TableRowBuilder : ITableRowBuilder
{
    readonly TextStyle _style;
    readonly PageContext _ctx;
    public List<(Container container, PdfColor? background)> Cells { get; } = new();

    public TableRowBuilder(TextStyle style, PageContext ctx)
    {
        _style = style;
        _ctx = ctx;
    }

    public IContainer Cell()
    {
        var c = new Container(_style, _ctx);
        Cells.Add((c, null));
        return c;
    }

    public IContainer Cell(PdfColor background)
    {
        var c = new Container(_style, _ctx);
        Cells.Add((c, background));
        return c;
    }

    public List<TableCell> Build()
    {
        var list = new List<TableCell>(Cells.Count);
        foreach (var (container, bg) in Cells)
            list.Add(new TableCell(container.Build(), bg));
        return list;
    }
}

internal sealed class TableDescriptor : ITableDescriptor
{
    readonly TextStyle _style;
    readonly PageContext _ctx;
    readonly List<TableColumnWidth> _columns = new();
    readonly List<List<TableCell>> _headerRows = new();
    readonly List<List<TableCell>> _bodyRows = new();
    float _cellPadding = 4f;
    float _borderThickness = 0.5f;
    PdfColor _borderColor = Colors.LightGray;

    public TableDescriptor(TextStyle style, PageContext ctx)
    {
        _style = style;
        _ctx = ctx;
    }

    public void Columns(System.Action<ITableColumnsBuilder> build)
    {
        var b = new TableColumnsBuilder();
        build(b);
        _columns.Clear();
        _columns.AddRange(b.Columns);
    }

    public void Header(System.Action<ITableRowBuilder> build)
    {
        var b = new TableRowBuilder(_style, _ctx);
        build(b);
        _headerRows.Add(b.Build());
    }

    public void Row(System.Action<ITableRowBuilder> build)
    {
        var b = new TableRowBuilder(_style, _ctx);
        build(b);
        _bodyRows.Add(b.Build());
    }

    public void CellPadding(float padding) => _cellPadding = padding;

    public void Border(float thickness, PdfColor color)
    {
        _borderThickness = thickness;
        _borderColor = color;
    }

    internal ILayoutElement Build()
    {
        // Sem colunas definidas: infere pelo maior número de células.
        if (_columns.Count == 0)
        {
            int max = 0;
            foreach (List<TableCell> r in _headerRows)
                max = System.Math.Max(max, r.Count);
            foreach (List<TableCell> r in _bodyRows)
                max = System.Math.Max(max, r.Count);
            for (int i = 0; i < max; i++)
                _columns.Add(TableColumnWidth.Relative(1f));
        }

        return new TableElement(
            _columns, _headerRows, _bodyRows,
            Edges.All(_cellPadding), _borderThickness, _borderColor);
    }
}

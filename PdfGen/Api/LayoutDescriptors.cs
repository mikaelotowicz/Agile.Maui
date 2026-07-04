using System.Collections.Generic;
using Agile.Maui.PdfGen.Layout;
using Agile.Maui.PdfGen.Layout.Elements;
using Agile.Maui.PdfGen.Text;

namespace Agile.Maui.PdfGen.Api;

// ---- Column ----

public interface IColumnDescriptor
{
    /// <summary>Adiciona um item vertical e devolve seu container.</summary>
    IContainer Item();
    /// <summary>Espaçamento vertical entre itens.</summary>
    void Spacing(float spacing);
}

internal sealed class ColumnDescriptor : IColumnDescriptor
{
    readonly TextStyle _style;
    readonly PageContext _ctx;
    readonly List<Container> _items = new();
    float _spacing;

    public ColumnDescriptor(TextStyle style, PageContext ctx)
    {
        _style = style;
        _ctx = ctx;
    }

    public IContainer Item()
    {
        var c = new Container(_style, _ctx);
        _items.Add(c);
        return c;
    }

    public void Spacing(float spacing) => _spacing = spacing;

    internal ILayoutElement Build()
    {
        var children = new List<ILayoutElement>(_items.Count);
        foreach (Container c in _items)
            children.Add(c.Build());
        return new ColumnElement(children, _spacing);
    }
}

// ---- Row ----

public interface IRowDescriptor
{
    /// <summary>Item que divide o espaço restante conforme o peso.</summary>
    IContainer RelativeItem(float weight = 1f);
    /// <summary>Item de largura fixa em pontos.</summary>
    IContainer ConstantItem(float width);
    /// <summary>Espaçamento horizontal entre itens.</summary>
    void Spacing(float spacing);
}

internal sealed class RowDescriptor : IRowDescriptor
{
    readonly TextStyle _style;
    readonly PageContext _ctx;
    readonly List<(Container container, float? constant, float weight)> _items = new();
    float _spacing;

    public RowDescriptor(TextStyle style, PageContext ctx)
    {
        _style = style;
        _ctx = ctx;
    }

    public IContainer RelativeItem(float weight = 1f)
    {
        var c = new Container(_style, _ctx);
        _items.Add((c, null, weight));
        return c;
    }

    public IContainer ConstantItem(float width)
    {
        var c = new Container(_style, _ctx);
        _items.Add((c, width, 0f));
        return c;
    }

    public void Spacing(float spacing) => _spacing = spacing;

    internal ILayoutElement Build()
    {
        var items = new List<RowItem>(_items.Count);
        foreach (var (container, constant, weight) in _items)
            items.Add(new RowItem(container.Build(), constant, weight));
        return new RowElement(items, _spacing);
    }
}

// ---- Stack (sobreposição) ----

public interface IStackDescriptor
{
    IContainer Item();
}

internal sealed class StackDescriptor : IStackDescriptor
{
    readonly TextStyle _style;
    readonly PageContext _ctx;
    readonly List<Container> _items = new();

    public StackDescriptor(TextStyle style, PageContext ctx)
    {
        _style = style;
        _ctx = ctx;
    }

    public IContainer Item()
    {
        var c = new Container(_style, _ctx);
        _items.Add(c);
        return c;
    }

    internal ILayoutElement Build()
    {
        var children = new List<ILayoutElement>(_items.Count);
        foreach (Container c in _items)
            children.Add(c.Build());
        return new StackElement(children);
    }
}

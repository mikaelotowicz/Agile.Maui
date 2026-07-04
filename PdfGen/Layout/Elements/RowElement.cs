using System.Collections.Generic;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;

namespace Agile.Maui.PdfGen.Layout.Elements;

/// <summary>Uma célula de uma Row: largura fixa (constante) ou relativa (peso).</summary>
public sealed class RowItem
{
    public ILayoutElement Element { get; }
    public float? ConstantWidth { get; }
    public float RelativeWeight { get; }

    public RowItem(ILayoutElement element, float? constantWidth, float relativeWeight)
    {
        Element = element;
        ConstantWidth = constantWidth;
        RelativeWeight = relativeWeight;
    }
}

/// <summary>Dispõe filhos horizontalmente. Larguras fixas primeiro; o resto é dividido por peso.</summary>
public sealed class RowElement : Element
{
    readonly List<RowItem> _items;
    readonly float _spacing;
    float[] _widths = System.Array.Empty<float>();

    public RowElement(List<RowItem> items, float spacing)
    {
        _items = items;
        _spacing = spacing;
    }

    public override PdfSize Measure(PdfSize available)
    {
        ComputeWidths(available.Width);

        float height = 0f;
        for (int i = 0; i < _items.Count; i++)
        {
            PdfSize size = _items[i].Element.Measure(new PdfSize(_widths[i], available.Height));
            if (size.Height > height)
                height = size.Height;
        }

        float totalWidth = available.IsWidthConstrained ? available.Width : SumWidths();
        return new PdfSize(totalWidth, height);
    }

    void ComputeWidths(float availableWidth)
    {
        _widths = new float[_items.Count];
        float totalSpacing = _spacing * MathF.Max(0, _items.Count - 1);
        float fixedTotal = 0f;
        float weightTotal = 0f;

        foreach (RowItem item in _items)
        {
            if (item.ConstantWidth is float c)
                fixedTotal += c;
            else
                weightTotal += item.RelativeWeight;
        }

        float remaining = MathF.Max(0f, availableWidth - fixedTotal - totalSpacing);

        for (int i = 0; i < _items.Count; i++)
        {
            RowItem item = _items[i];
            if (item.ConstantWidth is float c)
                _widths[i] = c;
            else
                _widths[i] = weightTotal > 0f ? remaining * (item.RelativeWeight / weightTotal) : 0f;
        }
    }

    float SumWidths()
    {
        float total = _spacing * MathF.Max(0, _items.Count - 1);
        foreach (float w in _widths)
            total += w;
        return total;
    }

    protected override void ArrangeCore(PdfRect bounds)
    {
        if (_widths.Length != _items.Count)
            ComputeWidths(bounds.Width);

        float x = bounds.Left;
        for (int i = 0; i < _items.Count; i++)
        {
            _items[i].Element.Arrange(new PdfRect(x, bounds.Top, _widths[i], bounds.Height));
            x += _widths[i] + _spacing;
        }
    }

    public override void Render(IRenderContext context)
    {
        foreach (RowItem item in _items)
            item.Element.Render(context);
    }
}

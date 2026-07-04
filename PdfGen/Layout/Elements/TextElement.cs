using System.Collections.Generic;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;
using Agile.Maui.PdfGen.Text;

namespace Agile.Maui.PdfGen.Layout.Elements;

/// <summary>Texto multi-linha com wrap automático e alinhamento. Fatiável em linhas para quebra de página.</summary>
public sealed class TextElement : Element, IFlowContainer
{
    readonly string _text;
    readonly TextStyle _style;
    readonly TextAlign _align;

    List<TextLine>? _lines;
    float _measuredWidth;

    public TextElement(string text, TextStyle style, TextAlign align = TextAlign.Left)
    {
        _text = text ?? string.Empty;
        _style = style;
        _align = align;
    }

    public override PdfSize Measure(PdfSize available)
    {
        float maxWidth = available.IsWidthConstrained ? available.Width : float.MaxValue;
        _lines = TextLayout.Wrap(_text, _style, maxWidth);

        float width = 0f;
        foreach (TextLine line in _lines)
        {
            if (line.Width > width)
                width = line.Width;
        }

        _measuredWidth = available.IsWidthConstrained ? available.Width : width;
        float height = _lines.Count * _style.LineSpacing;
        return new PdfSize(width, height);
    }

    public IEnumerable<FlowItem> Flatten(float width)
    {
        List<TextLine> lines = TextLayout.Wrap(_text, _style, width);
        foreach (TextLine line in lines)
        {
            var single = new SingleLineElement(line, _style, _align, width);
            yield return new FlowItem(single, _style.LineSpacing, width: width);
        }
    }

    public override void Render(IRenderContext context)
    {
        if (_lines is null)
            Measure(new PdfSize(Bounds.Width, PdfSize.Infinity));

        float y = Bounds.Top;
        foreach (TextLine line in _lines!)
        {
            SingleLineElement.DrawLine(context, line, _style, _align, Bounds.Left, y, Bounds.Width);
            y += _style.LineSpacing;
        }
    }
}

/// <summary>Uma única linha de texto já quebrada (produto do Flatten).</summary>
internal sealed class SingleLineElement : Element
{
    readonly TextLine _line;
    readonly TextStyle _style;
    readonly TextAlign _align;
    readonly float _width;

    public SingleLineElement(TextLine line, TextStyle style, TextAlign align, float width)
    {
        _line = line;
        _style = style;
        _align = align;
        _width = width;
    }

    public override PdfSize Measure(PdfSize available) => new(_width, _style.LineSpacing);

    public override void Render(IRenderContext context) =>
        DrawLine(context, _line, _style, _align, Bounds.Left, Bounds.Top, Bounds.Width);

    public static void DrawLine(IRenderContext context, TextLine line, TextStyle style, TextAlign align,
        float left, float top, float width)
    {
        if (line.Text.Length == 0)
            return;

        float x = align switch
        {
            TextAlign.Center => left + (width - line.Width) / 2f,
            TextAlign.Right => left + (width - line.Width),
            _ => left,
        };

        // Baseline: topo da linha + ascensão da fonte dentro do line spacing.
        float leading = (style.LineSpacing - style.FontSize) / 2f;
        float baseline = top + leading + style.Ascent * style.FontSize;

        context.DrawText(line.Text, new PdfPoint(x, baseline), style);
    }
}

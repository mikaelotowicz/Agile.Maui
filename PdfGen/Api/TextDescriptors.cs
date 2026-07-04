using Agile.Maui.PdfGen.Layout;
using Agile.Maui.PdfGen.Layout.Elements;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Text;

namespace Agile.Maui.PdfGen.Api;

/// <summary>Bloco de texto configurável, construído no final em um TextElement.</summary>
internal sealed class TextBlockDescriptor : ITextStyleDescriptor
{
    readonly string _text;
    TextStyle _style;
    TextAlign _align = TextAlign.Left;

    public TextBlockDescriptor(string text, TextStyle style)
    {
        _text = text;
        _style = style;
    }

    public ITextStyleDescriptor FontSize(float size) { _style = _style.WithFontSize(size); return this; }
    public ITextStyleDescriptor FontFamily(PdfFontFamily family) { _style = _style.WithFamily(family); return this; }
    public ITextStyleDescriptor Font(EmbeddedFont font) { _style = _style.WithFont(font); return this; }
    public ITextStyleDescriptor Bold() { _style = _style.WithWeight(FontWeight.Bold); return this; }
    public ITextStyleDescriptor Italic() { _style = _style.WithStyle(FontStyle.Italic); return this; }
    public ITextStyleDescriptor FontColor(PdfColor color) { _style = _style.WithColor(color); return this; }
    public ITextStyleDescriptor LineHeight(float ratio) { _style = _style.WithLineHeight(ratio); return this; }
    public ITextStyleDescriptor AlignLeft() { _align = TextAlign.Left; return this; }
    public ITextStyleDescriptor AlignCenter() { _align = TextAlign.Center; return this; }
    public ITextStyleDescriptor AlignRight() { _align = TextAlign.Right; return this; }
    public ITextStyleDescriptor AlignJustify() { _align = TextAlign.Justify; return this; }

    internal ILayoutElement Build() => new TextElement(_text, _style, _align);
}

/// <summary>Número da página configurável, construído em um PageNumberElement.</summary>
internal sealed class PageNumberDescriptor : ITextStyleDescriptor
{
    readonly PageContext _context;
    readonly string _format;
    TextStyle _style;
    TextAlign _align = TextAlign.Left;

    public PageNumberDescriptor(PageContext context, string format, TextStyle style)
    {
        _context = context;
        _format = format;
        _style = style;
    }

    public ITextStyleDescriptor FontSize(float size) { _style = _style.WithFontSize(size); return this; }
    public ITextStyleDescriptor FontFamily(PdfFontFamily family) { _style = _style.WithFamily(family); return this; }
    public ITextStyleDescriptor Font(EmbeddedFont font) { _style = _style.WithFont(font); return this; }
    public ITextStyleDescriptor Bold() { _style = _style.WithWeight(FontWeight.Bold); return this; }
    public ITextStyleDescriptor Italic() { _style = _style.WithStyle(FontStyle.Italic); return this; }
    public ITextStyleDescriptor FontColor(PdfColor color) { _style = _style.WithColor(color); return this; }
    public ITextStyleDescriptor LineHeight(float ratio) { _style = _style.WithLineHeight(ratio); return this; }
    public ITextStyleDescriptor AlignLeft() { _align = TextAlign.Left; return this; }
    public ITextStyleDescriptor AlignCenter() { _align = TextAlign.Center; return this; }
    public ITextStyleDescriptor AlignRight() { _align = TextAlign.Right; return this; }
    public ITextStyleDescriptor AlignJustify() { _align = TextAlign.Justify; return this; }

    internal ILayoutElement Build() => new PageNumberElement(_context, _format, _style, _align);
}

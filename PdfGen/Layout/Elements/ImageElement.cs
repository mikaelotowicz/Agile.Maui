using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;

namespace Agile.Maui.PdfGen.Layout.Elements;

public enum ImageFit
{
    /// <summary>Preenche a largura disponível; a altura segue a proporção.</summary>
    FitWidth,
    /// <summary>Cabe inteira dentro da área preservando a proporção.</summary>
    Contain
}

/// <summary>Desenha uma imagem preservando a proporção.</summary>
public sealed class ImageElement : Element
{
    readonly PdfImage _image;
    readonly ImageFit _fit;
    readonly HorizontalAlignment _align;

    PdfRect _drawRect;

    public ImageElement(PdfImage image, ImageFit fit = ImageFit.FitWidth, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        _image = image;
        _fit = fit;
        _align = align;
    }

    public override PdfSize Measure(PdfSize available)
    {
        float aspect = _image.AspectRatio;

        if (!available.IsWidthConstrained)
        {
            // Sem restrição: usa o tamanho nativo em pontos (72 dpi).
            return new PdfSize(_image.PixelWidth, _image.PixelHeight);
        }

        float width = available.Width;
        float height = width / aspect;

        if (_fit == ImageFit.Contain && available.IsHeightConstrained && height > available.Height)
        {
            height = available.Height;
            width = height * aspect;
        }

        return new PdfSize(available.Width, height);
    }

    protected override void ArrangeCore(PdfRect bounds)
    {
        float aspect = _image.AspectRatio;
        float width = bounds.Width;
        float height = width / aspect;

        if (_fit == ImageFit.Contain && height > bounds.Height)
        {
            height = bounds.Height;
            width = height * aspect;
        }

        float x = _align switch
        {
            HorizontalAlignment.Center => bounds.Left + (bounds.Width - width) / 2f,
            HorizontalAlignment.Right => bounds.Right - width,
            _ => bounds.Left,
        };

        _drawRect = new PdfRect(x, bounds.Top, width, height);
    }

    public override void Render(IRenderContext context) =>
        context.DrawImage(_image, _drawRect);
}

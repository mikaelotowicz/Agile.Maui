using System;
using Agile.Maui.PdfGen.Layout;
using Agile.Maui.PdfGen.Layout.Elements;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;

namespace Agile.Maui.PdfGen.Api;

/// <summary>
/// Superfície fluente que envolve exatamente um filho. Modificadores (Padding, Background, Border,
/// alinhamento, tamanho) retornam um novo container interno para encadear; os métodos de conteúdo
/// (Text, Image, Column, Row, Stack, Table, PageNumber) definem o conteúdo terminal.
/// </summary>
public interface IContainer
{
    // Modificadores — retornam o container interno para continuar o encadeamento.
    IContainer Padding(float all);
    IContainer Padding(float horizontal, float vertical);
    IContainer Padding(float left, float top, float right, float bottom);
    IContainer Background(PdfColor color, float cornerRadius = 0f);
    IContainer Background(GradientBrush brush, float cornerRadius = 0f);
    IContainer Border(float thickness, PdfColor color, float cornerRadius = 0f);
    IContainer Border(float thickness, GradientBrush brush, float cornerRadius = 0f);
    IContainer Width(float width);
    IContainer Height(float height);
    IContainer AlignLeft();
    IContainer AlignCenter();
    IContainer AlignRight();
    IContainer AlignTop();
    IContainer AlignMiddle();
    IContainer AlignBottom();

    // Conteúdo terminal.
    ITextStyleDescriptor Text(string text);
    ITextStyleDescriptor PageNumber(string format = "{0}");
    void Image(PdfImage image, ImageFit fit = ImageFit.FitWidth, HorizontalAlignment align = HorizontalAlignment.Left);
    void Image(byte[] data, ImageFit fit = ImageFit.FitWidth, HorizontalAlignment align = HorizontalAlignment.Left);
    void Column(Action<IColumnDescriptor> build);
    void Row(Action<IRowDescriptor> build);
    void Stack(Action<IStackDescriptor> build);
    void Table(Action<ITableDescriptor> build);

    /// <summary>Escape hatch: define um elemento de layout arbitrário.</summary>
    void Element(ILayoutElement element);
}

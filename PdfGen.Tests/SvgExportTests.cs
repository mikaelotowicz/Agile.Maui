using System.Text;
using System.Xml.Linq;
using Agile.Maui.PdfGen.Api;
using Agile.Maui.PdfGen.Primitives;
using Xunit;

namespace Agile.Maui.PdfGen.Tests;

/// <summary>Exportação SVG: XML bem-formado, dimensões/viewBox, empilhamento de páginas e escapes.</summary>
public class SvgExportTests
{
    static string Svg(System.Action<IDocumentContainer> build) =>
        Encoding.UTF8.GetString(PdfDocument.Create(build).GenerateSvg());

    [Fact]
    public void Svg_e_xml_bem_formado()
    {
        string svg = Svg(doc => doc.Page(page =>
        {
            page.Margin(20f);
            page.Content().Column(col =>
            {
                col.Item().Background(GradientBrush.Linear(Colors.Blue, Colors.White, 0f)).Padding(6f).Text("Título");
                col.Item().Border(1f, Colors.Gray).Padding(4f).Text("Endereço");
                col.Item().Image(TestImages.Rgba2x2());
            });
        }));

        XDocument parsed = XDocument.Parse(svg);   // lança se malformado
        Assert.Equal("svg", parsed.Root!.Name.LocalName);
    }

    [Fact]
    public void Pagina_unica_a4_dita_dimensoes_e_viewbox()
    {
        string svg = Svg(doc => doc.Page(page => page.Content().Text("p1")));

        XElement root = XDocument.Parse(svg).Root!;
        Assert.Equal("595", root.Attribute("width")!.Value);
        Assert.Equal("842", root.Attribute("height")!.Value);
        Assert.Equal("0 0 595 842", root.Attribute("viewBox")!.Value);
    }

    [Fact]
    public void Paginas_empilham_verticalmente_com_gap_de_16()
    {
        string svg = Svg(doc =>
        {
            doc.Page(page => page.Content().Text("p1"));
            doc.Page(page => page.Content().Text("p2"));
        });

        XElement root = XDocument.Parse(svg).Root!;
        Assert.Equal("1700", root.Attribute("height")!.Value);   // 842 + 16 + 842
        Assert.Contains("translate(0,858)", svg);                 // segunda página deslocada
    }

    [Fact]
    public void Conteudo_tem_texto_retangulo_e_gradiente()
    {
        string svg = Svg(doc => doc.Page(page =>
        {
            page.Content().Column(col =>
            {
                col.Item().Background(GradientBrush.Linear(Colors.Blue, Colors.White, 90f)).Height(20f);
                col.Item().Background(GradientBrush.Radial(Colors.White, Colors.Black)).Height(20f);
                col.Item().Background(Colors.LightGray).Padding(4f).Text("texto");
            });
        }));

        Assert.Contains("<text", svg);
        Assert.Contains("<rect", svg);
        Assert.Contains("<linearGradient", svg);
        Assert.Contains("<radialGradient", svg);
        Assert.Contains("gradientUnits=\"userSpaceOnUse\"", svg);
    }

    [Fact]
    public void Caracteres_especiais_de_xml_sao_escapados()
    {
        string svg = Svg(doc => doc.Page(page => page.Content().Text("a<b> & \"q\"")));

        Assert.Contains("a&lt;b&gt; &amp; &quot;q&quot;", svg);
        XDocument.Parse(svg);   // continua bem-formado
    }

    [Fact]
    public void Imagem_vira_data_uri_base64()
    {
        string svg = Svg(doc => doc.Page(page => page.Content().Image(TestImages.Rgba2x2())));

        Assert.Contains("href=\"data:image/png;base64,", svg);
        XDocument.Parse(svg);
    }

    [Fact]
    public void Acentos_preservados_em_utf8()
    {
        string svg = Svg(doc => doc.Page(page => page.Content().Text("Ação çãõé — €")));
        Assert.Contains("Ação çãõé — €", svg);
    }
}

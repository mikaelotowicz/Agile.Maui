using Agile.Maui.PdfGen.Api;
using Agile.Maui.PdfGen.Layout.Elements;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Text;
using System.Globalization;

// Exemplo executavel: gera uma proposta comercial premium em PDF e SVG.
// Demonstra fonte embutida, Unicode, gradientes, tabela, paginacao e agile.png.
// Uso: dotnet run --project PdfGen.Sample [caminho-de-saida.pdf]

string outPath = args.Length > 0
    ? args[0]
    : Path.Combine(Directory.GetCurrentDirectory(), "pedido.pdf");

var midnight = PdfColor.FromHex("#0B1020");
var royal = PdfColor.FromHex("#2563EB");
var cyan = PdfColor.FromHex("#18C5F4");
var violet = PdfColor.FromHex("#7C3AED");
var ink = PdfColor.FromHex("#111827");
var muted = PdfColor.FromHex("#6B7280");
var line = PdfColor.FromHex("#D7DEE8");
var soft = PdfColor.FromHex("#F5F7FB");
var softBlue = PdfColor.FromHex("#EAF4FF");
var success = PdfColor.FromHex("#10B981");
var brasil = CultureInfo.GetCultureInfo("pt-BR");

EmbeddedFont? fonte = TentarCarregarFonteSistema();
byte[] logo = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "agile.png"));

var itens = new[]
{
    new Item("Licenca Agile.Maui.PdfGen Enterprise", "Geracao de PDFs, SVG, fontes embutidas e layout fluente", 1, 2450m),
    new Item("Suite de templates comerciais", "Pedido, proposta, demonstrativo e recibo com identidade visual", 1, 3150m),
    new Item("Implantacao assistida premium", "Integracao, treinamento tecnico e suporte prioritario por 90 dias", 1, 1590m),
};

decimal subtotal = itens.Sum(i => i.Total);
decimal desconto = 640m;
decimal total = subtotal - desconto;

PdfDocument documento = PdfDocument.Create(doc =>
{
    doc.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(32);
        page.DefaultTextStyle(new TextStyle(fontSize: 10.5f));
        page.BackgroundColor(PdfColor.FromHex("#FBFCFE"));

        page.Header()
            .Background(GradientBrush.Linear(midnight, royal, 0f), cornerRadius: 10f)
            .Padding(14)
            .Column(header =>
            {
                header.Spacing(10);

                header.Item().Row(row =>
                {
                    row.Spacing(14);
                    row.ConstantItem(68).Image(logo, ImageFit.Contain, HorizontalAlignment.Left);
                    row.RelativeItem().Column(col =>
                    {
                        col.Spacing(3);
                        Txt(col.Item().Text("PROPOSTA COMERCIAL").FontSize(9).FontColor(PdfColor.FromHex("#B9E6FF")));
                        Txt(col.Item().Text("Agile.Maui.PdfGen Enterprise").Bold().FontSize(22).FontColor(Colors.White));
                        Txt(col.Item().Text("Documento executivo para implantacao de geracao de PDF premium").FontSize(9.2f).FontColor(PdfColor.FromHex("#D9E8FF")));
                    });
                    row.ConstantItem(112)
                        .Background(new PdfColor(255, 255, 255, 26), cornerRadius: 8f)
                        .Border(0.7f, new PdfColor(255, 255, 255, 80), cornerRadius: 8f)
                        .Padding(8)
                        .Column(col =>
                        {
                            col.Spacing(2);
                            Txt(col.Item().Text("VALIDADE").FontSize(8).FontColor(PdfColor.FromHex("#D9E8FF")));
                            Txt(col.Item().Text("15 dias").Bold().FontSize(14).FontColor(Colors.White));
                            Txt(col.Item().Text("#AG-2026-0001").FontSize(8).FontColor(PdfColor.FromHex("#B9E6FF")));
                        });
                });

                header.Item().Row(row =>
                {
                    row.Spacing(8);
                    Pill(row.RelativeItem(), "Backend gerenciado", "PDF + SVG");
                    Pill(row.RelativeItem(), "Sem WebView", "C# puro");
                    Pill(row.RelativeItem(), "Multi-plataforma", ".NET 10 / 11");
                });
            });

        page.Content().Padding(0, 10).Column(body =>
        {
            body.Spacing(9);

            body.Item().Row(row =>
            {
                row.Spacing(12);
                InfoPanel(row.RelativeItem(), "Cliente", "Rafael Silva", "Curitiba, PR", "contato@versatil.dev");
                InfoPanel(row.RelativeItem(), "Fornecedor", "Agile.Maui", "Bibliotecas nativas .NET MAUI", "github.com/mikaelotowicz/agile.maui");
                row.ConstantItem(150)
                    .Background(GradientBrush.Linear(royal, violet, 90f), cornerRadius: 8f)
                    .Padding(11)
                    .Column(col =>
                    {
                        col.Spacing(4);
                        Txt(col.Item().Text("TOTAL DA PROPOSTA").FontSize(8).FontColor(PdfColor.FromHex("#E6F3FF")));
                        Txt(col.Item().Text(Money(total)).Bold().FontSize(21).FontColor(Colors.White)).AlignRight();
                        Txt(col.Item().Text("Pagamento flexivel").FontSize(8.5f).FontColor(PdfColor.FromHex("#D9E8FF"))).AlignRight();
                    });
            });

            body.Item().Row(row =>
            {
                row.Spacing(10);
                Metric(row.RelativeItem(), "Entrega", "10 dias", "primeira versao operacional");
                Metric(row.RelativeItem(), "Cobertura", "4 plataformas", "Android, iOS, Mac e Windows");
                Metric(row.RelativeItem(), "SLA", "90 dias", "suporte premium incluso");
            });

            body.Item().Row(row =>
            {
                SectionTitle(row.RelativeItem(), "Escopo executivo", "O pacote entregue acelera a criacao de PDFs comerciais com layout consistente, fonte embutida, imagem real e alta compatibilidade.");
            });

            body.Item().Row(row =>
            {
                row.Spacing(10);
                Benefit(row.RelativeItem(), "Templates comerciais", "Pedido, proposta, demonstrativo e recibo em uma base fluente.");
                Benefit(row.RelativeItem(), "Performance real", "Streams comprimidos, imagens otimizadas e layout testavel no host.");
                Benefit(row.RelativeItem(), "Qualidade visual", "Gradientes, bordas, alpha solido, tabelas e SVG com a mesma arvore de layout.");
            });

            body.Item()
                .Background(Colors.White, cornerRadius: 8f)
                .Border(0.8f, line, cornerRadius: 8f)
                .Padding(0)
                .Table(t =>
                {
                    t.Columns(c =>
                    {
                        c.ConstantColumn(34);
                        c.RelativeColumn(3.4f);
                        c.RelativeColumn(1.2f);
                        c.RelativeColumn(1.3f);
                    });
                    t.CellPadding(8);
                    t.Border(0.45f, PdfColor.FromHex("#E5EAF2"));
                    t.Header(h =>
                    {
                        HeaderCell(h.Cell(royal), "#");
                        HeaderCell(h.Cell(royal), "Item / descricao");
                        HeaderCell(h.Cell(royal), "Qtd", alignRight: true);
                        HeaderCell(h.Cell(royal), "Total", alignRight: true);
                    });

                    for (int i = 0; i < itens.Length; i++)
                    {
                        Item item = itens[i];
                        PdfColor? bg = i % 2 == 0 ? PdfColor.FromHex("#FAFBFE") : null;
                        t.Row(r =>
                        {
                            Txt(Cell(r, bg).Text((i + 1).ToString()).FontColor(muted));
                            Cell(r, bg).Column(c =>
                            {
                                Txt(c.Item().Text(item.Nome).Bold().FontColor(ink));
                                Txt(c.Item().Text(item.Descricao).FontSize(8.5f).FontColor(muted));
                            });
                            Txt(Cell(r, bg).Text(item.Quantidade.ToString()).FontColor(ink)).AlignRight();
                            Txt(Cell(r, bg).Text(Money(item.Total)).Bold().FontColor(ink)).AlignRight();
                        });
                    }
                });

            body.Item().Row(row =>
            {
                row.Spacing(12);
                row.RelativeItem()
                    .Background(softBlue, cornerRadius: 8f)
                    .Border(0.8f, PdfColor.FromHex("#CFE8FF"), cornerRadius: 8f)
                    .Padding(11)
                    .Column(col =>
                    {
                        col.Spacing(5);
                        Txt(col.Item().Text("Plano de implantacao").Bold().FontSize(12).FontColor(ink));
                        Txt(col.Item().Text("1. Kickoff e parametrizacao do template premium").FontSize(9).FontColor(muted));
                        Txt(col.Item().Text("2. Integracao com dados comerciais e validacao visual").FontSize(9).FontColor(muted));
                        Txt(col.Item().Text("3. Publicacao do pacote e handoff tecnico").FontSize(9).FontColor(muted));
                        Txt(col.Item().Text("Condicoes: 50% no aceite e 50% na entrega homologada.").FontSize(8.4f).FontColor(royal));
                    });

                row.ConstantItem(190)
                    .Background(Colors.White, cornerRadius: 8f)
                    .Border(0.8f, line, cornerRadius: 8f)
                    .Padding(12)
                    .Column(col =>
                    {
                        col.Spacing(5);
                        SummaryLine(col.Item(), "Subtotal", Money(subtotal), muted);
                        SummaryLine(col.Item(), "Desconto", "-" + Money(desconto), success);
                        col.Item().Background(line).Height(0.7f);
                        SummaryLine(col.Item(), "Total", Money(total), ink, bold: true);
                    });
            });
        });

        page.Footer()
            .Border(0.6f, PdfColor.FromHex("#DCE3EE"))
            .Padding(8, 6)
            .Row(row =>
            {
                row.RelativeItem().Text("Agile.Maui.PdfGen - proposta gerada automaticamente").FontColor(muted).FontSize(8.5f);
                row.RelativeItem().AlignRight().PageNumber("Pagina {0} de {1}").FontColor(muted).FontSize(8.5f);
            });
    });
});

documento.Save(outPath);
Console.WriteLine($"PDF gerado em: {outPath}");
Console.WriteLine(fonte is not null
    ? "  (fonte embutida + Unicode ativados)"
    : "  (fonte do sistema nao encontrada; usando base-14)");

string svgPath = Path.ChangeExtension(outPath, ".svg");
File.WriteAllBytes(svgPath, documento.GenerateSvg());
Console.WriteLine($"SVG gerado em: {svgPath}");

ITextStyleDescriptor Txt(ITextStyleDescriptor texto)
{
    if (fonte is not null)
        texto.Font(fonte);
    return texto;
}

void Pill(IContainer c, string title, string detail)
{
    c.Background(new PdfColor(255, 255, 255, 22), cornerRadius: 7f)
        .Border(0.6f, new PdfColor(255, 255, 255, 65), cornerRadius: 7f)
        .Padding(8)
        .Column(col =>
        {
            Txt(col.Item().Text(title).Bold().FontSize(8.8f).FontColor(Colors.White));
            Txt(col.Item().Text(detail).FontSize(7.8f).FontColor(PdfColor.FromHex("#CDEBFF")));
        });
}

void InfoPanel(IContainer c, string title, string line1, string line2, string line3)
{
    c.Background(Colors.White, cornerRadius: 8f)
        .Border(0.8f, line, cornerRadius: 8f)
        .Padding(11)
        .Column(col =>
        {
            col.Spacing(3);
            Txt(col.Item().Text(title.ToUpperInvariant()).FontSize(8).FontColor(royal));
            Txt(col.Item().Text(line1).Bold().FontSize(12.5f).FontColor(ink));
            Txt(col.Item().Text(line2).FontSize(9).FontColor(muted));
            Txt(col.Item().Text(line3).FontSize(8.5f).FontColor(muted));
        });
}

void Metric(IContainer c, string label, string value, string hint)
{
    c.Background(Colors.White, cornerRadius: 8f)
        .Border(0.8f, line, cornerRadius: 8f)
        .Padding(11)
        .Column(col =>
        {
            col.Spacing(2);
            Txt(col.Item().Text(label.ToUpperInvariant()).FontSize(8).FontColor(muted));
            Txt(col.Item().Text(value).Bold().FontSize(16).FontColor(royal));
            Txt(col.Item().Text(hint).FontSize(8.2f).FontColor(muted));
        });
}

void SectionTitle(IContainer c, string title, string subtitle)
{
    c.Background(Colors.White, cornerRadius: 8f)
        .Border(0.8f, line, cornerRadius: 8f)
        .Padding(10)
        .Row(row =>
        {
            row.Spacing(10);
            row.ConstantItem(4).Background(GradientBrush.Linear(cyan, violet, 90f), cornerRadius: 2f).Height(32);
            row.RelativeItem().Column(col =>
            {
                Txt(col.Item().Text(title).Bold().FontSize(13).FontColor(ink));
                Txt(col.Item().Text(subtitle).FontSize(9).FontColor(muted));
            });
        });
}

void Benefit(IContainer c, string title, string description)
{
    c.Background(soft, cornerRadius: 7f)
        .Border(0.6f, PdfColor.FromHex("#E6ECF4"), cornerRadius: 7f)
        .Padding(9)
        .Column(col =>
        {
            col.Spacing(3);
            Txt(col.Item().Text(title).Bold().FontSize(9.5f).FontColor(ink));
            Txt(col.Item().Text(description).FontSize(8.3f).FontColor(muted));
        });
}

void HeaderCell(IContainer c, string text, bool alignRight = false)
{
    ITextStyleDescriptor header = Txt(c.Text(text).Bold().FontColor(Colors.White).FontSize(8.5f));
    if (alignRight)
        header.AlignRight();
}

IContainer Cell(ITableRowBuilder row, PdfColor? background) =>
    background is null ? row.Cell() : row.Cell(background.Value);

void SummaryLine(IContainer c, string label, string value, PdfColor color, bool bold = false)
{
    c.Row(row =>
    {
        Txt(row.RelativeItem().Text(label).FontColor(muted).FontSize(9));
        ITextStyleDescriptor amount = Txt(row.RelativeItem().AlignRight().Text(value).FontColor(color).FontSize(bold ? 13 : 9.5f));
        if (bold)
            amount.Bold();
    });
}

string Money(decimal value) => value.ToString("C", brasil);

static EmbeddedFont? TentarCarregarFonteSistema()
{
    string[] candidatos =
    {
        @"C:\Windows\Fonts\arial.ttf",
        "/System/Library/Fonts/Supplemental/Arial.ttf",
        "/Library/Fonts/Arial.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
    };

    foreach (string caminho in candidatos)
    {
        try
        {
            if (File.Exists(caminho))
                return EmbeddedFont.FromFile(caminho);
        }
        catch
        {
            // Tenta a proxima fonte candidata.
        }
    }
    return null;
}

readonly record struct Item(string Nome, string Descricao, int Quantidade, decimal ValorUnitario)
{
    public decimal Total => Quantidade * ValorUnitario;
}

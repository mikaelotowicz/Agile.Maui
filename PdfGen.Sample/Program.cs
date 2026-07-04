using System.IO.Compression;
using Agile.Maui.PdfGen.Api;
using Agile.Maui.PdfGen.Layout.Elements;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Text;

// Exemplo executável: gera um pedido de venda em PDF (e o mesmo documento em SVG),
// demonstrando fonte embutida + Unicode, gradientes e imagem PNG com transparência.
// Uso: dotnet run --project PdfGen.Sample [caminho-de-saida.pdf]

string outPath = args.Length > 0
    ? args[0]
    : Path.Combine(Directory.GetCurrentDirectory(), "pedido.pdf");

var blue = PdfColor.FromHex("#0D6EFD");
var deepBlue = PdfColor.FromHex("#0A3D91");
var zebra = PdfColor.FromHex("#F8F9FA");

// Fonte embutida (opcional): se não achar uma fonte do sistema, usa as base-14.
EmbeddedFont? fonte = TentarCarregarFonteSistema();
byte[] logo = GerarLogoPng(56);

PdfDocument documento = PdfDocument.Create(doc =>
{
    doc.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(36);
        page.DefaultTextStyle(new TextStyle(fontSize: 11));

        // Cabeçalho com fundo em gradiente e logo PNG (com transparência).
        page.Header()
            .Background(GradientBrush.Linear(deepBlue, blue, 0f), cornerRadius: 8f)
            .Padding(14)
            .Row(row =>
            {
                row.ConstantItem(56).AlignMiddle().Image(logo, ImageFit.Contain, HorizontalAlignment.Left);
                row.RelativeItem().Column(col =>
                {
                    AplicarFonte(col.Item().Text("Pedido de Venda").Bold().FontSize(22).FontColor(Colors.White));
                    AplicarFonte(col.Item().Text("Sistema Versátil — Agile.Maui.PdfGen").FontColor(Colors.White).FontSize(9));
                });
            });

        page.Content().Padding(0, 14).Column(col =>
        {
            col.Spacing(10);

            col.Item().Row(row =>
            {
                row.Spacing(12);
                row.RelativeItem().Background(PdfColor.FromHex("#F1F3F5"), 6).Padding(10).Column(c =>
                {
                    c.Item().Text("Cliente").Bold();
                    c.Item().Text("Micael Otowicz");
                    c.Item().Text("Rua das Flores, 123 — Curitiba/PR");
                });

                // Caixa de total com borda em gradiente.
                row.ConstantItem(180)
                   .Border(2f, GradientBrush.Linear(blue, deepBlue, 90f), cornerRadius: 6f)
                   .Padding(10).Column(c =>
                {
                    c.Item().Text("Pedido nº").Bold();
                    c.Item().Text("#2026-0001");
                    c.Item().Text("Data: 02/07/2026");
                    // Símbolos Unicode só aparecem com fonte embutida.
                    if (fonte is not null)
                        c.Item().Text("Total: € 1.250,00  ✓ conferido").Font(fonte).FontColor(deepBlue);
                });
            });

            col.Item().Text("Itens").Bold().FontSize(14);

            col.Item().Table(t =>
            {
                t.Columns(c =>
                {
                    c.ConstantColumn(40);
                    c.RelativeColumn(3);
                    c.RelativeColumn();
                    c.RelativeColumn();
                });
                t.CellPadding(6);
                t.Border(0.5f, Colors.LightGray);
                t.Header(h =>
                {
                    h.Cell(blue).Text("#").Bold().FontColor(Colors.White);
                    h.Cell(blue).Text("Produto").Bold().FontColor(Colors.White);
                    h.Cell(blue).Text("Qtd").Bold().FontColor(Colors.White).AlignRight();
                    h.Cell(blue).Text("Total").Bold().FontColor(Colors.White).AlignRight();
                });

                for (int i = 1; i <= 60; i++)
                {
                    PdfColor? bg = i % 2 == 0 ? zebra : null;
                    t.Row(r =>
                    {
                        (bg is null ? r.Cell() : r.Cell(bg.Value)).Text(i.ToString());
                        (bg is null ? r.Cell() : r.Cell(bg.Value)).Text($"Produto de exemplo {i} — descrição");
                        (bg is null ? r.Cell() : r.Cell(bg.Value)).Text((i % 5 + 1).ToString()).AlignRight();
                        (bg is null ? r.Cell() : r.Cell(bg.Value)).Text($"R$ {(i * 12.5m):0.00}").AlignRight();
                    });
                }
            });
        });

        page.Footer().Row(row =>
        {
            row.RelativeItem().Text("Agile.Maui.PdfGen").FontColor(Colors.Gray).FontSize(9);
            row.RelativeItem().AlignRight().PageNumber("Página {0} de {1}").FontColor(Colors.Gray).FontSize(9);
        });
    });
});

documento.Save(outPath);
Console.WriteLine($"PDF gerado em: {outPath}");
Console.WriteLine(fonte is not null
    ? "  (fonte embutida + Unicode ativados)"
    : "  (fonte do sistema não encontrada; usando base-14)");

// Mesmo documento exportado como SVG.
string svgPath = Path.ChangeExtension(outPath, ".svg");
File.WriteAllBytes(svgPath, documento.GenerateSvg());
Console.WriteLine($"SVG gerado em: {svgPath}");

// Aplica a fonte embutida ao descritor de texto, se houver.
void AplicarFonte(ITextStyleDescriptor texto)
{
    if (fonte is not null)
        texto.Font(fonte);
}

// Procura uma fonte TrueType comum nas plataformas de desktop.
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
            // fonte inválida/sem glyf — tenta a próxima
        }
    }
    return null;
}

// Gera um logo PNG: círculo azul sobre fundo transparente (demonstra o canal alfa / SMask).
static byte[] GerarLogoPng(int size)
{
    var rgba = new byte[size * size * 4];
    float cx = size / 2f, cy = size / 2f, r = size / 2f - 1f;
    for (int y = 0; y < size; y++)
    {
        for (int x = 0; x < size; x++)
        {
            int p = (y * size + x) * 4;
            float dist = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            bool dentro = dist <= r;
            rgba[p] = 13;      // R
            rgba[p + 1] = 110; // G
            rgba[p + 2] = 253; // B
            rgba[p + 3] = dentro ? (byte)255 : (byte)0; // alfa: transparente fora do círculo
        }
    }
    return MontarPng(size, size, rgba);
}

static byte[] MontarPng(int width, int height, byte[] rgba)
{
    using var ms = new MemoryStream();
    ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

    void BE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    uint UpCrc(uint crc, byte[] d) { foreach (byte x in d) { crc ^= x; for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1; } return crc; }
    void Chunk(string t, byte[] d)
    {
        var l = new byte[4]; BE(l, 0, d.Length); ms.Write(l);
        var tb = System.Text.Encoding.ASCII.GetBytes(t); ms.Write(tb); ms.Write(d);
        uint c = 0xFFFFFFFF; c = UpCrc(c, tb); c = UpCrc(c, d);
        var cb = new byte[4]; BE(cb, 0, (int)(c ^ 0xFFFFFFFF)); ms.Write(cb);
    }

    var ihdr = new byte[13]; BE(ihdr, 0, width); BE(ihdr, 4, height); ihdr[8] = 8; ihdr[9] = 6; Chunk("IHDR", ihdr);

    using var rawMs = new MemoryStream();
    for (int y = 0; y < height; y++) { rawMs.WriteByte(0); rawMs.Write(rgba, y * width * 4, width * 4); }
    var raw = rawMs.ToArray();
    using var comp = new MemoryStream();
    using (var z = new ZLibStream(comp, CompressionLevel.Optimal, true)) z.Write(raw, 0, raw.Length);
    Chunk("IDAT", comp.ToArray());
    Chunk("IEND", System.Array.Empty<byte>());
    return ms.ToArray();
}

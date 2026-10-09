# Agile.Maui.PdfGen

Biblioteca open source de geração de PDF para apps .NET e .NET MAUI, com API
fluente inspirada no QuestPDF, implementação própria e sem SkiaSharp, WebView,
HTML ou dependências comerciais.

- .NET 10 e .NET 11
- Android, iOS, Mac Catalyst, Windows e hosts .NET comuns
- Motor de layout independente de plataforma, testável no host
- Escritor PDF 100% gerenciado como backend padrão
- Renderers nativos opcionais em apps MAUI: Android `PdfDocument` e iOS/Mac `CGContextPDF`
- Fontes TrueType embutidas com Unicode e subsetting automático no backend gerenciado
- Gradientes, alpha em cores sólidas, PNG com transparência, JPEG e exportação SVG

Este pacote gera PDFs. Para visualizar PDFs em MAUI, use o pacote irmão
`Agile.Maui.Pdf` (`PdfViewer` / `PdfReaderView`).

## Instalação

```powershell
dotnet add package Agile.Maui.PdfGen --version 1.2.0
```

Para projetos .NET 11 preview, use o canal preview:

```powershell
dotnet add package Agile.Maui.PdfGen --version 1.2.0-preview.1
```

O histórico de versões está no `CHANGELOG.md` incluído no pacote.

## Uso básico

```csharp
using Agile.Maui.PdfGen.Api;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Text;

byte[] pdf = PdfDocument.Create(doc =>
{
    doc.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(36);
        page.DefaultTextStyle(new TextStyle(fontSize: 11));

        page.Header()
            .Text("Pedido")
            .Bold()
            .FontSize(22)
            .FontColor(PdfColor.FromHex("#0D6EFD"));

        page.Content().Column(col =>
        {
            col.Spacing(10);
            col.Item().Text("Cliente: Micael Otowicz");

            col.Item().Table(t =>
            {
                t.Columns(c =>
                {
                    c.ConstantColumn(40);
                    c.RelativeColumn(3);
                    c.RelativeColumn();
                });

                t.Header(h =>
                {
                    h.Cell(Colors.LightGray).Text("#").Bold();
                    h.Cell(Colors.LightGray).Text("Produto").Bold();
                    h.Cell(Colors.LightGray).Text("Total").Bold().AlignRight();
                });

                for (int i = 1; i <= 100; i++)
                {
                    t.Row(r =>
                    {
                        r.Cell().Text(i.ToString());
                        r.Cell().Text($"Produto {i}");
                        r.Cell().Text($"R$ {i * 10},00").AlignRight();
                    });
                }
            });
        });

        page.Footer().AlignCenter().PageNumber("Página {0} de {1}");
    });
}).GeneratePdf();
```

`GeneratePdf()` usa o escritor gerenciado e funciona em qualquer host .NET
compatível com os target frameworks do pacote.

Uma instância de `PdfDocument` não é thread-safe: não renderize o mesmo
documento em paralelo. Renders sequenciais da mesma instância são suportados.

## Uso em apps MAUI

Num app MAUI com `ImplicitUsings` (o padrão do template), os usings globais do
MAUI trazem tipos com o mesmo nome de tipos públicos do PdfGen. Usar um desses
nomes com os `using` do PdfGen gera o erro CS0104 (referência ambígua):

| Tipo do PdfGen | Colide com |
|---|---|
| `Agile.Maui.PdfGen.Api.IContainer` | `Microsoft.Maui.IContainer` |
| `Agile.Maui.PdfGen.Text.EmbeddedFont` | `Microsoft.Maui.EmbeddedFont` |
| `Agile.Maui.PdfGen.Primitives.Colors` | `Microsoft.Maui.Graphics.Colors` |
| `Agile.Maui.PdfGen.Primitives.GradientBrush` | `Microsoft.Maui.Controls.GradientBrush` |
| `Agile.Maui.PdfGen.Primitives.GradientStop` | `Microsoft.Maui.Controls.GradientStop` |
| `Agile.Maui.PdfGen.Primitives.HorizontalAlignment` | `Microsoft.Maui.Graphics.HorizontalAlignment` |
| `Agile.Maui.PdfGen.Primitives.VerticalAlignment` | `Microsoft.Maui.Graphics.VerticalAlignment` |
| `Agile.Maui.PdfGen.Primitives.FontWeight` | `Microsoft.Maui.FontWeight` |
| `Agile.Maui.PdfGen.Rendering.ImageFormat` | `Microsoft.Maui.Graphics.ImageFormat` |
| `Agile.Maui.PdfGen.Layout.Element` | `Microsoft.Maui.Controls.Element` |

Mantenha o código de geração de PDF num arquivo próprio e declare, no topo dele,
aliases para os tipos que usar. Um alias tem precedência sobre os usings globais:

```csharp
using Agile.Maui.PdfGen.Api;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Text;

using IContainer = Agile.Maui.PdfGen.Api.IContainer;
using Colors = Agile.Maui.PdfGen.Primitives.Colors;
using GradientBrush = Agile.Maui.PdfGen.Primitives.GradientBrush;
using GradientStop = Agile.Maui.PdfGen.Primitives.GradientStop;
using HorizontalAlignment = Agile.Maui.PdfGen.Primitives.HorizontalAlignment;
using VerticalAlignment = Agile.Maui.PdfGen.Primitives.VerticalAlignment;
using FontWeight = Agile.Maui.PdfGen.Primitives.FontWeight;
using EmbeddedFont = Agile.Maui.PdfGen.Text.EmbeddedFont;
// Só se usar Rendering/Layout diretamente (imagens por formato, elementos customizados):
// using ImageFormat = Agile.Maui.PdfGen.Rendering.ImageFormat;
// using Element = Agile.Maui.PdfGen.Layout.Element;
```

- Só os nomes usados no arquivo precisam de alias; os demais não geram erro.
- Não coloque esses aliases em páginas ou code-behind que usam os tipos do MAUI
  de mesmo nome (`Colors`, `Element` etc.), porque o alias passa a valer no
  arquivo inteiro.
- Alternativa: mantenha a geração de PDF num projeto de biblioteca sem os usings
  globais do MAUI (por exemplo, uma class library `net10.0` sem `UseMaui`) e
  referencie-o no app.
- Outra opção é qualificar o nome completo no ponto de uso, como
  `Agile.Maui.PdfGen.Primitives.Colors.LightGray`.

## Uso fora do MAUI

O pacote não depende de `Microsoft.Maui.Controls` para o backend gerenciado.
Você pode usá-lo em:

- WinForms ou WPF modernos em .NET 10/11
- Blazor Server
- Blazor WebAssembly, gerando `byte[]` para download no navegador
- console apps, workers e APIs

Em Blazor WebAssembly, evite `Save(path)`, porque o browser não tem acesso direto
ao sistema de arquivos. Gere `byte[]` com `GeneratePdf()` e entregue o download
via JS interop ou endpoint.

## Exemplo premium

O projeto `PdfGen.Sample` gera uma proposta comercial premium de uma página,
usando `agile.png` como imagem real no PDF e exportando o mesmo documento para
SVG:

```powershell
dotnet run --project PdfGen.Sample -- output\pdf\premium-proposal.pdf
```

O sample demonstra fonte TrueType embutida, Unicode, PNG com transparência,
gradientes, alpha, cards, tabela, resumo financeiro e numeração de páginas. O
arquivo `agile.png` é copiado para a pasta de saída pelo `PdfGen.Sample.csproj`.

## Backends

### `GeneratePdf()`

Backend recomendado para paridade completa. Ele é 100% gerenciado e suporta:

- fontes base-14 e fontes TrueType embutidas;
- Unicode com `ToUnicode` e subsetting automático;
- JPEG (RGB, escala de cinza e CMYK) e PNG, incluindo PNG com alpha via `SMask`;
- alpha uniforme em texto, linhas, bordas e fundos sólidos;
- gradientes PDF nativos;
- content streams e CMaps `ToUnicode` comprimidos com `FlateDecode`;
- WinForms, Blazor, MAUI e outros hosts .NET.

### `GeneratePdfNative()`

Disponível para apps MAUI que queiram renderizar com APIs nativas de plataforma.
No Android usa `PdfDocument`; no iOS/Mac Catalyst usa `CGContextPDF`; nas demais
plataformas recai no backend gerenciado.

Os renderers nativos são intencionalmente menores e não têm paridade total com o
backend gerenciado:

- fontes embutidas (`.Font(fonte)`) são usadas no desenho — `Typeface` no
  Android, `CGFont`/`CTFont` no iOS/Mac —, mas a gravação da fonte no PDF
  (subconjunto, `ToUnicode`) fica a cargo da API da plataforma; se a fonte não
  puder ser carregada, o texto recai na fonte do sistema;
- gradientes degradam para a cor da primeira parada;
- no Android, o `Typeface` de cada fonte embutida é criado uma vez por processo
  e reaproveitado entre documentos.

Para Unicode/subsetting garantidos e gradientes fiéis, prefira `GeneratePdf()`.

## Fontes embutidas e Unicode

Por padrão, texto usa as fontes base-14 do PDF (Helvetica, Times e Courier).
Para usar caracteres Unicode amplos ou uma fonte própria, carregue uma fonte
TrueType:

```csharp
var fonte = EmbeddedFont.FromFile(@"C:\Windows\Fonts\arial.ttf");
// ou: EmbeddedFont.Load(bytesDaFonte);

page.Content()
    .Text("Relatório 2026 - total € 1.250,00")
    .Font(fonte)
    .FontSize(14);
```

- A fonte é embutida como Type0/CIDFontType2 (`Identity-H`) no backend gerenciado.
- O CMap `ToUnicode` mantém o texto selecionável e pesquisável.
- Apenas os glifos usados são embutidos, reduzindo o tamanho do PDF; o nome da
  fonte recebe o prefixo de subconjunto (`XXXXXX+`) exigido pela especificação.
- O texto com fonte embutida é centrado verticalmente na linha.
- Fontes CFF (`.otf` com assinatura `OTTO`) não são suportadas; use TrueType
  (`glyf`). Fonte truncada ou corrompida gera `InvalidDataException`.

## Texto

O motor inclui wrap automático, quebras de linha explícitas, alinhamento à
esquerda, centro, direita e justificado. O texto justificado distribui espaço
entre palavras nas linhas que foram quebradas automaticamente; a última linha do
parágrafo permanece com alinhamento natural.

O número de página respeita o alinhamento do contêiner, como em
`page.Footer().AlignRight().PageNumber("Página {0} de {1}")`, e também o do
próprio texto, como em `.PageNumber(...).AlignRight()`.

## Cores, alpha e gradientes

```csharp
col.Item()
   .Background(new PdfColor(13, 110, 253, 128), cornerRadius: 6f)
   .Padding(12)
   .Text("Fundo azul com alpha");

col.Item()
   .Background(GradientBrush.Linear(Colors.Blue, Colors.White, 90f), cornerRadius: 6f)
   .Padding(12)
   .Text("Gradiente");
```

No backend gerenciado, cores sólidas com alpha usam `ExtGState`. Gradientes usam
shading patterns nativos do PDF; alpha uniforme entre todas as paradas é aplicado
ao shape inteiro. Alpha diferente por parada de gradiente ainda não é suportado.

## Imagens

```csharp
col.Item().Image(PdfImage.FromFile("logo.png"));
col.Item().Image(bytesDaImagem, ImageFit.Contain, HorizontalAlignment.Center);
```

- JPEG é embutido diretamente com `DCTDecode`, com o colorspace do arquivo
  (`DeviceRGB`, `DeviceGray` ou `DeviceCMYK`).
- PNG é decodificado e embutido com `FlateDecode`.
- PNG com transparência usa `SMask`.
- PNG malformado gera `InvalidDataException`.
- PNG de 16 bits e PNG entrelaçado Adam7 não são suportados pelo backend gerenciado.

## Exportação SVG

O mesmo documento pode ser exportado como SVG:

```csharp
byte[] svg = documento.GenerateSvg();
documento.GenerateSvg(stream);
```

Todas as páginas são empilhadas verticalmente em um único SVG.

## Arquitetura

```text
Document -> Layout Tree -> Measure -> Arrange -> Render Tree -> Renderer
```

Cada elemento implementa `ILayoutElement` (`Measure`, `Arrange`, `Render`) e
cada backend implementa `IRenderContext`. O mesmo motor alimenta PDF gerenciado,
PDF nativo e SVG.

## Recursos

**Estrutura**: documento, página, tamanho, margem, header, footer e content.

**Containers**: row, column, stack, table/cell, padding, alinhamento, width/height
e spacer.

**Texto**: fontes base-14, fontes TrueType embutidas, Unicode, tamanho, bold,
italic, cor, line height, wrap e alinhamento.

**Gráficos**: background, border, cantos arredondados, linhas, alpha sólido,
gradientes, PNG e JPEG.

**Paginação**: quebra automática em fluxos verticais, header/footer repetidos,
cabeçalho de tabela repetido, número de página e wrappers decorativos
(`Background`, `Border` e variantes com gradiente) sobre conteúdo paginável. A
decoração é desenhada uma vez por página sobre o trecho contíguo: um bloco com
cantos arredondados continua sendo um único retângulo, e um bloco partido entre
páginas fecha e reabre os cantos na quebra.

Componentes customizados podem implementar `Element` ou `ILayoutElement` e serem
injetados com `.Element(seuElemento)`. Contêineres pagináveis customizados podem
levar fundos e bordas ao motor por `FlowItem.Decorations` (`FlowDecoration`).

## Licença

MIT - Copyright 2026 Micael Otowicz

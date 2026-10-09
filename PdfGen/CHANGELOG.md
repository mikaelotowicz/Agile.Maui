# Changelog

Todas as mudanças relevantes deste pacote são documentadas aqui.
O formato segue Keep a Changelog e o versionamento é semântico.

## [1.2.0] - 2026-10-09

### Adicionado
- `GeneratePdfNative()` desenha com a fonte embutida (`.Font(EmbeddedFont)`):
  no Android via `Typeface` e no iOS/Mac Catalyst via `CGFont`/`CTFont`. Se a
  fonte não puder ser carregada, o texto recai na fonte do sistema. A gravação
  da fonte no PDF (subconjunto, `ToUnicode`) fica a cargo da API da plataforma.
- Overloads `GlyphWidth(int codepoint)` em `EmbeddedFont`, `StandardFont` e
  `TextStyle`, para medir codepoints fora do BMP.
- `FlowDecoration`, `FlowDecorationFrame` e `FlowItem.Decorations` (com
  `FlowItem.Decorate` e `FlowItem.WithWidth`): contêineres pagináveis
  customizados podem entregar fundos e bordas ao motor, que os desenha uma vez
  por página.
- Documentação XML (IntelliSense) incluída no pacote.

### Corrigido
- `AlignJustify()` agora distribui espaço entre palavras nas linhas quebradas
  automaticamente, em vez de se comportar como alinhamento à esquerda.
- A quebra de palavras longas preserva pares substitutos Unicode, evitando partir
  emojis e codepoints fora do BMP.
- Pontuação WinAnsi fora de Latin-1, como `—`, `–`, aspas tipográficas e `€`,
  agora é codificada corretamente em texto base-14, em vez de virar `?`.
- `PageNumberElement` agora mede texto com `TextStyle.MeasureWidth`, respeitando
  fontes embutidas quando aplicadas ao número de página.
- O escritor PDF gerenciado agora respeita alpha em texto, linhas, bordas e
  fundos sólidos usando `/ExtGState`.
- O renderer Android libera os `Bitmap` decodificados ao finalizar o documento.
- Texto com fonte embutida agora fica centrado na linha (caixa ascensão+descida
  do `hhea`); antes a baseline usava só a ascensão, e fontes como Open Sans
  desciam na linha e invadiam o elemento de baixo. Base-14 não muda.
- O renderer Android cria o `Typeface` de cada fonte embutida uma única vez por
  processo (chave = conteúdo da fonte), em vez de um por documento. O Android
  nunca libera um `Typeface` de `CreateFromFile`: cada `GeneratePdfNative()` com
  fonte embutida vazava ~60 KB de heap nativo e mantinha mapeado o `.ttf`
  temporário já apagado.
- `Background`/`Border` (e variantes com gradiente) com `cornerRadius` sobre
  conteúdo paginável, como `.Background(cor, 8f).Padding(12).Text(...)`, agora
  desenham um único retângulo arredondado por página sobre as fatias contíguas,
  em vez de um retângulo por fatia (padding e cada linha de texto). Um bloco
  partido entre páginas fecha e reabre os cantos na quebra; gradientes passam a
  cobrir o bloco inteiro em vez de se repetir por linha.
- `AlignRight()`/`AlignCenter()` do contêiner agora alinham o número de página,
  como em `page.Footer().AlignRight().PageNumber("Página {0} de {1}")`. O
  `PageNumberElement` reportava no `Measure` a largura disponível em vez da do
  texto, então o número ficava sempre à esquerda. O alinhamento do próprio
  texto (`.PageNumber(...).AlignRight()`) não muda.
- JPEG em escala de cinza é embutido como `DeviceGray` e JPEG CMYK como
  `DeviceCMYK` (com `Decode` invertido nos arquivos com marcador Adobe APP14);
  antes tudo saía como `DeviceRGB`, com as cores corrompidas.
- O decoder de PNG valida dimensões, limita o inflate e confere os blocos
  `IDAT`: entrada malformada gera `InvalidDataException`, e não falta de memória
  ou overflow.
- Leitura de fontes e subsetting com checagem de limites: fonte truncada ou
  corrompida gera `InvalidDataException`; fonte sem a tabela `loca` é recusada
  com `NotSupportedException`.
- A fonte subconjunto recebe o prefixo de seis letras (`XXXXXX+`) exigido pela
  ISO 32000, e o `FontDescriptor` grava o `ItalicAngle` real da tabela `post`.

### Alterado
- Streams de conteúdo de página agora são comprimidos com `FlateDecode`; o CMap
  `ToUnicode` das fontes embutidas também.
- Wrappers decorativos (`Background`, `Border` e variantes com gradiente)
  permitem paginação do conteúdo interno; a decoração é desenhada uma vez por
  página sobre o trecho contíguo (`FlowItem.Decorations`).
- Os renderers nativos decodificam cada imagem uma única vez por documento,
  mesmo quando ela se repete em várias páginas.
- `PdfGen.Sample` agora gera uma proposta comercial premium de uma página, usa
  `agile.png` como imagem real e remove a geração manual de PNG em runtime.
- Testes de fonte embutida agora procuram fontes TrueType comuns em Windows,
  macOS e Linux, reduzindo a dependência fixa de `C:\Windows\Fonts\arial.ttf`.
- README atualizado para explicar o uso em WinForms/Blazor/hosts .NET, a
  diferença entre `GeneratePdf()` e `GeneratePdfNative()` e as limitações reais
  dos renderers nativos.

### Notas
- Primeira versão publicada no nuget.org. As versões 1.0.0 e 1.1.0 registradas
  abaixo nunca chegaram ao feed; a 1.2.0 inclui todo o conteúdo de ambas. É
  minor, e não patch, porque este ciclo acrescenta API pública e fontes
  embutidas nos renderers nativos.
- Uma instância de `PdfDocument` não é thread-safe: não renderize o mesmo
  documento em paralelo. Renders sequenciais da mesma instância são suportados.
- Documentação: em apps MAUI com `ImplicitUsings` (padrão do template), dez
  tipos públicos do PdfGen têm o mesmo nome de tipos dos usings globais do MAUI
  e geram CS0104 (referência ambígua) quando usados: `IContainer`,
  `EmbeddedFont`, `Colors`, `GradientBrush`, `GradientStop`,
  `HorizontalAlignment`, `VerticalAlignment`, `FontWeight`, `ImageFormat` e
  `Element`. O README traz os aliases recomendados (seção "Uso em apps MAUI").

## [1.1.0] - 2026-07-02

### Adicionado
- Fontes TrueType/OTF embutidas (`EmbeddedFont.FromFile` / `EmbeddedFont.Load`)
  com Unicode completo. O texto é gravado como fonte Type0/CIDFontType2
  (`Identity-H`) com CMap `ToUnicode`.
- Subsetting automático de fonte: apenas os glifos usados são embutidos,
  incluindo componentes de glifos compostos.
- Gradientes linear e radial (`GradientBrush.Linear` / `GradientBrush.Radial`),
  aplicáveis em `.Background(brush)` e `.Border(thickness, brush)`.
- Imagens PNG no escritor gerenciado: decodificação própria e embutimento via
  `FlateDecode`, com transparência mapeada para `SMask`.
- Exportação para SVG (`PdfDocument.GenerateSvg`): o mesmo documento e motor de
  layout, apenas trocando o backend de renderização.

### Corrigido
- Medição de texto acentuado nas fontes base-14: a faixa WinAnsi 0xA0-0xFF usa
  a largura AFM correspondente da letra base.

### Notas
- Mudanças retrocompatíveis: overloads novos e parâmetro opcional no fim do
  construtor de `TextStyle`; `IRenderContext` ganhou `FillGradient` e
  `StrokeGradient` como default interface methods.
- Limitações conhecidas: sem exportação raster (PNG/JPG/WEBP como saída), sem
  imagem SVG de entrada, fontes CFF (`.otf` com assinatura `OTTO`), PNG de 16
  bits e PNG entrelaçado Adam7 não são suportados.

## [1.0.0] - 2026

### Adicionado
- Primeira versão: motor de layout independente de plataforma com
  `Measure`/`Arrange`/`Render`, quebra de página automática, header/footer
  repetidos e cabeçalho de tabela repetido.
- API fluente inspirada no QuestPDF: documento, página, header/footer/content,
  texto, imagem, row, column, stack, table/cell, border, background, padding,
  alinhamento e número de página.
- Backends de renderização: escritor PDF 100% gerenciado, Android
  `PdfDocument` e iOS/Mac `CGContextPDF`. Fontes base-14. Sem SkiaSharp,
  WebView ou HTML.

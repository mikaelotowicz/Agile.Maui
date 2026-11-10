# DeviceTests

Device tests da Agile.Maui: complemento dos testes de host (TFMs neutros `net10.0`/`net11.0`) para o que
**só roda em plataforma** — handlers nativos, export de bitmap, PDFium, Glide, RecyclerView.

É um **app MAUI** (`net10.0-android`/`net11.0-android` e `net10.0-windows10.0.19041.0`/`net11.0-windows10.0.26100.0`)
com harness próprio:
sem pacote de runner (frágeis em net10), só `xunit.assert` + atributo `[DeviceFact]`.
A descoberta é por reflection e a execução é **sequencial na UI thread**, com timeout
individual por teste. O app roda a suíte inteira no startup, mostra os resultados na tela e
emite linhas parseáveis:

```
[DEVICETEST] PASS|FAIL NomeDoTeste | mensagem
[DEVICETEST-SUMMARY] total=N pass=N fail=N
```

No Windows, as mesmas linhas também vão para `%TEMP%\agile-devicetests-result.txt`.

## Pré-requisitos

- Android: SDK em `C:\Program Files (x86)\Android\android-sdk` e um AVD
  (default: `pixel_7_-_api_36_0`). O script inicia o emulador se não estiver rodando
  (boot pode levar minutos).
- Windows: nada além do .NET SDK — o app roda **unpackaged** (`WindowsPackageType=None`).
- **iOS/MacCatalyst estão fora do escopo**: exigem um Mac para build/execução.

## Como rodar

```powershell
cd DeviceTests
.\run-android.ps1    # builda, instala no emulador, inicia a activity e faz poll do logcat
.\run-windows.ps1    # builda, roda o exe e espera o arquivo de resultado
.\run-android.ps1 -Dotnet net11    # o mesmo contra o MAUI 11 (default: net10)
```

Ambos imprimem o resultado e saem com exit code 0 (verde) ou 1 (falha/timeout).
Parâmetros úteis: `-Dotnet net10|net11`, `-Configuration`, `-Avd`/`-DeviceSerial` (Android),
`-RunTimeoutSeconds`. Ao alternar `-Dotnet` no Android, desinstale antes o app
(`adb uninstall com.agile.maui.devicetests`): a implantação rápida de um TFM não sobrescreve a do outro (XA0127).

Os ícones e o splash são SVG **sem `<text>`**: o Resizetizer do MAUI 11.0.0 lança
`MissingMethodException` (`SKTextBlobBuilder.AddPositionedRun`) ao rasterizar texto em SVG.
No Windows, `AGILE_DEVICETESTS_KEEP_OPEN=1` mantém a janela aberta após a suíte.

## Como adicionar um teste

1. Crie (ou edite) uma classe pública em `Tests/` com um método:

   ```csharp
   [DeviceFact(Platforms.Android, TimeoutSeconds = 90)]   // ou Platforms.All / Platforms.Windows
   public static async Task Nome_Claro_Em_Portugues(TestHost host)
   {
       var view = new MeuControle();
       await host.MountAsync(view);            // página real: handler conectado + layout com tamanho
       await host.WaitForAsync(() => ..., 5_000, "descrição do que espera");
       Assert.True(...);
       await host.UnmountAsync();              // exercita o DisconnectHandler
   }
   ```

2. Código específico de plataforma: `#if ANDROID` / `#if WINDOWS` (o arquivo inteiro ou só o trecho).
3. Se precisar de internals de uma lib, adicione `<InternalsVisibleTo Include="Agile.Maui.DeviceTests" />`
   no `.csproj` dela (já feito no SignaturePad).
4. Dê timeout individual generoso — um teste travado não pode derrubar a suíte.

## Cobertura atual

| Teste | Plataformas | Regressão/sinergia coberta |
|---|---|---|
| `SignaturePad_Exporta_Tinta_Nos_Quatro_Quadrantes_Com_Escala_2x` | Android + Windows | `canvas.Scale` no export (conteúdo colapsado no quadrante superior-esquerdo); dimensões = bounds×2 |
| `VirtualizedCollectionView_Monta_Rola_TrocaTemplate_Repopula_E_Desmonta` | Android | 500 itens + virtualização, `ScrollTo` até o fim, troca de `ItemTemplate` em runtime, esvaziar/repopular e teardown sem `NotSupportedException` |
| `VirtualizedCollectionView_Windows_Monta_E_Desmonta_Sem_Excecao` | Windows | smoke do caminho Windows (CollectionView como Content) |
| `PdfGen_Gerenciado_Carrega_No_PdfViewer_Com_2_Paginas` | Android | `GeneratePdf()` → PdfViewer: `DocumentLoaded`, `PageCount` e rasterização da 1ª página |
| `PdfGen_Gerenciado_Com_Fonte_Embutida_Carrega_No_PdfViewer` | Android | `EmbeddedFont.Load` de um TTF real (MauiAsset) no device |
| `PdfGen_Nativo_Typeface_Carrega_No_PdfViewer` | Android | `GeneratePdfNative()` (caminho Typeface do Android) |
| `ImageView_Carrega_Local_E_Abre_Fullscreen_Com_Imagem_Pequena` | Android | `ImageLoaded` com arquivo local + fullscreen com imagem menor que a tela (regressão do `Math.Clamp` min>max no `InitMatrix`) |
| `ChipGroup_Renderiza_Chips_E_Tap_Programatico_Seleciona` | Android + Windows | chips com tamanho real após layout + seleção via Command do `TapGestureRecognizer` |
| `ChipGroup_Horizontal_Nao_Corta_A_Altura_Dos_Chips` | Android + Windows | faixa horizontal com chips da mesma altura do Wrap (no MAUI 11 Android o FlexLayout descontava a margem inferior e cortava o texto) |
| `ChipGroup_Vertical_Nao_Encolhe_A_Altura_Dos_Chips` | Android + Windows | coluna com chips da mesma altura do Wrap (o Shrink padrão cortava a base do texto) |

Limitação conhecida: no teste de fullscreen do ImageView, pinch/double-tap reais **não** são
simulados (exigiria injetar `MotionEvent`s com timing realista no `GestureDetector`); o caminho
coberto é abrir o dialog + `InitMatrix` com imagem pequena, que era onde a regressão estourava.

using AppMobile;

namespace sample;

/// <summary>
/// Mapeamento semântico dos ícones do PdfViewer para os glyphs da fonte
/// Material Design Icons (alias "MaterialDesignIcons", registrada em <c>MauiProgram</c>).
/// Centralizar aqui mantém o XAML legível e garante consistência visual entre as barras.
/// </summary>
public static class Icons
{
    /// <summary>Família de fonte registrada em <c>ConfigureFonts</c> (materialdesignicons-webfont.ttf).</summary>
    public const string FontFamily = "MaterialDesignIcons";

    // ── Top bar ──────────────────────────────────────────────────────────────
    public const string Hamburger  = MaterialDesingIconsFonts.Menu;                 // abrir flyout
    public const string Back       = MaterialDesingIconsFonts.ArrowLeft;            // voltar
    public const string Print      = MaterialDesingIconsFonts.PrinterOutline;       // imprimir
    public const string Share      = MaterialDesingIconsFonts.ShareVariantOutline;  // compartilhar
    public const string Menu       = MaterialDesingIconsFonts.DotsVertical;         // menu de opções (overflow)
    public const string Open       = MaterialDesingIconsFonts.FolderOpenOutline;    // abrir arquivo
    public const string Thumbnails = MaterialDesingIconsFonts.ViewGridOutline;      // grade de miniaturas
    public const string Log        = MaterialDesingIconsFonts.ScriptTextOutline;    // painel de log
    public const string Settings   = MaterialDesingIconsFonts.CogOutline;           // configurações

    // ── Zoom ─────────────────────────────────────────────────────────────────
    public const string ZoomIn     = MaterialDesingIconsFonts.MagnifyPlusOutline;  // ampliar
    public const string ZoomOut    = MaterialDesingIconsFonts.MagnifyMinusOutline; // reduzir
    public const string Fit        = MaterialDesingIconsFonts.FitToPageOutline;    // ajustar à página

    // ── Navegação ──────────────────────────────────────────────────────────────
    public const string Prev       = MaterialDesingIconsFonts.ChevronLeft;   // página anterior
    public const string Next       = MaterialDesingIconsFonts.ChevronRight;  // próxima página

    // ── Visualização ─────────────────────────────────────────────────────────
    public const string Fullscreen = MaterialDesingIconsFonts.Fullscreen;    // tela cheia

    // ── Itens do menu (flyout do Shell) ────────────────────────────────────────
    public const string MenuHome       = MaterialDesingIconsFonts.HomeOutline;            // início
    public const string MenuPdf        = MaterialDesingIconsFonts.FilePdfBox;             // PDF Viewer
    public const string MenuList       = MaterialDesingIconsFonts.ViewListOutline;        // lista virtualizada
    public const string MenuCollection = MaterialDesingIconsFonts.ViewDashboardOutline;   // MAUI CollectionView
}

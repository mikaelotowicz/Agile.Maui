namespace sample;

/// <summary>
/// Demonstra o componente pronto <c>PdfReaderView</c>: o leitor completo (toolbar, busca, barra
/// inferior) só apontando o <c>Source</c> no XAML — inclusive um PDF empacotado, que o controle
/// resolve sozinho (copia o asset para o cache).
/// </summary>
public partial class ReaderDemoPage : ContentPage
{
    public ReaderDemoPage() => InitializeComponent();
}

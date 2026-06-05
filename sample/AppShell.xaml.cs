namespace sample
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            // As páginas são itens do flyout (ShellContent no XAML), navegáveis pelas suas
            // próprias rotas (MainPage, PdfViewerPage, VirtualizedListPage, CollectionViewPage).
        }
    }
}

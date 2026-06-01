namespace sample
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute("pdfviewer", typeof(PdfViewerPage));
            // Demais demos acessíveis por rota (o menu/flyout do Shell crasha no Android .NET 10).
            Routing.RegisterRoute("virtualized", typeof(VirtualizedListPage));
            Routing.RegisterRoute("collectionview", typeof(CollectionViewPage));
        }
    }
}

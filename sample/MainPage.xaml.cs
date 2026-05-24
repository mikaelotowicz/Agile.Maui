namespace sample;

public partial class MainPage : ContentPage
{
    private readonly List<string> _log = [];

    public MainPage()
    {
        InitializeComponent();

        UrlGallery.Images = [
            "https://picsum.photos/seed/gallery1/800/500",
            "https://picsum.photos/seed/gallery2/800/600",
            "https://picsum.photos/seed/gallery3/600/800",
            "https://picsum.photos/seed/gallery4/800/500",
            "https://picsum.photos/seed/gallery5/900/600",
        ];

        LocalGallery.Images = [
            "dotnet_bot",
            "dotnet_bot",
            "dotnet_bot",
        ];
    }

    private void OnImageLoaded(object? sender, EventArgs e)
    {
        var source = (sender as Controls.ImageView)?.Source ?? "?";
        AppendLog($"[OK]   {Truncate(source)}");
    }

    private void OnImageFailed(object? sender, EventArgs e)
    {
        var source = (sender as Controls.ImageView)?.Source ?? "?";
        AppendLog($"[ERRO] {Truncate(source)}");
    }

    private void OnGallerySelectionChanged(object? sender, Controls.GalleryIndexChangedEventArgs e)
    {
        AppendLog($"[NAV]  galeria → índice {e.Index}");
    }

    private void OnGalleryImageLoaded(object? sender, EventArgs e)
    {
        AppendLog("[OK]   galeria carregou imagem");
    }

    private void OnGalleryImageFailed(object? sender, EventArgs e)
    {
        AppendLog("[ERRO] galeria falhou ao carregar imagem");
    }

    private void AppendLog(string entry)
    {
        _log.Insert(0, entry);
        if (_log.Count > 8) _log.RemoveAt(_log.Count - 1);
        StatusLabel.Text = string.Join("\n", _log);
    }

    private static string Truncate(string s) =>
        s.Length > 50 ? s[..47] + "..." : s;
}

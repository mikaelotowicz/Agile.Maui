namespace sample;

public partial class MainPage : ContentPage
{
    private readonly List<string> _log = [];

    public MainPage()
    {
        InitializeComponent();
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

    private void AppendLog(string entry)
    {
        _log.Insert(0, entry);
        if (_log.Count > 8) _log.RemoveAt(_log.Count - 1);
        StatusLabel.Text = string.Join("\n", _log);
    }

    private static string Truncate(string s) =>
        s.Length > 50 ? s[..47] + "..." : s;
}

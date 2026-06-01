namespace sample;

public partial class MainPage : ContentPage
{
    public MainPage() => InitializeComponent();

    private async void OnOpenPdfClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("pdfviewer");
}

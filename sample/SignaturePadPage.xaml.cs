using Agile.Maui;

namespace sample;

public partial class SignaturePadPage : ContentPage
{
    public SignaturePadPage()
    {
        InitializeComponent();
    }

    private void OnStrokeCompleted(object? sender, StrokeCompletedEventArgs e)
    {
        UpdateStats();
    }

    private void OnClear(object? sender, EventArgs e)
    {
        Pad.Clear();
        Preview.Source = null;
        StatsLabel.Text = "No strokes yet.";
    }

    private void OnUndo(object? sender, EventArgs e)
    {
        Pad.Undo();
        UpdateStats();
    }

    private void OnRedo(object? sender, EventArgs e)
    {
        Pad.Redo();
        UpdateStats();
    }

    private async void OnExport(object? sender, EventArgs e)
    {
        if (Pad.IsEmpty)
        {
            await DisplayAlertAsync("SignaturePad", "Sign before exporting.", "OK");
            return;
        }

        var options = new SignatureExportOptions
        {
            CropToContent = true,
            Padding = 20,
            Scale = 2.0,
            BackgroundColor = null, // Transparent PNG
        };

        try
        {
            using var stream = await Pad.GetImageStreamAsync(SignatureImageFormat.Png, options);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var bytes = memory.ToArray();

            Preview.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
            UpdateStats(bytes.Length);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Export failed", ex.ToString(), "OK");
        }
    }

    private void UpdateStats(int? exportedBytes = null)
    {
        var data = Pad.GetSignatureData();
        var pressure = data.HasRealPressure ? "real physical pressure" : "velocity-derived pressure";
        var sizeInfo = exportedBytes is { } b ? $" - PNG {b / 1024.0:0.0} KB" : string.Empty;

        StatsLabel.Text =
            $"Strokes: {data.Strokes.Count} - points: {data.TotalPoints} - " +
            $"duration: {data.TotalDurationMs / 1000.0:0.00}s - {pressure}{sizeInfo}";
    }
}

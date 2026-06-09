namespace Agile.Maui;

/// <summary>Options for <see cref="SignaturePad.GetImageStreamAsync"/>.</summary>
public sealed class SignatureExportOptions
{
    /// <summary>
    /// Crops the image to the rectangle containing the strokes plus <see cref="Padding"/>.
    /// When false, exports the entire pad area. Default is true.
    /// </summary>
    public bool CropToContent { get; set; } = true;

    /// <summary>Padding in DIP around the cropped strokes. Default is 16.</summary>
    public double Padding { get; set; } = 16;

    /// <summary>Export resolution multiplier, for example 2.0 for @2x. Default is 2.0.</summary>
    public double Scale { get; set; } = 2.0;

    /// <summary>Background color. Null means transparent for PNG; JPEG falls back to white.</summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>Optional export-only stroke color override, useful for black-only signatures.</summary>
    public Color? StrokeColorOverride { get; set; }

    /// <summary>JPEG quality from 0 to 1. Ignored for PNG. Default is 0.9.</summary>
    public float JpegQuality { get; set; } = 0.9f;
}

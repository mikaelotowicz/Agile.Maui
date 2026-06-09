namespace Agile.Maui;

/// <summary>Event data raised when a stroke completes after finger/stylus release.</summary>
public sealed class StrokeCompletedEventArgs : EventArgs
{
    public StrokeCompletedEventArgs(SignatureStroke stroke, bool isEmpty)
    {
        Stroke = stroke;
        IsEmpty = isEmpty;
    }

    /// <summary>The newly completed stroke.</summary>
    public SignatureStroke Stroke { get; }

    /// <summary>Pad state after the stroke. This is normally false because a stroke was just added.</summary>
    public bool IsEmpty { get; }
}

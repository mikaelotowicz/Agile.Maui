// Controls/GalleryView.cs
namespace Controls;

public class GalleryView : View
{
    public static readonly BindableProperty ImagesProperty =
        BindableProperty.Create(nameof(Images), typeof(IList<string>), typeof(GalleryView), null);

    public static readonly BindableProperty IsUrlProperty =
        BindableProperty.Create(nameof(IsUrl), typeof(bool), typeof(GalleryView), false);

    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(GalleryView), null);

    public static readonly BindableProperty SelectedIndexProperty =
        BindableProperty.Create(nameof(SelectedIndex), typeof(int), typeof(GalleryView), 0,
            validateValue: (_, v) => (int)v >= 0);

    public static readonly BindableProperty AspectModeProperty =
        BindableProperty.Create(nameof(AspectMode), typeof(ZoomImageAspect), typeof(GalleryView),
            ZoomImageAspect.CenterCrop);

    public static readonly BindableProperty MaxZoomProperty =
        BindableProperty.Create(nameof(MaxZoom), typeof(float), typeof(GalleryView), 5f,
            validateValue: (_, v) => (float)v >= 1f);

    public IList<string>? Images { get => (IList<string>?)GetValue(ImagesProperty); set => SetValue(ImagesProperty, value); }
    public bool IsUrl { get => (bool)GetValue(IsUrlProperty); set => SetValue(IsUrlProperty, value); }
    public string? Placeholder { get => (string?)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public ZoomImageAspect AspectMode { get => (ZoomImageAspect)GetValue(AspectModeProperty); set => SetValue(AspectModeProperty, value); }
    public float MaxZoom { get => (float)GetValue(MaxZoomProperty); set => SetValue(MaxZoomProperty, value); }

    public event EventHandler<GalleryIndexChangedEventArgs>? SelectionChanged;
    public event EventHandler? ImageLoaded;
    public event EventHandler? ImageFailed;

    internal void RaiseSelectionChanged(int index) =>
        SelectionChanged?.Invoke(this, new GalleryIndexChangedEventArgs(index));
    internal void RaiseImageLoaded() => ImageLoaded?.Invoke(this, EventArgs.Empty);
    internal void RaiseImageFailed() => ImageFailed?.Invoke(this, EventArgs.Empty);
}

public sealed class GalleryIndexChangedEventArgs : EventArgs
{
    public int Index { get; }
    public GalleryIndexChangedEventArgs(int index) => Index = index;
}

// Platforms/Windows/GalleryView/GalleryViewHandler.cs
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using WinStretch = Microsoft.UI.Xaml.Media.Stretch;

namespace Controls.Platforms.Windows;

using NativeImage = Microsoft.UI.Xaml.Controls.Image;

public sealed class GalleryViewHandler : ViewHandler<GalleryView, FlipView>
{
    public static readonly PropertyMapper<GalleryView, GalleryViewHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(GalleryView.Images)]        = (h, _) => h.LoadImages(),
            [nameof(GalleryView.SelectedIndex)] = (h, _) => h.SyncPage(),
            [nameof(GalleryView.IsUrl)]         = (h, _) => h.LoadImages(),
            [nameof(GalleryView.Placeholder)]   = (h, _) => h.LoadImages(),
            [nameof(GalleryView.AspectMode)]    = (h, _) => h.LoadImages(),
            [nameof(GalleryView.MaxZoom)]       = (h, _) => { },
        };

    private bool _syncingPage;

    public GalleryViewHandler() : base(Mapper) { }

    protected override FlipView CreatePlatformView() => new();

    protected override void ConnectHandler(FlipView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.SelectionChanged += OnSelectionChanged;
        LoadImages();
    }

    protected override void DisconnectHandler(FlipView platformView)
    {
        platformView.SelectionChanged -= OnSelectionChanged;
        platformView.Items.Clear();
        base.DisconnectHandler(platformView);
    }

    private void LoadImages()
    {
        if (PlatformView is null) return;

        PlatformView.SelectionChanged -= OnSelectionChanged;
        PlatformView.Items.Clear();

        var images = VirtualView.Images;
        if (images is null || images.Count == 0)
        {
            PlatformView.SelectionChanged += OnSelectionChanged;
            return;
        }

        var stretch = VirtualView.AspectMode == ZoomImageAspect.CenterCrop
            ? WinStretch.UniformToFill
            : WinStretch.Uniform;

        foreach (var source in images)
        {
            var img = new NativeImage { Stretch = stretch };
            img.ImageOpened += (_, _) => VirtualView?.RaiseImageLoaded();
            img.ImageFailed += (_, _) => { ApplyPlaceholder(img); VirtualView?.RaiseImageFailed(); };

            if (VirtualView.IsUrl)
            {
                if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
                    img.Source = new BitmapImage(uri);
                else
                    ApplyPlaceholder(img);
            }
            else
            {
                var filename = System.IO.Path.HasExtension(source) ? source : source + ".png";
                img.Source = new BitmapImage(new Uri($"ms-appx:///{filename}"));
            }

            PlatformView.Items.Add(img);
        }

        _syncingPage = true;
        PlatformView.SelectedIndex = Math.Clamp(VirtualView.SelectedIndex, 0, images.Count - 1);
        _syncingPage = false;

        PlatformView.SelectionChanged += OnSelectionChanged;
    }

    private void SyncPage()
    {
        if (PlatformView is null || _syncingPage) return;
        var images = VirtualView.Images;
        if (images is null || images.Count == 0) return;
        _syncingPage = true;
        PlatformView.SelectedIndex = Math.Clamp(VirtualView.SelectedIndex, 0, images.Count - 1);
        _syncingPage = false;
    }

    private void OnSelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (_syncingPage || VirtualView is null) return;
        var idx = PlatformView.SelectedIndex;
        if (idx < 0) return;
        _syncingPage = true;
        VirtualView.SelectedIndex = idx;
        VirtualView.RaiseSelectionChanged(idx);
        _syncingPage = false;
    }

    private void ApplyPlaceholder(NativeImage img)
    {
        if (string.IsNullOrWhiteSpace(VirtualView?.Placeholder)) return;
        var filename = System.IO.Path.HasExtension(VirtualView.Placeholder)
            ? VirtualView.Placeholder
            : VirtualView.Placeholder + ".png";
        img.Source = new BitmapImage(new Uri($"ms-appx:///{filename}"));
    }
}

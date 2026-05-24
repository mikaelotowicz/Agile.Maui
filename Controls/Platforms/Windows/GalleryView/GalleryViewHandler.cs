// Platforms/Windows/GalleryView/GalleryViewHandler.cs
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using WinStretch = Microsoft.UI.Xaml.Media.Stretch;

namespace Controls.Platforms.Windows;

using NativeImage = Microsoft.UI.Xaml.Controls.Image;

public sealed class GalleryViewHandler : ViewHandler<GalleryView, GalleryWinContainer>
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
            [nameof(GalleryView.ShowIndicator)] = (h, _) => h.UpdateDots(),
        };

    private bool _syncingPage;
    private readonly List<Action> _imageHandlerCleanup = [];

    public GalleryViewHandler() : base(Mapper) { }

    protected override GalleryWinContainer CreatePlatformView() => new();

    protected override void ConnectHandler(GalleryWinContainer platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Pager.SelectionChanged += OnSelectionChanged;
        LoadImages();
    }

    protected override void DisconnectHandler(GalleryWinContainer platformView)
    {
        platformView.Pager.SelectionChanged -= OnSelectionChanged;
        ClearImages();
        base.DisconnectHandler(platformView);
    }

    private void ClearImages()
    {
        foreach (var cleanup in _imageHandlerCleanup) cleanup();
        _imageHandlerCleanup.Clear();
        PlatformView?.Pager.Items.Clear();
    }

    private void LoadImages()
    {
        if (PlatformView is null) return;

        PlatformView.Pager.SelectionChanged -= OnSelectionChanged;
        ClearImages();

        var images = VirtualView.Images;
        if (images is null || images.Count == 0)
        {
            PlatformView.Pager.SelectionChanged += OnSelectionChanged;
            UpdateDots();
            return;
        }

        var stretch = VirtualView.AspectMode == ZoomImageAspect.CenterCrop
            ? WinStretch.UniformToFill
            : WinStretch.Uniform;

        foreach (var source in images)
        {
            var img = new NativeImage { Stretch = stretch };
            RoutedEventHandler openedHandler = (_, _) => VirtualView?.RaiseImageLoaded();
            ExceptionRoutedEventHandler failedHandler = (_, _) => { ApplyPlaceholder(img); VirtualView?.RaiseImageFailed(); };
            img.ImageOpened += openedHandler;
            img.ImageFailed += failedHandler;
            _imageHandlerCleanup.Add(() => { img.ImageOpened -= openedHandler; img.ImageFailed -= failedHandler; });

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

            PlatformView.Pager.Items.Add(img);
        }

        _syncingPage = true;
        PlatformView.Pager.SelectedIndex = Math.Clamp(VirtualView.SelectedIndex, 0, images.Count - 1);
        _syncingPage = false;

        PlatformView.Pager.SelectionChanged += OnSelectionChanged;
        UpdateDots();
    }

    private void SyncPage()
    {
        if (PlatformView is null || _syncingPage) return;
        var images = VirtualView.Images;
        if (images is null || images.Count == 0) return;
        _syncingPage = true;
        PlatformView.Pager.SelectedIndex = Math.Clamp(VirtualView.SelectedIndex, 0, images.Count - 1);
        _syncingPage = false;
    }

    private void OnSelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (_syncingPage || VirtualView is null) return;
        var idx = PlatformView?.Pager.SelectedIndex ?? -1;
        if (idx < 0) return;
        _syncingPage = true;
        VirtualView.SelectedIndex = idx;
        VirtualView.RaiseSelectionChanged(idx);
        _syncingPage = false;
        UpdateDots();
    }

    private void UpdateDots()
    {
        if (PlatformView is null) return;
        var show  = VirtualView.ShowIndicator;
        var count = VirtualView.Images?.Count ?? 0;
        var idx   = Math.Clamp(VirtualView.SelectedIndex, 0, Math.Max(0, count - 1));
        var panel = PlatformView.Dots;

        if (!show || count <= 1) { panel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed; return; }

        panel.Children.Clear();
        for (int i = 0; i < count; i++)
        {
            var alpha = (byte)(i == idx ? 255 : 128);
            panel.Children.Add(new Ellipse
            {
                Width  = 7,
                Height = 7,
                Margin = new Microsoft.UI.Xaml.Thickness(3, 0, 3, 0),
                Fill   = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    global::Windows.UI.Color.FromArgb(alpha, 255, 255, 255)),
            });
        }
        panel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
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

// ─────────────────────────────────────────────────────────────────────────────
// GalleryWinContainer — Grid que envolve FlipView + DotsPanel
// ─────────────────────────────────────────────────────────────────────────────

public sealed class GalleryWinContainer : Microsoft.UI.Xaml.Controls.Grid
{
    public   readonly FlipView   Pager;
    internal readonly StackPanel Dots;

    public GalleryWinContainer()
    {
        Pager = new FlipView();
        Dots  = new StackPanel
        {
            Orientation         = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
            VerticalAlignment   = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
            Margin              = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 8),
            Visibility          = Microsoft.UI.Xaml.Visibility.Collapsed,
            IsHitTestVisible    = false,
        };
        Children.Add(Pager);
        Children.Add(Dots);
    }
}

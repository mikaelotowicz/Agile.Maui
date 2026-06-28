// Platforms/Windows/GalleryView/GalleryViewHandler.cs
using System.Collections.Specialized;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using WinStretch = Microsoft.UI.Xaml.Media.Stretch;

namespace Agile.Maui.Platforms.Windows;

using NativeImage = Microsoft.UI.Xaml.Controls.Image;

public sealed class GalleryViewHandler : ViewHandler<GalleryView, GalleryWinContainer>
{
    public static readonly PropertyMapper<GalleryView, GalleryViewHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(GalleryView.Images)]        = (h, _) => h.LoadImages(),
            [nameof(GalleryView.SelectedIndex)] = (h, _) => h.SyncPage(),
            ["IsUrl"]                           = (h, _) => h.LoadImages(),
            [nameof(GalleryView.Placeholder)]   = (h, _) => h.LoadImages(),
            [nameof(GalleryView.AspectMode)]    = (h, _) => h.LoadImages(),
            [nameof(GalleryView.ThumbMaxPx)]    = (h, _) => h.LoadImages(),
            [nameof(GalleryView.MaxZoom)]                = (h, _) => { },
            [nameof(GalleryView.ShowIndicator)]          = (h, _) => h.UpdateDots(),
            [nameof(GalleryView.IndicatorColor)]         = (h, _) => h.UpdateDots(),
            [nameof(GalleryView.IndicatorInactiveColor)] = (h, _) => h.UpdateDots(),
        };

    private bool                      _syncingPage;
    private INotifyCollectionChanged? _imagesChangedSource;
    private readonly List<Action>     _imageHandlerCleanup = [];

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
        UnsubscribeImages();
        ClearImages(platformView);
        base.DisconnectHandler(platformView);
    }

    private void ClearImages(GalleryWinContainer? platformView = null)
    {
        foreach (var cleanup in _imageHandlerCleanup) cleanup();
        _imageHandlerCleanup.Clear();
        (platformView ?? PlatformView)?.Pager.Items.Clear();
    }

    private void LoadImages()
    {
        if (PlatformView is null || VirtualView is null) return;

        PlatformView.Pager.SelectionChanged -= OnSelectionChanged;
        UnsubscribeImages();
        ClearImages();

        var images = VirtualView.Images;
        if (images is null || images.Count == 0)
        {
            SubscribeImages(images);
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
            _imageHandlerCleanup.Add(() =>
            {
                img.ImageOpened -= openedHandler;
                img.ImageFailed -= failedHandler;
                img.Source = null;
            });

            if (ImageSourceResolver.IsRemote(source, VirtualView.LegacyIsUrl))
            {
                if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
                    img.Source = CreateBitmap(uri);
                else
                    ApplyPlaceholder(img);
            }
            else
            {
                img.Source = CreateLocalBitmap(source);
            }

            PlatformView.Pager.Items.Add(img);
        }

        _syncingPage = true;
        PlatformView.Pager.SelectedIndex = Math.Clamp(VirtualView.SelectedIndex, 0, images.Count - 1);
        _syncingPage = false;

        SubscribeImages(images);
        PlatformView.Pager.SelectionChanged += OnSelectionChanged;
        UpdateDots();
    }

    private void SubscribeImages(IList<string>? images)
    {
        if (images is INotifyCollectionChanged ncc)
        {
            _imagesChangedSource = ncc;
            ncc.CollectionChanged += OnImagesCollectionChanged;
        }
    }

    private void UnsubscribeImages()
    {
        if (_imagesChangedSource is not null)
        {
            _imagesChangedSource.CollectionChanged -= OnImagesCollectionChanged;
            _imagesChangedSource = null;
        }
    }

    private void OnImagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (PlatformView is null || VirtualView is null) return;
        LoadImages();
    }

    private void SyncPage()
    {
        if (PlatformView is null || VirtualView is null || _syncingPage) return;
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
        if (PlatformView is null || VirtualView is null) return;
        var show     = VirtualView.ShowIndicator;
        var count    = VirtualView.Images?.Count ?? 0;
        var idx      = Math.Clamp(VirtualView.SelectedIndex, 0, Math.Max(0, count - 1));
        var panel    = PlatformView.Dots;
        var active   = ToWinColor(VirtualView.IndicatorColor);
        var inactive = ToWinColor(VirtualView.IndicatorInactiveColor);

        if (!show || count <= 1)
        {
            panel.Children.Clear();
            panel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            return;
        }

        // Rebuild estrutural apenas quando o número de dots muda.
        if (panel.Children.Count != count)
        {
            panel.Children.Clear();
            for (int i = 0; i < count; i++)
            {
                panel.Children.Add(new Ellipse
                {
                    Width  = 7,
                    Height = 7,
                    Margin = new Microsoft.UI.Xaml.Thickness(3, 0, 3, 0),
                    Fill   = new Microsoft.UI.Xaml.Media.SolidColorBrush(inactive),
                });
            }
        }

        // Atualiza apenas as cores — sem recriar elementos por swipe.
        for (int i = 0; i < panel.Children.Count; i++)
        {
            if (panel.Children[i] is Ellipse e && e.Fill is Microsoft.UI.Xaml.Media.SolidColorBrush brush)
                brush.Color = i == idx ? active : inactive;
        }
        panel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
    }

    private static global::Windows.UI.Color ToWinColor(Microsoft.Maui.Graphics.Color c) =>
        global::Windows.UI.Color.FromArgb(
            (byte)(c.Alpha * 255),
            (byte)(c.Red   * 255),
            (byte)(c.Green * 255),
            (byte)(c.Blue  * 255));

    private void ApplyPlaceholder(NativeImage img)
    {
        if (string.IsNullOrWhiteSpace(VirtualView?.Placeholder)) return;
        img.Source = CreateLocalBitmap(VirtualView.Placeholder);
    }

    private BitmapImage CreateLocalBitmap(string source)
    {
        if (ImageSourceResolver.TryGetLocalFilePath(source, out var path))
            return CreateBitmap(new Uri(path, UriKind.Absolute));

        if (ImageSourceResolver.TryGetAbsoluteLocalUri(source, out var uri))
            return CreateBitmap(uri);

        var filename = ImageSourceResolver.MauiResourcePath(source);
        return CreateBitmap(new Uri($"ms-appx:///{filename}"));
    }

    private BitmapImage CreateBitmap(Uri uri)
    {
        var maxPx = Math.Max(64, VirtualView?.ThumbMaxPx ?? 720);
        return new BitmapImage
        {
            DecodePixelWidth = maxPx,
            DecodePixelHeight = maxPx,
            UriSource = uri
        };
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

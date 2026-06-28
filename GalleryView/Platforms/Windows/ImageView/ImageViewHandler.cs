// Platforms/Windows/ImageViewHandler.cs
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Media.Imaging;
using Agile.Maui;

namespace Agile.Maui.Platforms.Windows;

using NativeImage = Microsoft.UI.Xaml.Controls.Image;

public sealed class ImageViewHandler : ViewHandler<ImageView, NativeImage>
{
    public static readonly PropertyMapper<ImageView, ImageViewHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(ImageView.Source)]           = (h, _) => h.LoadImage(),
            ["IsUrl"]                            = (h, _) => h.LoadImage(),
            [nameof(ImageView.Placeholder)]      = (h, _) => h.LoadImage(),
            [nameof(ImageView.AspectMode)]       = (h, _) => h.ApplyStretch(),
            [nameof(ImageView.DecodeMaxPx)]      = (h, _) => h.LoadImage(),
            [nameof(ImageView.MaxZoom)]          = (h, _) => { },
            [nameof(ImageView.EnableFullscreen)] = (h, _) => { },
        };

    public ImageViewHandler() : base(Mapper) { }

    protected override NativeImage CreatePlatformView() => new();

    protected override void ConnectHandler(NativeImage platformView)
    {
        base.ConnectHandler(platformView);
        platformView.ImageOpened += OnImageOpened;
        platformView.ImageFailed += OnImageFailed;
        ApplyStretch();
        LoadImage();
    }

    protected override void DisconnectHandler(NativeImage platformView)
    {
        platformView.ImageOpened -= OnImageOpened;
        platformView.ImageFailed -= OnImageFailed;
        VirtualView?.SetIsLoading(false);
        platformView.Source = null;
        base.DisconnectHandler(platformView);
    }

    private void LoadImage()
    {
        if (PlatformView is null) return;

        if (string.IsNullOrWhiteSpace(VirtualView.Source))
        {
            ApplyPlaceholder();
            VirtualView.SetIsLoading(false);
            return;
        }

        VirtualView.SetIsLoading(true);

        if (ImageSourceResolver.IsRemote(VirtualView.Source, VirtualView.LegacyIsUrl))
        {
            if (Uri.TryCreate(VirtualView.Source, UriKind.Absolute, out var uri))
                PlatformView.Source = CreateBitmap(uri);
            else
            {
                ApplyPlaceholder();
                VirtualView?.RaiseImageFailed();
            }
        }
        else
        {
            // Imagens locais em apps MAUI Windows ficam em ms-appx:///
            // Se Source não tiver extensão, assume .png (padrão MAUI)
            PlatformView.Source = CreateLocalBitmap(VirtualView.Source);
        }
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
        var maxPx = Math.Max(64, VirtualView.DecodeMaxPx);
        return new BitmapImage
        {
            DecodePixelWidth = maxPx,
            DecodePixelHeight = maxPx,
            UriSource = uri
        };
    }

    private void ApplyStretch()
    {
        if (PlatformView is null) return;
        PlatformView.Stretch = VirtualView.AspectMode == ZoomImageAspect.CenterCrop
            ? Microsoft.UI.Xaml.Media.Stretch.UniformToFill
            : Microsoft.UI.Xaml.Media.Stretch.Uniform;
    }

    private void ApplyPlaceholder()
    {
        if (string.IsNullOrWhiteSpace(VirtualView.Placeholder)) return;
        PlatformView.Source = CreateLocalBitmap(VirtualView.Placeholder);
    }

    private void OnImageOpened(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => VirtualView?.RaiseImageLoaded();

    private void OnImageFailed(object sender, Microsoft.UI.Xaml.ExceptionRoutedEventArgs e)
    {
        ApplyPlaceholder();
        VirtualView?.RaiseImageFailed();
    }
}

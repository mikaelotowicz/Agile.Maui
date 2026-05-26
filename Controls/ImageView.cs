// Controls/ZoomImageView.cs
using System.Windows.Input;

namespace Agile.Maui;

/// <summary>
/// Cross-platform image view com suporte a zoom fullscreen.
/// Android: DialogFragment com Matrix zoom nativo + Glide.
/// iOS/MacCatalyst: UIViewController com UIScrollView nativo.
/// Windows: WinUI Image com carregamento via BitmapImage.
/// </summary>
public class ImageView : View
{
    public static readonly BindableProperty SourceProperty =
        BindableProperty.Create(
            nameof(Source), typeof(string), typeof(ImageView), null);

    public static readonly BindableProperty IsUrlProperty =
        BindableProperty.Create(
            nameof(IsUrl), typeof(bool), typeof(ImageView), false);

    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(
            nameof(Placeholder), typeof(string), typeof(ImageView), null);

    public static readonly BindableProperty MaxZoomProperty =
        BindableProperty.Create(
            nameof(MaxZoom), typeof(float), typeof(ImageView), 5f,
            validateValue: (_, v) => (float)v >= 1f);

    public static readonly BindableProperty EnableFullscreenProperty =
        BindableProperty.Create(
            nameof(EnableFullscreen), typeof(bool), typeof(ImageView), true);

    public static readonly BindableProperty FullscreenSourceProperty =
        BindableProperty.Create(
            nameof(FullscreenSource), typeof(string), typeof(ImageView), null);

    public static readonly BindableProperty AspectModeProperty =
        BindableProperty.Create(
            nameof(AspectMode), typeof(ZoomImageAspect), typeof(ImageView),
            ZoomImageAspect.CenterCrop);

    public static readonly BindableProperty ImageLoadedCommandProperty =
        BindableProperty.Create(nameof(ImageLoadedCommand), typeof(ICommand), typeof(ImageView), null);

    public static readonly BindableProperty ImageFailedCommandProperty =
        BindableProperty.Create(nameof(ImageFailedCommand), typeof(ICommand), typeof(ImageView), null);

    /// <summary>Nome do drawable local ou URL completa.</summary>
    public string? Source
    {
        get => (string?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>True quando Source é uma URL HTTP/HTTPS.</summary>
    public bool IsUrl
    {
        get => (bool)GetValue(IsUrlProperty);
        set => SetValue(IsUrlProperty, value);
    }

    /// <summary>Drawable usado como placeholder e fallback de erro.</summary>
    public string? Placeholder
    {
        get => (string?)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>Escala máxima no fullscreen. Mínimo: 1f.</summary>
    public float MaxZoom
    {
        get => (float)GetValue(MaxZoomProperty);
        set => SetValue(MaxZoomProperty, value);
    }

    /// <summary>Habilita abertura fullscreen ao tocar.</summary>
    public bool EnableFullscreen
    {
        get => (bool)GetValue(EnableFullscreenProperty);
        set => SetValue(EnableFullscreenProperty, value);
    }

    /// <summary>URL/recurso de alta qualidade para o fullscreen. Se null, usa Source.</summary>
    public string? FullscreenSource
    {
        get => (string?)GetValue(FullscreenSourceProperty);
        set => SetValue(FullscreenSourceProperty, value);
    }

    /// <summary>Modo de exibição no thumbnail.</summary>
    public ZoomImageAspect AspectMode
    {
        get => (ZoomImageAspect)GetValue(AspectModeProperty);
        set => SetValue(AspectModeProperty, value);
    }

    public ICommand? ImageLoadedCommand { get => (ICommand?)GetValue(ImageLoadedCommandProperty); set => SetValue(ImageLoadedCommandProperty, value); }
    public ICommand? ImageFailedCommand { get => (ICommand?)GetValue(ImageFailedCommandProperty); set => SetValue(ImageFailedCommandProperty, value); }

    /// <summary>Disparado quando a imagem carrega com sucesso.</summary>
    public event EventHandler? ImageLoaded;

    /// <summary>Disparado quando o carregamento falha ou Source nao e encontrado.</summary>
    public event EventHandler? ImageFailed;

    internal void RaiseImageLoaded()
    {
        ImageLoaded?.Invoke(this, EventArgs.Empty);
        if (ImageLoadedCommand?.CanExecute(null) == true)
            ImageLoadedCommand.Execute(null);
    }

    internal void RaiseImageFailed()
    {
        ImageFailed?.Invoke(this, EventArgs.Empty);
        if (ImageFailedCommand?.CanExecute(null) == true)
            ImageFailedCommand.Execute(null);
    }
}

/// <summary>Modo de exibição da imagem no thumbnail.</summary>
public enum ZoomImageAspect
{
    CenterCrop,
    AspectFit
}

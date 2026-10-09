#if ANDROID
using Xunit;

namespace Agile.Maui.DeviceTests;

public static class ImageViewDeviceTests
{
    /// <summary>
    /// Carrega uma imagem local pequena (gerada em runtime) e abre o fullscreen com imagem
    /// MENOR que a tela — regressão do Math.Clamp com min &gt; max no ZoomTouchHandler
    /// (ArgumentException ao calcular matrix/limites para imagens pequenas). O InitMatrix roda
    /// num Post() após o Glide carregar; um crash aqui derruba a UI thread e a suíte inteira.
    /// Pinch/double-tap reais não são simulados (ver README).
    /// </summary>
    [DeviceFact(Platforms.Android, TimeoutSeconds = 90)]
    public static async Task ImageView_Carrega_Local_E_Abre_Fullscreen_Com_Imagem_Pequena(TestHost host)
    {
        // PNG 64x48 gerado em runtime (azul sólido) num arquivo local.
        var path = Path.Combine(FileSystem.CacheDirectory, $"devicetest-img-{Guid.NewGuid():N}.png");
        using (var bmp = global::Android.Graphics.Bitmap.CreateBitmap(
                   64, 48, global::Android.Graphics.Bitmap.Config.Argb8888!))
        {
            bmp.EraseColor(unchecked((int)0xFF2F6FDB));
            await using var fs = File.Create(path);
            bmp.Compress(global::Android.Graphics.Bitmap.CompressFormat.Png!, 100, fs);
        }

        var imageView = new ImageView
        {
            Source = path,
            EnableFullscreen = true,
            AspectMode = ZoomImageAspect.AspectFit,
            WidthRequest = 200,
            HeightRequest = 150,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };

        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        imageView.ImageLoaded += (_, _) => loaded.TrySetResult();
        imageView.ImageFailed += (_, _) =>
            loaded.TrySetException(new InvalidOperationException("ImageFailed disparou para imagem local válida"));

        try
        {
            await host.MountAsync(imageView);
            await host.WaitForAsync(loaded.Task, 15_000, "ImageLoaded da imagem local");
            Assert.False(imageView.IsLoading, "IsLoading deveria voltar a false após ImageLoaded");

            // Abre o fullscreen pelo mesmo caminho do toque (Click no container nativo).
            var container = imageView.Handler?.PlatformView as Agile.Maui.Platforms.Android.ImageViewContainer
                ?? throw new InvalidOperationException("platform view não é ImageViewContainer");
            container.PerformClick();

            var activity = Platform.CurrentActivity as AndroidX.Fragment.App.FragmentActivity
                ?? throw new InvalidOperationException("CurrentActivity não é FragmentActivity");

            AndroidX.Fragment.App.Fragment? Fragment() =>
                activity.SupportFragmentManager.FindFragmentByTag(
                    Agile.Maui.Platforms.Android.FullscreenZoomDialogFragment.Tag);

            await host.WaitForAsync(() => Fragment() is not null, 10_000, "fragment de fullscreen aberto");

            // Janela para o Glide carregar e o InitMatrix (Post) rodar com a imagem pequena —
            // a regressão do Clamp estourava exatamente aqui.
            await host.PumpAsync(2_500);

            var fragment = Fragment();
            Assert.True(fragment is { IsAdded: true },
                "fragment de fullscreen deveria continuar aberto após o InitMatrix (sem crash)");

            // Fecha o fullscreen e confirma o teardown.
            ((AndroidX.Fragment.App.DialogFragment)fragment!).DismissAllowingStateLoss();
            await host.WaitForAsync(() => Fragment() is null, 10_000, "fragment de fullscreen fechado");

            await host.UnmountAsync();
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }
}
#endif

#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using ImageIO;
using UIKit;

namespace Agile.Maui.Platforms.iOS;

internal static class AppleImageCache
{
    private static readonly NSCache s_cache = new()
    {
        CountLimit = 180,
        // Custo em bytes: só por contagem, 180 imagens grandes (fullscreen até 4096px) somariam GBs.
        TotalCostLimit = 96 * 1024 * 1024,
    };

    public static UIImage? Get(string key) => s_cache.ObjectForKey((NSString)key) as UIImage;

    public static void Set(string key, UIImage image)
    {
        var scale = image.CurrentScale > 0 ? image.CurrentScale : 1;
        var cost  = (nuint)(image.Size.Width * scale * image.Size.Height * scale * 4);
        s_cache.SetCost(image, (NSString)key, cost);
    }

    public static string Key(string source, int maxPixelSize) => $"{maxPixelSize}:{source}";

    public static int ResolveMaxPixelSize(double width, double height, nfloat scale, int fallbackPx)
    {
        if (width > 0 && height > 0 && scale > 0)
            return Math.Max(64, (int)Math.Ceiling(Math.Max(width, height) * (double)scale));

        return Math.Max(64, fallbackPx);
    }

    public static UIImage? Decode(NSData data, int maxPixelSize, nfloat scale)
    {
        try
        {
            using var src = CGImageSource.FromData(data);
            if (src is not null)
            {
                using var cg = src.CreateThumbnail(0, new CGImageThumbnailOptions
                {
                    CreateThumbnailFromImageAlways = true,
                    CreateThumbnailWithTransform = true,
                    MaxPixelSize = Math.Max(64, maxPixelSize),
                });
                if (cg is not null)
                    return UIImage.FromImage(cg, scale > 0 ? scale : UIScreen.MainScreen.Scale, UIImageOrientation.Up);
            }
        }
        catch
        {
            // Fall back to UIKit decode below.
        }

        return UIImage.LoadFromData(data);
    }

    public static UIImage? LoadLocal(string? source, int maxPixelSize, nfloat scale)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        var key = Key("local:" + source.Trim(), Math.Max(64, maxPixelSize));
        if (Get(key) is { } cached)
            return cached;

        UIImage? image = null;
        if (ImageSourceResolver.TryGetLocalFilePath(source, out var path))
            image = DecodeFile(path, maxPixelSize, scale);

        image ??= UIImage.FromBundle(source);

        var resourceName = ImageSourceResolver.ResourceName(source);
        if (image is null && !string.IsNullOrWhiteSpace(resourceName))
            image = UIImage.FromBundle(resourceName);

        if (image is not null)
            Set(key, image);

        return image;
    }

    private static UIImage? DecodeFile(string path, int maxPixelSize, nfloat scale)
    {
        try
        {
            using var url = NSUrl.FromFilename(path);
            using var src = CGImageSource.FromUrl(url);
            if (src is not null)
            {
                using var cg = src.CreateThumbnail(0, new CGImageThumbnailOptions
                {
                    CreateThumbnailFromImageAlways = true,
                    CreateThumbnailWithTransform = true,
                    MaxPixelSize = Math.Max(64, maxPixelSize),
                });
                if (cg is not null)
                    return UIImage.FromImage(cg, scale > 0 ? scale : UIScreen.MainScreen.Scale, UIImageOrientation.Up);
            }
        }
        catch
        {
            // Fall back to UIKit file loading below.
        }

        return UIImage.FromFile(path);
    }
}
#endif

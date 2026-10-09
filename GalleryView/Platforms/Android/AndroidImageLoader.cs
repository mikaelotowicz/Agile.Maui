#if ANDROID
using System.Collections.Concurrent;
using Bumptech.Glide;
using Bumptech.Glide.Request;

namespace Agile.Maui.Platforms.Android;

internal static class AndroidImageLoader
{
    // Ids de resource são estáveis por processo — GetIdentifier (reflexivo e lento) só na 1ª vez.
    private static readonly ConcurrentDictionary<string, int> s_drawableIds = new();

    public static void LoadInto(
        global::Android.Widget.ImageView imageView,
        string source,
        RequestOptions options,
        Bumptech.Glide.Request.IRequestListener? listener,
        bool legacyIsUrl = false)
    {
        if (ImageSourceResolver.IsRemote(source, legacyIsUrl))
        {
            var request = Glide.With(imageView).Load(source).Apply(options);
            if (listener is not null) request = request.Listener(listener);
            request.Into(imageView);
            return;
        }

        var drawableId = ResolveDrawable(imageView.Context!, source);
        if (drawableId != 0)
        {
            var request = Glide.With(imageView).Load(drawableId).Apply(options);
            if (listener is not null) request = request.Listener(listener);
            request.Into(imageView);
            return;
        }

        if (ImageSourceResolver.TryGetLocalFilePath(source, out var path))
        {
            var request = Glide.With(imageView).Load(new Java.IO.File(path)).Apply(options);
            if (listener is not null) request = request.Listener(listener);
            request.Into(imageView);
            return;
        }

        if (ImageSourceResolver.TryGetAbsoluteLocalUri(source, out _))
        {
            var request = Glide.With(imageView).Load(global::Android.Net.Uri.Parse(source)).Apply(options);
            if (listener is not null) request = request.Listener(listener);
            request.Into(imageView);
            return;
        }

        var fallback = Glide.With(imageView).Load(source).Apply(options);
        if (listener is not null) fallback = fallback.Listener(listener);
        fallback.Into(imageView);
    }

    public static int ResolveDrawable(global::Android.Content.Context context, string? source)
    {
        var name = ImageSourceResolver.ResourceName(source);
        if (string.IsNullOrWhiteSpace(name))
            return 0;

        if (s_drawableIds.TryGetValue(name, out var cached))
            return cached;

        var resources = context.Resources;
        if (resources is null)
            return 0;

        var id = resources.GetIdentifier(name, "drawable", context.PackageName);
        if (id == 0)
            id = resources.GetIdentifier(name, "mipmap", context.PackageName);

        s_drawableIds[name] = id;
        return id;
    }
}
#endif

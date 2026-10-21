#if ANDROID
using System.Collections.Concurrent;
using Bumptech.Glide;
using Bumptech.Glide.Request;

namespace Agile.Maui.Platforms.Android;

internal static class AndroidImageLoader
{
    // Ids de resource são estáveis por processo — GetIdentifier (reflexivo e lento) só na 1ª vez.
    private static readonly ConcurrentDictionary<string, int> s_drawableIds = new();

    /// <param name="currentSource">A foto que a página mostra agora, quando a fonte dela foi trocada.</param>
    public static void LoadInto(
        global::Android.Widget.ImageView imageView,
        string source,
        RequestOptions options,
        Bumptech.Glide.Request.IRequestListener? listener,
        bool legacyIsUrl = false,
        string? currentSource = null)
    {
        var request = Request(imageView, source, legacyIsUrl).Apply(options);
        if (listener is not null) request = request.Listener(listener);

        // A foto atual, com as mesmas options, está na memória do Glide: como thumbnail ela volta na
        // hora e segura a página até a nova decodificar. Sem isso a página fica vazia nesse meio-tempo.
        if (currentSource is not null)
            request = request.Thumbnail(Request(imageView, currentSource, legacyIsUrl).Apply(options));

        request.Into(imageView);
    }

    private static RequestBuilder Request(
        global::Android.Widget.ImageView imageView, string source, bool legacyIsUrl)
    {
        var glide = Glide.With(imageView);

        if (ImageSourceResolver.IsRemote(source, legacyIsUrl))
            return glide.Load(source);

        var drawableId = ResolveDrawable(imageView.Context!, source);
        if (drawableId != 0)
            return glide.Load(drawableId);

        if (ImageSourceResolver.TryGetLocalFilePath(source, out var path))
            return glide.Load(new Java.IO.File(path));

        if (ImageSourceResolver.TryGetAbsoluteLocalUri(source, out _))
            return glide.Load(global::Android.Net.Uri.Parse(source));

        return glide.Load(source);
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

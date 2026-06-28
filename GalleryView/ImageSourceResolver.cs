namespace Agile.Maui;

internal static class ImageSourceResolver
{
    public static bool IsRemote(string? source, bool legacyIsUrl = false)
    {
        if (legacyIsUrl)
            return true;

        if (string.IsNullOrWhiteSpace(source))
            return false;

        return Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public static bool TryGetAbsoluteLocalUri(string? source, out Uri uri)
    {
        uri = null!;

        if (string.IsNullOrWhiteSpace(source))
            return false;

        return Uri.TryCreate(source.Trim(), UriKind.Absolute, out uri!)
            && uri.Scheme != Uri.UriSchemeHttp
            && uri.Scheme != Uri.UriSchemeHttps;
    }

    public static bool TryGetLocalFilePath(string? source, out string path)
    {
        path = string.Empty;

        if (string.IsNullOrWhiteSpace(source))
            return false;

        var value = source.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (!uri.IsFile)
                return false;

            path = uri.LocalPath;
            return true;
        }

        if (value.Contains("://", StringComparison.Ordinal))
            return false;

        if (!Path.IsPathRooted(value))
            return false;

        path = value;
        return true;
    }

    public static string? ResourceName(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        var value = source.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (!uri.IsFile)
                return null;

            value = uri.LocalPath;
        }

        value = value.Replace('\\', '/');
        var slash = value.LastIndexOf('/');
        if (slash >= 0)
            value = value[(slash + 1)..];

        var resourceName = Path.GetFileNameWithoutExtension(value);
        return string.IsNullOrWhiteSpace(resourceName) ? null : resourceName;
    }

    public static string MauiResourcePath(string source)
    {
        var value = source.Trim().Replace('\\', '/').TrimStart('/');
        return Path.HasExtension(value) ? value : value + ".png";
    }
}

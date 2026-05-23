// Platforms/Android/ContextExtensions.cs
using AndroidX.Fragment.App;

namespace Controls.Platforms.Android;

internal static class ContextExtensions
{
    public static FragmentActivity? GetActivity(
        this global::Android.Content.Context context)
    {
        while (context is global::Android.Content.ContextWrapper wrapper)
        {
            if (wrapper is FragmentActivity activity)
                return activity;
            context = wrapper.BaseContext!;
        }
        return null;
    }
}

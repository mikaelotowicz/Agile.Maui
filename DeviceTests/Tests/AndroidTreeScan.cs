#if ANDROID
namespace Agile.Maui.DeviceTests;

/// <summary>Varredura da árvore de views nativa do Android (para asserts de handlers).</summary>
public static class AndroidTreeScan
{
    public static IEnumerable<global::Android.Views.View> Descendants(global::Android.Views.View root)
    {
        yield return root;
        if (root is global::Android.Views.ViewGroup group)
        {
            for (var i = 0; i < group.ChildCount; i++)
            {
                var child = group.GetChildAt(i);
                if (child is null)
                    continue;
                foreach (var v in Descendants(child))
                    yield return v;
            }
        }
    }
}
#endif

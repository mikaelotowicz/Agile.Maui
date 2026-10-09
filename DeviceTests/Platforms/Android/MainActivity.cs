using Android.App;
using Android.Content.PM;

namespace Agile.Maui.DeviceTests;

// Name explícito: o run-android.ps1 inicia a activity por
// `am start -n com.agile.maui.devicetests/com.agile.maui.devicetests.MainActivity`.
[Activity(
    Name = "com.agile.maui.devicetests.MainActivity",
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
}

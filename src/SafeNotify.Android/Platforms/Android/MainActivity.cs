using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace SafeNotify.Android;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public sealed class MainActivity : MauiAppCompatActivity
{
    public static bool IsForeground { get; private set; }

    protected override void OnResume()
    {
        base.OnResume();
        IsForeground = true;
    }

    protected override void OnPause()
    {
        IsForeground = false;
        base.OnPause();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
    }
}

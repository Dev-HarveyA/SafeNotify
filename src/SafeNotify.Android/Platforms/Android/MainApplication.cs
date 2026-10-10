using Android.App;
using Android.Runtime;
using SafeNotify.Android.Platforms.Android;

namespace SafeNotify.Android;

[Application]
public sealed class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();
        AndroidAlertNotifications.CreateChannels(this);
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

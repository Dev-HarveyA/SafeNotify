using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using NativeButton = Android.Widget.Button;
using NativeColor = Android.Graphics.Color;
using NativeView = Android.Views.View;

namespace SafeNotify.Android.Platforms.Android;

public static class AndroidAlertOverlay
{
    private sealed record PendingAlert(MobileAlert Alert, Func<Task<bool>> Acknowledge);

    private static readonly Queue<PendingAlert> Alerts = new();
    private static readonly Handler MainHandler = new(Looper.MainLooper!);
    private static IWindowManager? windowManager;
    private static NativeView? overlay;
    private static bool processing;

    public static bool CanShow(Context context) =>
        global::Android.Provider.Settings.CanDrawOverlays(context);

    public static void Show(Context context, MobileAlert alert, Func<Task<bool>> acknowledge)
    {
        MainHandler.Post(() =>
        {
            Alerts.Enqueue(new PendingAlert(alert, acknowledge));
            ShowNext(context);
        });
    }

    private static void ShowNext(Context context)
    {
        if (processing || Alerts.Count == 0) return;
        if (!CanShow(context))
        {
            var pending = Alerts.Dequeue();
            AndroidAlertNotifications.Post(context, pending.Alert);
            ShowNext(context);
            return;
        }

        processing = true;
        var current = Alerts.Peek();
        windowManager ??= (IWindowManager)context.GetSystemService(Context.WindowService)!;
        overlay = BuildView(context, current);
        var parameters = new WindowManagerLayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent,
            WindowManagerTypes.ApplicationOverlay,
            WindowManagerFlags.LayoutInScreen |
            WindowManagerFlags.LayoutNoLimits |
            WindowManagerFlags.KeepScreenOn,
            Format.Translucent)
        {
            Gravity = GravityFlags.Fill
        };

        try
        {
            windowManager.AddView(overlay, parameters);
        }
        catch
        {
            overlay = null;
            processing = false;
            Alerts.Dequeue();
            AndroidAlertNotifications.Post(context, current.Alert);
            ShowNext(context);
        }
    }

    private static NativeView BuildView(Context context, PendingAlert pending)
    {
        var emergency = pending.Alert.Severity.Equals("emergency", StringComparison.OrdinalIgnoreCase);
        var warning = pending.Alert.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase);
        var accent = NativeColor.ParseColor(emergency ? "#B4232D" : warning ? "#A65A00" : "#087D78");
        var shade = new FrameLayout(context);
        shade.SetBackgroundColor(NativeColor.Argb(232, 15, 24, 28));
        shade.Clickable = true;

        var card = new LinearLayout(context)
        {
            Orientation = Orientation.Vertical
        };
        card.SetGravity(GravityFlags.CenterVertical);
        card.SetPadding(Dp(context, 28), Dp(context, 28), Dp(context, 28), Dp(context, 24));
        var background = new GradientDrawable();
        background.SetColor(NativeColor.White);
        background.SetCornerRadius(Dp(context, 8));
        card.Background = background;

        var bar = new NativeView(context);
        bar.SetBackgroundColor(accent);
        card.AddView(bar, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Dp(context, 5)));
        card.AddView(CreateText(context, pending.Alert.Severity.ToUpperInvariant(), 13, accent, true),
            Margins(context, 0, 24, 0, 8));
        card.AddView(CreateText(context, pending.Alert.Title, 29, NativeColor.ParseColor("#17333A"), true),
            Margins(context, 0, 0, 0, 14));
        card.AddView(CreateText(context, pending.Alert.Message, 19, NativeColor.ParseColor("#263B40"), false),
            Margins(context, 0, 0, 0, 28));

        var status = CreateText(context, "Acknowledge to close this alert.", 14, NativeColor.ParseColor("#617176"), false);
        status.Gravity = GravityFlags.Center;
        var button = new NativeButton(context)
        {
            Text = emergency ? "Acknowledge emergency" : "Acknowledge"
        };
        button.SetAllCaps(false);
        button.SetTextColor(NativeColor.White);
        button.BackgroundTintList = global::Android.Content.Res.ColorStateList.ValueOf(accent);
        button.Click += async (_, _) => await AcknowledgeAsync(pending, button, status);
        card.AddView(button, Margins(context, 0, 0, 0, 8));
        card.AddView(status);

        var cardParameters = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent,
            GravityFlags.Center);
        cardParameters.SetMargins(Dp(context, 24), Dp(context, 24), Dp(context, 24), Dp(context, 24));
        shade.AddView(card, cardParameters);
        return shade;
    }

    private static async Task AcknowledgeAsync(PendingAlert pending, NativeButton button, TextView status)
    {
        button.Enabled = false;
        var succeeded = false;
        try
        {
            succeeded = await pending.Acknowledge();
        }
        catch
        {
        }

        MainHandler.Post(() =>
        {
            if (!succeeded)
            {
                status.Text = "Could not reach the server. Try again.";
                button.Enabled = true;
                return;
            }

            RemoveOverlay();
            if (pending.Alert.Severity.Equals("emergency", StringComparison.OrdinalIgnoreCase))
                AndroidAlertNotifications.StopEmergencySound();
            if (Alerts.Count > 0) Alerts.Dequeue();
            processing = false;
            ShowNext(global::Android.App.Application.Context);
        });
    }

    private static void RemoveOverlay()
    {
        if (overlay is null || windowManager is null) return;
        try { windowManager.RemoveView(overlay); }
        catch { }
        overlay = null;
    }

    private static TextView CreateText(Context context, string text, float size, NativeColor color, bool bold)
    {
        var view = new TextView(context)
        {
            Text = text,
            TextSize = size,
            Gravity = GravityFlags.CenterVertical
        };
        view.SetTextColor(color);
        if (bold) view.SetTypeface(null, TypefaceStyle.Bold);
        return view;
    }

    private static LinearLayout.LayoutParams Margins(Context context, int left, int top, int right, int bottom)
    {
        var parameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent);
        parameters.SetMargins(Dp(context, left), Dp(context, top), Dp(context, right), Dp(context, bottom));
        return parameters;
    }

    private static int Dp(Context context, int value) =>
        (int)(value * context.Resources!.DisplayMetrics!.Density + 0.5f);
}

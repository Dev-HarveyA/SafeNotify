using Android.Media;
using Android.OS;
using Microsoft.Maui.ApplicationModel;
using SafeNotify.Android.Platforms.Android;

namespace SafeNotify.Android;

public partial class MainPage : ContentPage
{
    private readonly HashSet<int> visibleAlerts = [];
    private readonly Queue<MobileAlert> queuedAlerts = [];
    private MobileClientState state = new();
    private MobileClient? client;
    private string? rootCertificate;
    private bool requestPending;
    private bool alertOpen;
    private bool polling;

    public MainPage()
    {
        InitializeComponent();
        Loaded += LoadClientAsync;
        Appearing += PageAppearing;
        MobileAlertEvents.AlertReceived += ReceiveAlert;
        MobileAlertEvents.AccessRevoked += AccessWasRevoked;
    }

    private void UpdateOverlayPermission()
    {
        var allowed = AndroidAlertOverlay.CanShow(global::Android.App.Application.Context);
        OverlayPermissionStatus.IsVisible = !allowed;
        OverlayPermissionButton.IsVisible = !allowed;
    }

    private async void EnableOverlayPermissionClicked(object? sender, EventArgs e)
    {
        var openSettings = await DisplayAlertAsync(
            "Allow pop-up alerts",
            "SafeNotify needs permission to show an alert over the app you are using. Android will ask you to allow display over other apps.",
            "Open settings",
            "Not now");
        if (!openSettings) return;
        var intent = new global::Android.Content.Intent(
            global::Android.Provider.Settings.ActionManageOverlayPermission,
            global::Android.Net.Uri.Parse($"package:{AppInfo.PackageName}"));
        intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
        global::Android.App.Application.Context.StartActivity(intent);
    }

    private async void LoadClientAsync(object? sender, EventArgs e)
    {
        UpdateOverlayPermission();
        state = await MobileClient.LoadStateAsync();
        rootCertificate = await MobileClient.LoadRootAsync();
        if (!string.IsNullOrWhiteSpace(state.ServerUrl)) ServerAddress.Text = state.ServerUrl;
        requestPending = state.RequestId.HasValue;
        RebuildClient();
        if (state.Credential is not null || requestPending)
            await StartPollingServiceAsync();
        await RefreshConnectionAsync();
    }

    private async void PageAppearing(object? sender, EventArgs e)
    {
        UpdateOverlayPermission();
        if (!requestPending) return;
        PagePollingTimer();
        await PollOnceAsync();
    }

    private async void FindClicked(object? sender, EventArgs e) => await RefreshConnectionAsync();

    private async Task RefreshConnectionAsync()
    {
        if (!await TrySetAddressAsync()) return;
        FindButton.IsEnabled = false;
        SetStatus("Checking the server…", "#8A5700");
        try
        {
            var result = await client!.DiscoverAsync();
            OrganisationName.Text = result.Organisation;
            if (state.Credential is not null)
            {
                if (await client.CheckSessionAsync())
                {
                    SetStatus("This device is approved and connected.", "#24734F");
                    JoinButton.IsVisible = false;
                    await StartPollingServiceAsync();
                    return;
                }
                SetStatus("This device is no longer approved. Request access again.", "#A92832");
                JoinButton.IsVisible = true;
                JoinButton.IsEnabled = true;
            }
            else if (requestPending)
            {
                SetStatus("Access request sent. Waiting for administrator approval.", "#8A5700");
                JoinButton.IsVisible = false;
            }
            else
            {
                SetStatus("Organisation found. Request access to receive alerts.", "#24734F");
                JoinButton.IsVisible = true;
            }
        }
        catch
        {
            OrganisationName.Text = "";
            SetStatus("Could not reach the server. Check the address, Wi-Fi and certificate.", "#A92832");
        }
        finally
        {
            FindButton.IsEnabled = true;
        }
    }

    private async void JoinClicked(object? sender, EventArgs e)
    {
        if (!await TrySetAddressAsync()) return;
        JoinButton.IsEnabled = false;
        try
        {
            await client!.RequestAccessAsync(DeviceInfo.Name);
            requestPending = true;
            JoinButton.IsVisible = false;
            SetStatus("Access request sent. Waiting for administrator approval.", "#8A5700");
            await StartPollingServiceAsync();
        }
        catch
        {
            SetStatus("Could not send the request. Check the server connection and try again.", "#A92832");
            JoinButton.IsEnabled = true;
        }
    }

    private async void ImportCertificateClicked(object? sender, EventArgs e)
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose the public SafeNotify LAN certificate" });
            if (file is null) return;
            await using var stream = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var bytes = memory.ToArray();
            using var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(bytes);
            var isAuthority = certificate.Extensions
                .OfType<System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension>()
                .Any(extension => extension.CertificateAuthority);
            if (!certificate.Subject.Contains("SafeNotify LAN Root", StringComparison.OrdinalIgnoreCase) || !isAuthority)
            {
                SetStatus("That is not the public SafeNotify LAN certificate.", "#A92832");
                return;
            }
            rootCertificate = Convert.ToBase64String(bytes);
            await MobileClient.SaveRootAsync(rootCertificate);
            RebuildClient();
            SetStatus("Certificate saved securely. Checking the server again.", "#24734F");
            await RefreshConnectionAsync();
        }
        catch
        {
            SetStatus("Could not import that certificate. Select the public SafeNotify .cer file.", "#A92832");
        }
    }

    private void PagePollingTimer()
    {
        if (polling) return;
        polling = true;
        Dispatcher.StartTimer(TimeSpan.FromSeconds(12), () =>
        {
            if (!IsVisible)
            {
                polling = false;
                return false;
            }
            _ = PollOnceAsync();
            return true;
        });
    }

    private async Task PollOnceAsync()
    {
        if (client is null) return;
        try
        {
            if (requestPending)
            {
                if (await client.CheckApprovalAsync())
                {
                    requestPending = false;
                    SetStatus("Access approved. This device can receive alerts.", "#24734F");
                    await StartPollingServiceAsync();
                }
                else if (state.RequestId is null)
                {
                    requestPending = false;
                    SetStatus("Access request declined. Contact your administrator.", "#A92832");
                    JoinButton.IsVisible = true;
                    JoinButton.IsEnabled = true;
                }
                return;
            }
            if (state.Credential is null) return;
        }
        catch
        {
            SetStatus("Server unavailable. SafeNotify will keep trying while connected.", "#A92832");
        }
    }

    private void ReceiveAlert(MobileAlert alert)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (alert.ExpiresAt <= DateTimeOffset.UtcNow || !visibleAlerts.Add(alert.Id)) return;
            if (alertOpen)
            {
                queuedAlerts.Enqueue(alert);
                return;
            }
            await ShowAlertAsync(alert);
        });
    }

    private void AccessWasRevoked()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            state.Credential = null;
            requestPending = false;
            JoinButton.IsVisible = true;
            JoinButton.IsEnabled = true;
            SetStatus("This device is no longer approved. Request access again.", "#A92832");
        });
    }

    private async Task ShowAlertAsync(MobileAlert alert)
    {
            alertOpen = true;
            await client!.MarkDeliveredAsync(alert.Id);
            var emergency = alert.Severity.Equals("emergency", StringComparison.OrdinalIgnoreCase);
            var warning = alert.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase);
            var tone = PlayAlertSound(warning, emergency);
            if (alert.SpeakMessage) _ = SpeakAlertAsync(alert, emergency, warning);
            while (true)
            {
                var title = $"{alert.Severity.ToUpperInvariant()}{(emergency ? " ALERT" : "")}\n{alert.Title}";
                var acknowledged = await DisplayAlertAsync(
                    title,
                    alert.Message,
                    emergency ? "Acknowledge emergency" : "Acknowledge",
                    "Keep alert open");
                if (!acknowledged) continue;
                if (await client.AcknowledgeAsync(alert.Id))
                {
                    visibleAlerts.Remove(alert.Id);
                    SetStatus("Acknowledgement recorded.", "#24734F");
                    break;
                }
                await DisplayAlertAsync(
                    "Could not record acknowledgement",
                    "Check the server connection and try again.",
                    "Retry",
                    "Keep alert open");
            }
            tone?.Stop();
            tone?.Dispose();
            if (emergency) AndroidAlertNotifications.StopEmergencySound();
            alertOpen = false;
            if (queuedAlerts.TryDequeue(out var next)) await ShowAlertAsync(next);
    }

    private static Ringtone? PlayAlertSound(bool warning, bool emergency)
    {
        if (emergency)
        {
            AndroidAlertNotifications.StartEmergencySound(global::Android.App.Application.Context);
            return null;
        }
        if (!warning)
        {
            var notification = RingtoneManager.GetRingtone(
                global::Android.App.Application.Context,
                RingtoneManager.GetDefaultUri(RingtoneType.Notification));
            notification?.Play();
            return notification;
        }
        var generator = new ToneGenerator(global::Android.Media.Stream.Notification, 90);
        Task.Run(async () =>
        {
            for (var index = 0; index < 3; index++)
            {
                generator.StartTone(Tone.PropBeep, 120);
                await Task.Delay(200);
            }
            await Task.Delay(160);
            generator.Release();
        });
        return null;
    }

    private static async Task SpeakAlertAsync(MobileAlert alert, bool emergency, bool warning)
    {
        if (!emergency) await Task.Delay(warning ? 900 : 500);
        if (emergency) AndroidAlertNotifications.SetEmergencyVolume(0.35f);
        try { await TextToSpeech.Default.SpeakAsync($"{alert.Title}. {alert.Message}"); }
        catch { }
        finally
        {
            if (emergency) AndroidAlertNotifications.SetEmergencyVolume(1f);
        }
    }

    private async Task<bool> TrySetAddressAsync()
    {
        var value = ServerAddress.Text?.Trim().TrimEnd('/');
        if (!Uri.TryCreate(value, UriKind.Absolute, out var address))
        {
            SetStatus("Enter the HTTPS address shown by the SafeNotify server.", "#A92832");
            return false;
        }
        var isSecure = address.Scheme == "https";
        var isLocalHttp = address.Scheme == "http" && address.IsLoopback;
        if (address.AbsolutePath != "/" || address.Query.Length > 0 || address.Fragment.Length > 0 || (!isSecure && !isLocalHttp))
        {
            SetStatus("Enter the HTTPS address shown by the SafeNotify server.", "#A92832");
            return false;
        }
        state.ServerUrl = address.ToString().TrimEnd('/');
        RebuildClient();
        await client!.SaveAsync();
        return true;
    }

    private void RebuildClient()
    {
        client?.Dispose();
        client = new MobileClient(state, rootCertificate);
    }

    private async Task StartPollingServiceAsync()
    {
        if (state.Credential is null && state.RequestId is null) return;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            await Permissions.RequestAsync<Permissions.PostNotifications>();
        var context = global::Android.App.Application.Context;
        var intent = new global::Android.Content.Intent(context, typeof(AlertPollingService));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O) context.StartForegroundService(intent);
        else context.StartService(intent);
        if (requestPending) PagePollingTimer();
    }

    private void SetStatus(string text, string color)
    {
        StatusText.Text = text;
        StatusText.TextColor = Color.FromArgb(color);
    }
}

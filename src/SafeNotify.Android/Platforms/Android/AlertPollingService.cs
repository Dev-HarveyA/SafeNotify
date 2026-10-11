using Android.App;
using Android.Content;
using Android.Media;
using Android.OS;

namespace SafeNotify.Android.Platforms.Android;

public static class MobileAlertEvents
{
    public static event Action<MobileAlert>? AlertReceived;
    public static event Action? AccessRevoked;
    public static void Publish(MobileAlert alert) => AlertReceived?.Invoke(alert);
    public static void PublishAccessRevoked() => AccessRevoked?.Invoke();
}

public static class AndroidAlertNotifications
{
    public const string ChannelId = "safenotify-alerts";
    private const string ServiceChannelId = "safenotify-connection";
    private static readonly object PlaybackLock = new();
    private static MediaPlayer? emergencyPlayer;
    private static MediaPlayer? messagePlayer;
    private static ToneGenerator? warningGenerator;

    public static void CreateChannels(Context context)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        var alerts = new NotificationChannel(ChannelId, "SafeNotify alerts", NotificationImportance.High)
        {
            Description = "Organisation messages, warnings and emergency alerts"
        };
        alerts.SetSound(null, null);
        manager.CreateNotificationChannel(alerts);
        manager.CreateNotificationChannel(new NotificationChannel(ServiceChannelId, "SafeNotify connection", NotificationImportance.Low)
        {
            Description = "Shows when SafeNotify is connected to your organisation"
        });
    }

    public static Notification Connection(Context context) => new Notification.Builder(context, ServiceChannelId)
        .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
        .SetContentTitle("SafeNotify is connected")
        .SetContentText("Listening for organisation alerts on this network")
        .SetOngoing(true)
        .SetCategory(Notification.CategoryService)
        .Build();

    public static void Post(Context context, MobileAlert alert)
    {
        var activityIntent = new Intent(context, typeof(MainActivity));
        activityIntent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        var pending = PendingIntent.GetActivity(context, alert.Id, activityIntent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var notification = new Notification.Builder(context, ChannelId)
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetContentTitle(alert.Title)
            .SetContentText(alert.Message)
            .SetStyle(new Notification.BigTextStyle().BigText(alert.Message))
            .SetContentIntent(pending)
            .SetAutoCancel(true)
            .SetCategory(alert.Severity.Equals("emergency", StringComparison.OrdinalIgnoreCase)
                ? Notification.CategoryAlarm
                : Notification.CategoryMessage)
            .Build();
        ((NotificationManager)context.GetSystemService(Context.NotificationService)!).Notify(alert.Id + 10000, notification);
    }

    public static void UpdateConnection(Context context)
    {
        ((NotificationManager)context.GetSystemService(Context.NotificationService)!).Notify(1901, Connection(context));
    }

    public static void PlayAlertSound(Context context, string severity)
    {
        if (severity.Equals("emergency", StringComparison.OrdinalIgnoreCase))
        {
            StartEmergencySound(context);
            return;
        }

        if (severity.Equals("warning", StringComparison.OrdinalIgnoreCase))
        {
            PlayWarningSound();
            return;
        }

        PlayMessageSound(context);
    }

    public static void StopEmergencySound()
    {
        lock (PlaybackLock)
        {
            try { emergencyPlayer?.Stop(); }
            catch { }
            emergencyPlayer?.Release();
            emergencyPlayer = null;
        }
    }

    public static void ReleaseEmergencySound()
    {
        StopEmergencySound();
        lock (PlaybackLock)
        {
            warningGenerator?.Release();
            warningGenerator = null;
            if (messagePlayer is null) return;
            try { messagePlayer.Stop(); }
            catch { }
            messagePlayer.Release();
            messagePlayer = null;
        }
    }

    public static void StartEmergencySound(Context context)
    {
        lock (PlaybackLock)
        {
            if (emergencyPlayer?.IsPlaying == true) return;
            try
            {
                emergencyPlayer?.Release();
                var player = new MediaPlayer();
                emergencyPlayer = player;
                var alarm = global::Android.Net.Uri.Parse(
                    $"android.resource://{context.PackageName}/{Resource.Raw.safenotify_emergency_tone}");
                if (alarm is null) return;
                var attributes = new AudioAttributes.Builder()
                    .SetUsage(AudioUsageKind.Alarm)
                    .SetContentType(AudioContentType.Sonification)
                    .Build() ?? throw new InvalidOperationException();
                player.SetAudioAttributes(attributes);
                player.SetDataSource(context, alarm);
                player.Looping = true;
                player.Prepare();
                player.Start();
            }
            catch
            {
                emergencyPlayer?.Release();
                emergencyPlayer = null;
            }
        }
    }

    public static void SetEmergencyVolume(float volume)
    {
        lock (PlaybackLock) emergencyPlayer?.SetVolume(volume, volume);
    }

    private static void PlayMessageSound(Context context)
    {
        lock (PlaybackLock)
        {
            try
            {
                if (messagePlayer is not null)
                {
                    try { messagePlayer.Stop(); }
                    catch { }
                    messagePlayer.Release();
                }

                var player = new MediaPlayer();
                messagePlayer = player;
                var sound = RingtoneManager.GetDefaultUri(RingtoneType.Notification)!;
                var attributes = new AudioAttributes.Builder()
                    .SetUsage(AudioUsageKind.Alarm)
                    .SetContentType(AudioContentType.Sonification)
                    .Build() ?? throw new InvalidOperationException();
                player.SetAudioAttributes(attributes);
                player.SetDataSource(context, sound);
                player.Completion += (_, _) => ReleaseMessageSound(player);
                player.Error += (_, _) => ReleaseMessageSound(player);
                player.Prepare();
                player.Start();
            }
            catch
            {
                ReleaseMessageSound(messagePlayer);
            }
        }
    }

    private static void PlayWarningSound()
    {
        lock (PlaybackLock)
        {
            warningGenerator?.Release();
            warningGenerator = new ToneGenerator(global::Android.Media.Stream.Alarm, 90);
            var generator = warningGenerator ?? throw new InvalidOperationException();
            _ = Task.Run(async () =>
            {
                for (var index = 0; index < 3; index++)
                {
                    generator.StartTone(Tone.PropBeep, 180);
                    await Task.Delay(260);
                }

                lock (PlaybackLock)
                {
                    if (!ReferenceEquals(warningGenerator, generator)) return;
                    generator.Release();
                    warningGenerator = null;
                }
            });
        }
    }

    private static void ReleaseMessageSound(MediaPlayer? player)
    {
        if (player is null) return;
        lock (PlaybackLock)
        {
            if (!ReferenceEquals(messagePlayer, player)) return;
            messagePlayer = null;
            player.Release();
        }
    }
}

[Service(
    Name = "org.safenotify.client.AlertPollingService",
    Exported = false,
    ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeSpecialUse)]
public sealed class AlertPollingService : Service
{
    private readonly HashSet<int> postedAlerts = [];
    private CancellationTokenSource? cancellation;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        AndroidAlertNotifications.CreateChannels(this);
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
            StartForeground(1901, AndroidAlertNotifications.Connection(this), global::Android.Content.PM.ForegroundService.TypeSpecialUse);
        else
            StartForeground(1901, AndroidAlertNotifications.Connection(this));
        if (cancellation is null)
        {
            cancellation = new CancellationTokenSource();
            _ = RunAsync(cancellation.Token);
        }
        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
        AndroidAlertNotifications.ReleaseEmergencySound();
        base.OnDestroy();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var state = await MobileClient.LoadStateAsync();
                var root = await MobileClient.LoadRootAsync();
                if (state.RequestId is not null)
                {
                    using var pendingClient = new MobileClient(state, root);
                    if (await pendingClient.CheckApprovalAsync())
                        AndroidAlertNotifications.UpdateConnection(this);
                }
                if (state.Credential is not null)
                {
                    using var client = new MobileClient(state, root);
                    foreach (var alert in await client.GetAlertsAsync())
                    {
                        if (alert.ExpiresAt <= DateTimeOffset.UtcNow || !postedAlerts.Add(alert.Id)) continue;
                        await client.MarkDeliveredAsync(alert.Id);
                        AndroidAlertNotifications.PlayAlertSound(this, alert.Severity);
                        if (AndroidAlertOverlay.CanShow(this))
                            AndroidAlertOverlay.Show(this, alert, () => AcknowledgeAsync(alert.Id));
                        else
                        {
                            AndroidAlertNotifications.Post(this, alert);
                            MobileAlertEvents.Publish(alert);
                        }
                        if (alert.SpeakMessage) await SpeakAlertAsync(alert);
                    }
                }
            }
            catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                MobileAlertEvents.PublishAccessRevoked();
                StopSelf();
                return;
            }
            catch
            {
            }

            try { await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken); }
            catch (System.OperationCanceledException) { return; }
        }
    }

    private async Task<bool> AcknowledgeAsync(int alertId)
    {
        var state = await MobileClient.LoadStateAsync();
        var root = await MobileClient.LoadRootAsync();
        using var client = new MobileClient(state, root);
        return await client.AcknowledgeAsync(alertId);
    }

    private static async Task SpeakAlertAsync(MobileAlert alert)
    {
        var emergency = alert.Severity.Equals("emergency", StringComparison.OrdinalIgnoreCase);
        if (!emergency) await Task.Delay(alert.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase) ? 900 : 500);
        if (emergency) AndroidAlertNotifications.SetEmergencyVolume(0.35f);
        try { await TextToSpeech.Default.SpeakAsync($"{alert.Title}. {alert.Message}"); }
        catch { }
        finally
        {
            if (emergency) AndroidAlertNotifications.SetEmergencyVolume(1f);
        }
    }
}

using System.IO;
using System.Media;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;

namespace SafeNotify.Client;

public partial class AlertWindow : Window
{
    private const double EmergencySpeechAlarmVolume = 0.38;
    private static readonly TimeSpan MessageSoundDuration = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan WarningSoundDuration = TimeSpan.FromMilliseconds(900);
    private readonly Func<Task<bool>> acknowledge;
    private readonly bool emergency;
    private readonly bool soundOwner;
    private readonly bool speakMessage;
    private readonly bool warning;
    private readonly DispatcherTimer pulseTimer;
    private AlarmSound? alarmSound;
    private WarningSound? warningSound;
    private SpeechSynthesizer? speechSynthesizer;
    private bool closeAllowed;
    private bool pulseOn;

    public AlertWindow(string title, string message, string severity, bool speakMessage, Func<Task<bool>> acknowledge, bool soundOwner)
    {
        InitializeComponent();
        this.acknowledge = acknowledge;
        this.soundOwner = soundOwner;
        emergency = string.Equals(severity, "emergency", StringComparison.OrdinalIgnoreCase);
        warning = string.Equals(severity, "warning", StringComparison.OrdinalIgnoreCase);
        this.speakMessage = speakMessage;
        TitleText.Text = title;
        MessageText.Text = message;
        SeverityText.Text = severity.ToUpperInvariant() + (emergency ? " ALERT" : "");
        if (emergency)
        {
            Height = 470;
            Width = 700;
            Shell.BorderBrush = new SolidColorBrush(Color.FromRgb(150, 0, 12));
            Shell.Background = new SolidColorBrush(Color.FromRgb(255, 239, 240));
            AlertStripe.Background = new SolidColorBrush(Color.FromRgb(191, 0, 18));
            SeverityText.Foreground = new SolidColorBrush(Color.FromRgb(176, 0, 19));
            TitleText.Foreground = new SolidColorBrush(Color.FromRgb(125, 0, 13));
            AcknowledgeButton.Background = new SolidColorBrush(Color.FromRgb(174, 0, 18));
            AcknowledgeButton.Content = "ACKNOWLEDGE EMERGENCY";
            InstructionText.Text = "The alarm continues until you acknowledge receipt.";
        }
        else if (string.Equals(severity, "warning", StringComparison.OrdinalIgnoreCase))
        {
            Shell.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 145, 19));
            AlertStripe.Background = new SolidColorBrush(Color.FromRgb(224, 145, 11));
            SeverityText.Foreground = new SolidColorBrush(Color.FromRgb(160, 96, 0));
            AcknowledgeButton.Background = new SolidColorBrush(Color.FromRgb(184, 111, 0));
        }
        pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        pulseTimer.Tick += PulseTimer_Tick;
        Loaded += Window_Loaded;
    }

    public void AllowClose()
    {
        closeAllowed = true;
    }

    public void SetMonitorBounds(int left, int top, int width, int height)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        MinWidth = Math.Min(MinWidth, width - 24);
        MinHeight = Math.Min(MinHeight, height - 24);
        Width = Math.Min(Width, width - 24);
        Height = Math.Min(Height, height - 24);
        Left = left + (width - Width) / 2;
        Top = top + (height - Height) / 2;
    }

    public void SetAcknowledgementPending()
    {
        AcknowledgeButton.IsEnabled = false;
        InstructionText.Text = "Recording your acknowledgement…";
    }

    public void SetAcknowledgementFailed()
    {
        AcknowledgeButton.IsEnabled = true;
        InstructionText.Text = "Could not record acknowledgement. Check the network and try again.";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Activate();
        if (!soundOwner) return;
        if (emergency)
        {
            try
            {
                alarmSound = new AlarmSound();
                alarmSound.Start();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException) { }
            pulseTimer.Start();
        }
        else if (warning)
        {
            try
            {
                warningSound = new WarningSound();
                warningSound.Play();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException) { }
        }
        else SystemSounds.Asterisk.Play();
        if (!speakMessage) return;
        if (!emergency) await Task.Delay(warning ? WarningSoundDuration : MessageSoundDuration);
        await SpeakAlertAsync();
    }

    private async Task SpeakAlertAsync()
    {
        if (!speakMessage || !soundOwner || closeAllowed) return;
        EventHandler<SpeakCompletedEventArgs>? completedHandler = null;
        try
        {
            if (emergency) SetAlarmVolume(EmergencySpeechAlarmVolume);
            speechSynthesizer = new SpeechSynthesizer();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            completedHandler = (_, eventArgs) =>
            {
                if (eventArgs.Cancelled) completion.TrySetCanceled();
                else if (eventArgs.Error is not null) completion.TrySetException(eventArgs.Error);
                else completion.TrySetResult();
            };
            speechSynthesizer.SpeakCompleted += completedHandler;
            speechSynthesizer.SpeakAsync($"{TitleText.Text}. {MessageText.Text}");
            await completion.Task;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        finally
        {
            if (speechSynthesizer is not null)
            {
                if (completedHandler is not null) speechSynthesizer.SpeakCompleted -= completedHandler;
                speechSynthesizer.SpeakAsyncCancelAll();
                speechSynthesizer.Dispose();
                speechSynthesizer = null;
            }
            if (emergency && !closeAllowed) SetAlarmVolume(1);
        }
    }

    private void SetAlarmVolume(double volume)
    {
        try { alarmSound?.SetVolume(volume); }
        catch (Exception) { }
    }

    private async void Acknowledge_Click(object sender, RoutedEventArgs e)
    {
        AcknowledgeButton.IsEnabled = false;
        InstructionText.Text = "Recording your acknowledgement…";
        var succeeded = await acknowledge();
        if (!succeeded)
        {
            AcknowledgeButton.IsEnabled = true;
            InstructionText.Text = "Could not record acknowledgement. Check the network and try again.";
            return;
        }
        AllowClose();
        Close();
    }

    private void PulseTimer_Tick(object? sender, EventArgs e)
    {
        pulseOn = !pulseOn;
        Shell.BorderBrush = new SolidColorBrush(pulseOn ? Color.FromRgb(150, 0, 12) : Color.FromRgb(240, 20, 35));
        AlertStripe.Background = new SolidColorBrush(pulseOn ? Color.FromRgb(191, 0, 18) : Color.FromRgb(240, 20, 35));
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!closeAllowed)
        {
            e.Cancel = true;
            return;
        }
        speechSynthesizer?.SpeakAsyncCancelAll();
        speechSynthesizer?.Dispose();
        speechSynthesizer = null;
        StopAlarm();
    }

    private void StopAlarm()
    {
        pulseTimer.Stop();
        alarmSound?.Dispose();
        alarmSound = null;
        warningSound?.Dispose();
        warningSound = null;
    }

}

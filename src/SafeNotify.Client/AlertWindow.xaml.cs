using System.IO;
using System.Media;
using System.Speech.Synthesis;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using FormsScreen = System.Windows.Forms.Screen;
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

    private sealed class AlarmSound : IDisposable
    {
        private const int SampleRate = 22050;
        private MemoryStream stream = CreateAlarmWave(1);
        private SoundPlayer player;

        public AlarmSound()
        {
            stream.Position = 0;
            player = new SoundPlayer(stream);
            player.Load();
        }

        public void Start() => player.PlayLooping();

        public void SetVolume(double volume)
        {
            player.Stop();
            player.Dispose();
            stream.Dispose();
            stream = CreateAlarmWave(volume);
            player = new SoundPlayer(stream);
            player.Load();
            player.PlayLooping();
        }

        public void Dispose()
        {
            player.Stop();
            player.Dispose();
            stream.Dispose();
        }

        private static MemoryStream CreateAlarmWave(double volume)
        {
            const int durationMilliseconds = 10000;
            var sampleCount = SampleRate * durationMilliseconds / 1000;
            var stream = new MemoryStream(44 + sampleCount * 2);
            using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + sampleCount * 2);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(sampleCount * 2);
            var phase = 0d;
            for (var index = 0; index < sampleCount; index++)
            {
                phase += 2 * Math.PI * 880 / SampleRate;
                var tone = Math.Sin(phase) * 0.78 + Math.Sin(phase * 2.01) * 0.17 + Math.Sin(phase * 3.02) * 0.05;
                var sample = tone * 0.98 * Math.Clamp(volume, 0, 1);
                writer.Write((short)(sample * short.MaxValue));
            }
            stream.Position = 0;
            return stream;
        }
    }

    private sealed class WarningSound : IDisposable
    {
        private const int SampleRate = 22050;
        private readonly MemoryStream stream = CreateWarningWave();
        private readonly SoundPlayer player;

        public WarningSound()
        {
            stream.Position = 0;
            player = new SoundPlayer(stream);
            player.Load();
        }

        public void Play() => player.Play();

        public void Dispose()
        {
            player.Stop();
            player.Dispose();
            stream.Dispose();
        }

        private static MemoryStream CreateWarningWave()
        {
            const int durationMilliseconds = 800;
            var sampleCount = SampleRate * durationMilliseconds / 1000;
            var stream = new MemoryStream(44 + sampleCount * 2);
            using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + sampleCount * 2);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(sampleCount * 2);
            for (var index = 0; index < sampleCount; index++)
            {
                var time = (double)index / SampleRate;
                var position = time % 0.27;
                var active = position < 0.11;
                var edge = Math.Min(position, 0.11 - position);
                var envelope = active ? Math.Clamp(edge / 0.008, 0, 1) : 0;
                var sample = Math.Sin(2 * Math.PI * 1000 * time) * envelope * 0.88;
                writer.Write((short)(sample * short.MaxValue));
            }
            stream.Position = 0;
            return stream;
        }
    }
}

public sealed class AlertWindowGroup
{
    private readonly Func<Task<bool>> acknowledge;
    private readonly List<AlertWindow> windows = [];
    private EmergencyAudioDucker? audioDucker;
    private bool acknowledgementInProgress;
    private bool closed;

    private AlertWindowGroup(Func<Task<bool>> acknowledge, bool emergency)
    {
        this.acknowledge = acknowledge;
        if (emergency) audioDucker = new EmergencyAudioDucker();
    }

    public event Func<Task>? AcknowledgedAndClosed;

    public static AlertWindowGroup ShowOnEveryScreen(string title, string message, string severity, bool speakMessage, Func<Task<bool>> acknowledge)
    {
        var group = new AlertWindowGroup(acknowledge, string.Equals(severity, "emergency", StringComparison.OrdinalIgnoreCase));
        var screens = FormsScreen.AllScreens;
        for (var index = 0; index < screens.Length; index++)
        {
            var window = new AlertWindow(title, message, severity, speakMessage, group.AcknowledgeAsync, index == 0);
            var bounds = screens[index].WorkingArea;
            window.SetMonitorBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            group.windows.Add(window);
        }
        foreach (var window in group.windows)
        {
            window.Show();
            window.Activate();
        }
        return group;
    }

    public void CloseForShutdown()
    {
        if (closed) return;
        closed = true;
        foreach (var window in windows)
        {
            window.AllowClose();
            if (window.IsVisible) window.Close();
        }
        audioDucker?.Dispose();
        audioDucker = null;
    }

    private async Task<bool> AcknowledgeAsync()
    {
        if (acknowledgementInProgress || closed) return false;
        acknowledgementInProgress = true;
        foreach (var window in windows) window.SetAcknowledgementPending();
        bool succeeded;
        try
        {
            succeeded = await acknowledge();
        }
        catch
        {
            succeeded = false;
        }
        if (!succeeded)
        {
            acknowledgementInProgress = false;
            foreach (var window in windows) window.SetAcknowledgementFailed();
            return false;
        }
        closed = true;
        foreach (var window in windows) window.AllowClose();
        foreach (var window in windows)
        {
            if (window.IsVisible) window.Close();
        }
        audioDucker?.Dispose();
        audioDucker = null;
        if (AcknowledgedAndClosed is not null) await AcknowledgedAndClosed.Invoke();
        return true;
    }
}

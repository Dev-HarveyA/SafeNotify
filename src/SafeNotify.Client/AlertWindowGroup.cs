using System.Windows.Forms;
using FormsScreen = System.Windows.Forms.Screen;

namespace SafeNotify.Client;

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

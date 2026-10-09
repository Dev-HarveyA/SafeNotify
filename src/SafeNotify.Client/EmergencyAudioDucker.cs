using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace SafeNotify.Client;

public sealed class EmergencyAudioDucker : IDisposable
{
    private readonly Dictionary<string, bool> originalMuteStates = new(StringComparer.Ordinal);
    private MMDeviceEnumerator? enumerator;
    private DispatcherTimer? timer;
    private bool disposed;

    public EmergencyAudioDucker()
    {
        try
        {
            enumerator = new MMDeviceEnumerator();
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += Timer_Tick;
            timer.Start();
            MuteOtherSessions();
        }
        catch
        {
            timer?.Stop();
            enumerator?.Dispose();
            timer = null;
            enumerator = null;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer?.Stop();
        RestoreOtherSessions();
        enumerator?.Dispose();
        timer = null;
        enumerator = null;
    }

    private void Timer_Tick(object? sender, EventArgs e) => MuteOtherSessions();

    private void MuteOtherSessions()
    {
        if (enumerator is null || disposed) return;
        try
        {
            using var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            for (var deviceIndex = 0; deviceIndex < devices.Count; deviceIndex++)
            {
                using var device = devices[deviceIndex];
                using var sessions = device.AudioSessionManager.Sessions;
                for (var sessionIndex = 0; sessionIndex < sessions.Count; sessionIndex++)
                {
                    using var session = sessions[sessionIndex];
                    try
                    {
                        if (session.GetProcessID == (uint)Environment.ProcessId) continue;
                        var key = $"{device.ID}|{session.GetSessionInstanceIdentifier}";
                        using var volume = session.SimpleAudioVolume;
                        if (!originalMuteStates.ContainsKey(key)) originalMuteStates.Add(key, volume.Mute);
                        if (!volume.Mute) volume.Mute = true;
                    }
                    catch (Exception exception) when (exception is COMException or InvalidOperationException or ArgumentException) { }
                }
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or ArgumentException) { }
    }

    private void RestoreOtherSessions()
    {
        if (enumerator is null || originalMuteStates.Count == 0) return;
        try
        {
            using var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            for (var deviceIndex = 0; deviceIndex < devices.Count; deviceIndex++)
            {
                using var device = devices[deviceIndex];
                using var sessions = device.AudioSessionManager.Sessions;
                for (var sessionIndex = 0; sessionIndex < sessions.Count; sessionIndex++)
                {
                    using var session = sessions[sessionIndex];
                    try
                    {
                        var key = $"{device.ID}|{session.GetSessionInstanceIdentifier}";
                        if (!originalMuteStates.TryGetValue(key, out var wasMuted)) continue;
                        using var volume = session.SimpleAudioVolume;
                        volume.Mute = wasMuted;
                        originalMuteStates.Remove(key);
                    }
                    catch (Exception exception) when (exception is COMException or InvalidOperationException or ArgumentException) { }
                }
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or ArgumentException) { }
    }
}

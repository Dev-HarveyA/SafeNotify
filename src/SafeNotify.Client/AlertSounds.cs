using System.IO;
using System.Media;
using System.Text;

namespace SafeNotify.Client;

internal sealed class AlarmSound : IDisposable
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

internal sealed class WarningSound : IDisposable
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

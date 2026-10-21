using NAudio.Wave;

namespace Ghostbar.Services;

class AudioRecorder
{
    private WaveInEvent? input;
    private WaveFileWriter? writer;
    private string? path;

    public Action<string?>? OnDone { get; set; }

    public void Start()
    {
        path = Path.Combine(Path.GetTempPath(), $"ghostbar_audio_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.wav");
        var format = new WaveFormat(16000, 16, 1);
        writer = new WaveFileWriter(path, format);
        input = new WaveInEvent { WaveFormat = format };
        input.DataAvailable += (_, e) => writer?.Write(e.Buffer, 0, e.BytesRecorded);
        input.RecordingStopped += (_, _) =>
        {
            writer?.Dispose();
            writer = null;
            input?.Dispose();
            input = null;
            OnDone?.Invoke(path);
        };
        input.StartRecording();
    }

    public void Stop() => input?.StopRecording();
}

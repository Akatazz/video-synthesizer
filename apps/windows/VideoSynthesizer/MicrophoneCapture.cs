using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VideoSynthesizer;

/// <summary>WASAPI microphone capture; never started by application startup.</summary>
public sealed class MicrophoneCapture : IDisposable
{
    private WasapiCapture? capture;
    private readonly object gate = new();
    private AudioBands bands;
    private string? error;
    public AudioBands Bands { get { lock (gate) return bands; } }
    public string? Error { get { lock (gate) return error; } }
    public bool IsRunning { get; private set; }

    public void Start()
    {
        Stop();
        var analysis = new AudioAnalysis();
        var next = new WasapiCapture();
        try
        {
            // WASAPI shared-mode conversion supplies a fixed mono float stream.
            next.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
            next.DataAvailable += (_, data) =>
            {
                AudioBands latest = default;
                for (int i = 0; i + 4 <= data.BytesRecorded; i += 4)
                    latest = analysis.Push(BitConverter.ToSingle(data.Buffer, i), 48000);
                lock (gate) { if (ReferenceEquals(capture, next)) bands = latest; }
            };
            next.RecordingStopped += (_, stopped) =>
            {
                lock (gate)
                {
                    if (!ReferenceEquals(capture, next)) return;
                    bands = default;
                    error = stopped.Exception?.Message ?? "Microphone capture stopped.";
                    IsRunning = false;
                }
            };
            lock (gate) { capture = next; error = null; IsRunning = true; }
            next.StartRecording();
        }
        catch { Stop(); throw; }
    }

    public void Stop()
    {
        WasapiCapture? previous;
        lock (gate) { previous = capture; capture = null; bands = default; error = null; IsRunning = false; }
        // Dispose joins the capture thread after releasing the publication lock.
        previous?.Dispose();
    }

    public void Dispose() => Stop();
}

using System.Collections.Concurrent;
using System.Diagnostics;

namespace VideoSynthesizer;

public sealed class NdiOutput : IDisposable
{
    private sealed record Frame(byte[] Pixels, int Width, int Height, NdiSettings Settings);
    private BlockingCollection<Frame>? queue;
    private Task? worker;
    private long lastSubmitted;
    private volatile string status = "NDI off";
    private volatile bool running;
    public bool IsRunning => running;
    public string Status => status;

    public void Start(string name, string? path)
    {
        Stop();
        var sender = new NdiSender(NdiRuntime.Load(path), name);
        var pending = new BlockingCollection<Frame>(1);
        queue = pending;
        running = true;
        status = "NDI sending · waiting for receivers";
        lastSubmitted = 0;
        worker = Task.Run(() =>
        {
            try
            {
                foreach (var frame in pending.GetConsumingEnumerable())
                {
                    if (!running) break;
                    var pixels = LightingOutput.Process(frame.Pixels, frame.Width, frame.Height, frame.Settings);
                    sender.Send(pixels, frame.Settings.Width, frame.Settings.Height, frame.Settings.Fps);
                    var tally = sender.Telemetry();
                    status = $"NDI sending · {frame.Settings.Width} × {frame.Settings.Height} · {frame.Settings.Fps} fps · {tally.Receivers} receiver(s) · {(tally.Program ? "PROGRAM" : tally.Preview ? "PREVIEW" : "No tally")}";
                }
            }
            catch (Exception error) { status = $"NDI error: {error.Message}"; }
            finally { sender.Dispose(); running = false; }
        });
    }

    public void Submit(byte[] pixels, int width, int height, NdiSettings settings)
    {
        var pending = queue;
        if (!running || pending == null || pending.IsAddingCompleted || pending.Count != 0) return;
        long now = Stopwatch.GetTimestamp();
        if ((now - lastSubmitted) / (double)Stopwatch.Frequency < 1.0 / settings.Fps) return;
        // Snapshot the frame before the renderer reuses its pixel buffer.
        if (pending.TryAdd(new Frame(pixels.ToArray(), width, height, settings))) lastSubmitted = now;
    }

    public void Stop()
    {
        running = false;
        queue?.CompleteAdding();
        worker?.GetAwaiter().GetResult();
        queue?.Dispose();
        queue = null; worker = null; status = "NDI off";
    }
    public void Dispose() => Stop();
}

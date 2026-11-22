namespace VideoSynthesizer;

/// <summary>Single-owner renderer; calls must be serialized by the application.</summary>
public interface ISynthRenderer : IDisposable
{
    int Width { get; }
    int Height { get; }
    byte[] Pixels { get; }
    string Backend { get; }
    void Reset();
    void Render(float time, SynthParameters parameters, AudioBands audio = default);
}

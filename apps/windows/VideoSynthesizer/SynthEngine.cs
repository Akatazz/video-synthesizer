namespace VideoSynthesizer;

/// <summary>Independent CPU renderer for the native Windows preview.</summary>
public sealed class SynthEngine
{
    public int Width { get; }
    public int Height { get; }
    private float[] history;
    private float[] next;
    public byte[] Pixels { get; }

    public SynthEngine(int width, int height)
    {
        if (width < 1 || height < 1 || width > 1920 || height > 1080)
            throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        history = new float[width * height * 3];
        next = new float[history.Length];
        Pixels = new byte[width * height * 4];
    }

    public void Reset()
    {
        Array.Clear(history);
        Array.Clear(next);
        Array.Clear(Pixels);
    }

    private float Sample(float x, float y, int channel)
    {
        // Match Metal's normalized, linear, clamp-to-edge sampler.
        x = Math.Clamp(x * Width - 0.5f, 0, Width - 1);
        y = Math.Clamp(y * Height - 0.5f, 0, Height - 1);
        int x0 = (int)x, y0 = (int)y;
        int x1 = Math.Min(x0 + 1, Width - 1), y1 = Math.Min(y0 + 1, Height - 1);
        float a = x - x0, b = y - y0;
        float top = float.Lerp(history[(y0 * Width + x0) * 3 + channel], history[(y0 * Width + x1) * 3 + channel], a);
        float bottom = float.Lerp(history[(y1 * Width + x0) * 3 + channel], history[(y1 * Width + x1) * 3 + channel], a);
        return float.Lerp(top, bottom, b);
    }

    public void Render(float time, SynthParameters p)
    {
        p.Validate();
        if (!float.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        Parallel.For(0, Height, y =>
        {
            for (int x = 0; x < Width; x++)
            {
                float u = (x + 0.5f) / Width, v = (y + 0.5f) / Height;
                float wx = MathF.Sin(v * p.OscFreq + time * p.OscSpeed) * 0.01f;
                float wy = MathF.Cos(u * p.OscFreq + time * p.OscSpeed) * 0.01f;
                float zx = (u + wx - 0.5f) * p.FractalZoom * 5;
                float zy = (v + wy - 0.5f) * p.FractalZoom * 5;
                float iterations = 0;
                for (int i = 0; i < 30 && i < p.FractalDetail; i++)
                {
                    (zx, zy) = (zx * zx - zy * zy + p.FractalMorph,
                        2 * zx * zy + 0.156f + MathF.Sin(time * 0.1f) * 0.1f);
                    if (zx * zx + zy * zy > 4) break;
                    iterations++;
                }
                float smooth = iterations - MathF.Log2(MathF.Max(1, MathF.Log2(MathF.Max(zx * zx + zy * zy, 1e-10f))));
                float value = smooth / p.FractalDetail;
                int pixel = y * Width + x;
                float reactionRed = 0;
                for (int channel = 0; channel < 3; channel++)
                {
                    float laplacian = -Sample(u, v, channel);
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            float weight = dx == 0 || dy == 0 ? 0.2f : 0.05f;
                            laplacian += Sample(u + (float)dx / Width, v + (float)dy / Height, channel) * weight;
                        }
                    float last = Sample(u + wx * 0.5f, v + wy * 0.5f, channel);
                    float reaction = last + laplacian - last * last * last + p.CaFeed * (1 - last) - p.CaKill * last;
                    if (channel == 0) reactionRed = reaction;
                    float phase = channel == 0 ? 0 : channel == 1 ? 0.33f : 0.67f;
                    float palette = 0.5f + 0.5f * MathF.Cos(6.28318f * (reactionRed + p.ColorShift + phase));
                    float fractal = channel == 0 ? value : channel == 1 ? value * 0.5f : MathF.Sin(value * 3.14f) * 0.5f + 0.5f;
                    float composite = float.Lerp(palette * reaction, fractal, 0.05f);
                    float result = Math.Clamp(float.Lerp(fractal, composite, p.FeedbackMix), 0, 1);
                    next[pixel * 3 + channel] = result;
                    // BGRA for WriteableBitmap; retain float history for the next frame.
                    Pixels[pixel * 4 + (2 - channel)] = (byte)MathF.Round(result * 255);
                }
                Pixels[pixel * 4 + 3] = 255;
            }
        });
        (history, next) = (next, history);
    }
}

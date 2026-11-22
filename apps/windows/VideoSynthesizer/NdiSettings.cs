namespace VideoSynthesizer;

public sealed record NdiSettings
{
    public int Width { get; init; } = 512;
    public int Height { get; init; } = 512;
    public int Fps { get; init; } = 30;
    public float BlackCutoff { get; init; } = 0.02f;
    public float Gamma { get; init; } = 1;
    public int Channel { get; init; }
    public int Alpha { get; init; }

    public void Validate()
    {
        if (Width < 2 || Width > 1920 || Width % 2 != 0 || Height < 1 || Height > 1080 ||
            Fps is not (15 or 25 or 30) || !float.IsFinite(BlackCutoff) || BlackCutoff < 0 || BlackCutoff > 0.25f ||
            !float.IsFinite(Gamma) || Gamma < 0.5f || Gamma > 2.5f || Channel < 0 || Channel > 4 || Alpha < 0 || Alpha > 2)
            throw new ArgumentException("Invalid NDI output settings.");
    }
}

public static class LightingOutput
{
    /// <summary>Top-down BGRA, bilinear resize, straight alpha. Matches the web lighting pass.</summary>
    public static byte[] Process(byte[] source, int width, int height, NdiSettings p)
    {
        p.Validate();
        if (width < 1 || height < 1 || width > 4096 || height > 4096 || source.Length != width * height * 4)
            throw new ArgumentException("Invalid source image.");
        var output = new byte[p.Width * p.Height * 4];
        Parallel.For(0, p.Height, y =>
        {
            float sy = Math.Clamp((y + 0.5f) * height / p.Height - 0.5f, 0, height - 1);
            int y0 = (int)sy, y1 = Math.Min(y0 + 1, height - 1);
            for (int x = 0; x < p.Width; x++)
            {
                float sx = Math.Clamp((x + 0.5f) * width / p.Width - 0.5f, 0, width - 1);
                int x0 = (int)sx, x1 = Math.Min(x0 + 1, width - 1);
                float Read(int c)
                {
                    float top = float.Lerp(source[(y0 * width + x0) * 4 + c], source[(y0 * width + x1) * 4 + c], sx - x0);
                    float bottom = float.Lerp(source[(y1 * width + x0) * 4 + c], source[(y1 * width + x1) * 4 + c], sx - x0);
                    return float.Lerp(top, bottom, sy - y0) / 255;
                }
                float b = Read(0), g = Read(1), r = Read(2);
                if (r * 0.2126f + g * 0.7152f + b * 0.0722f < p.BlackCutoff) r = g = b = 0;
                r = MathF.Pow(r, p.Gamma); g = MathF.Pow(g, p.Gamma); b = MathF.Pow(b, p.Gamma);
                if (p.Channel != 0)
                {
                    float mono = p.Channel == 1 ? r * 0.2126f + g * 0.7152f + b * 0.0722f : p.Channel == 2 ? r : p.Channel == 3 ? g : b;
                    r = g = b = mono;
                }
                float luma = r * 0.2126f + g * 0.7152f + b * 0.0722f;
                float alpha = p.Alpha == 0 ? 1 : p.Alpha == 1 ? luma : 1 - luma;
                int pixel = (y * p.Width + x) * 4;
                static byte Encode(float value) => (byte)MathF.Round(Math.Clamp(value, 0, 1) * 255);
                output[pixel] = Encode(b); output[pixel + 1] = Encode(g); output[pixel + 2] = Encode(r); output[pixel + 3] = Encode(alpha);
            }
        });
        return output;
    }
}

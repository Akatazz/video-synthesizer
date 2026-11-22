namespace VideoSynthesizer;

public readonly record struct AudioBands(float Bass, float Mid, float High);

/// <summary>512-sample periodic Hann FFT. Called by a single capture worker.</summary>
public sealed class AudioAnalysis
{
    public const int Size = 512;
    private readonly float[] samples = new float[Size];
    private readonly double[] real = new double[Size], imaginary = new double[Size];
    private int count;
    private AudioBands smoothed;

    public AudioBands Push(float sample, int sampleRate)
    {
        if (sampleRate < 8000 || sampleRate > 192000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        samples[count++] = float.IsFinite(sample) ? Math.Clamp(sample, -1, 1) : 0;
        if (count != Size) return smoothed;
        count = 0;
        double mean = samples.Average(value => (double)value);
        for (int i = 0; i < Size; i++)
        {
            real[i] = (samples[i] - mean) * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / Size));
            imaginary[i] = 0;
        }
        for (int i = 1, j = 0; i < Size; i++)
        {
            int bit = Size >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (real[i], real[j]) = (real[j], real[i]); (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]); }
        }
        for (int length = 2; length <= Size; length *= 2)
        {
            double angle = -2 * Math.PI / length;
            for (int offset = 0; offset < Size; offset += length)
                for (int j = 0; j < length / 2; j++)
                {
                    double cos = Math.Cos(angle * j), sin = Math.Sin(angle * j);
                    int a = offset + j, b = a + length / 2;
                    double r = real[b] * cos - imaginary[b] * sin, im = real[b] * sin + imaginary[b] * cos;
                    real[b] = real[a] - r; imaginary[b] = imaginary[a] - im;
                    real[a] += r; imaginary[a] += im;
                }
        }
        double bass = 0, mid = 0, high = 0;
        for (int bin = 1; bin <= Size / 2; bin++)
        {
            double hz = (double)bin * sampleRate / Size;
            if (hz < 20) continue;
            double power = (real[bin] * real[bin] + imaginary[bin] * imaginary[bin]) * 16 / (Size * Size * 1.5);
            if (hz < 250) bass += power;
            else if (hz < 2000) mid += power;
            else high += power;
        }
        static float Smooth(float last, double power) => last * 0.8f + (float)Math.Min(1, Math.Sqrt(power)) * 0.2f;
        smoothed = new(Smooth(smoothed.Bass, bass), Smooth(smoothed.Mid, mid), Smooth(smoothed.High, high));
        return smoothed;
    }
}

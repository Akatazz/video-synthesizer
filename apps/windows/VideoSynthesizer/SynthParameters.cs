using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VideoSynthesizer;

public sealed record SynthParameters
{
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    [JsonRequired] public float OscFreq { get; init; } = 2;
    [JsonRequired] public float OscSpeed { get; init; } = 0.5f;
    [JsonRequired] public float FractalZoom { get; init; } = 1.5f;
    [JsonRequired] public float FractalDetail { get; init; } = 10;
    [JsonRequired] public float FractalMorph { get; init; } = 0.2f;
    [JsonRequired] public float CaFeed { get; init; } = 0.045f;
    [JsonRequired] public float CaKill { get; init; } = 0.06f;
    [JsonRequired] public float FeedbackMix { get; init; } = 0.95f;
    [JsonRequired] public float ColorShift { get; init; }
    public float AudioToOsc { get; init; } = 0.5f;
    public float AudioToFractal { get; init; } = 0.5f;
    public float AudioToCA { get; init; } = 0.2f;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public void Validate()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("Unsupported preset version.");
        Check(OscFreq, 0, 30, "Oscillator frequency");
        Check(OscSpeed, 0, 5, "Oscillator speed");
        Check(FractalZoom, 0.1f, 4, "Fractal zoom");
        Check(FractalDetail, 1, 30, "Fractal detail");
        Check(FractalMorph, -1, 1, "Fractal morph");
        Check(CaFeed, 0, 0.1f, "Cell feed");
        Check(CaKill, 0, 0.1f, "Cell kill");
        Check(FeedbackMix, 0, 1, "Feedback mix");
        Check(ColorShift, 0, 1, "Color shift");
        Check(AudioToOsc, 0, 1, "Mid to oscillator");
        Check(AudioToFractal, 0, 1, "Bass to fractal");
        Check(AudioToCA, 0, 1, "High to cell feed");
    }

    private static void Check(float value, float min, float max, string name)
    {
        if (!float.IsFinite(value) || value < min || value > max)
            throw new InvalidDataException($"{name} must be between {min} and {max}.");
    }

    public static SynthParameters FromJson(string json)
    {
        var preset = JsonSerializer.Deserialize<SynthParameters>(json, JsonOptions)
            ?? throw new InvalidDataException("The preset is empty.");
        preset.Validate();
        return preset;
    }

    public string ToJson()
    {
        Validate();
        return JsonSerializer.Serialize(this, JsonOptions);
    }
}

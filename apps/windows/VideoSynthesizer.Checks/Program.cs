using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using VideoSynthesizer;

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

string fixture = File.ReadAllText(args.Length > 0 ? args[0] : "contracts/presets/classic-feedback.json");
var preset = SynthParameters.FromJson(fixture);
Require(preset == new SynthParameters(), "Canonical preset must match native defaults.");
Require(SynthParameters.FromJson(preset.ToJson()) == preset, "Preset round trip changed values.");
var routed = preset with { AudioToOsc = 0.15f, AudioToFractal = 0.75f, AudioToCA = 1 };
Require(SynthParameters.FromJson(routed.ToJson()) == routed, "Audio routing did not survive preset save/load.");

foreach (string key in JsonNode.Parse(fixture)!.AsObject().Select(pair => pair.Key))
{
    var missing = JsonNode.Parse(fixture)!.AsObject();
    missing.Remove(key);
    bool rejected = false;
    try { SynthParameters.FromJson(missing.ToJsonString()); }
    catch (JsonException) { rejected = true; }
    Require(rejected, $"Missing {key} must be rejected.");
}
foreach (string invalid in new[] {
    fixture.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"),
    fixture.Replace("\"oscFreq\": 2", "\"oscFreq\": -1"),
    fixture.Replace("\"fractalDetail\": 10", "\"fractalDetail\": 0"),
    "null", "{}", "{malformed}"
})
{
    bool rejected = false;
    try { SynthParameters.FromJson(invalid); }
    catch (Exception error) when (error is JsonException or InvalidDataException) { rejected = true; }
    Require(rejected, "Invalid preset was accepted.");
}
bool nonfiniteRejected = false;
try { (preset with { OscFreq = float.NaN }).Validate(); }
catch (InvalidDataException) { nonfiniteRejected = true; }
Require(nonfiniteRejected, "NaN must be rejected.");

var engine = new SynthEngine(64, 36);
engine.Render(1, preset);
var first = engine.Pixels.ToArray();
Require(first.Where((_, i) => i % 4 != 3).Distinct().Count() > 20, "Output is blank or constant.");
Require(first.Where((_, i) => i % 4 == 3).All(value => value == 255), "Output must be opaque.");
engine.Render(1, preset);
Require(!first.SequenceEqual(engine.Pixels), "Feedback must affect the next frame.");
engine.Reset();
engine.Render(1, preset);
Require(first.SequenceEqual(engine.Pixels), "Reset must reproduce a clean first frame.");
engine.Reset();
engine.Render(1, preset with { FractalMorph = -0.8f, ColorShift = 0.4f });
Require(!first.SequenceEqual(engine.Pixels), "Controls must change rendered output.");
var dry = preset with { FeedbackMix = 0 };
engine.Reset();
engine.Render(1, dry);
var dryFrame = engine.Pixels.ToArray();
engine.Render(1, dry);
Require(dryFrame.SequenceEqual(engine.Pixels), "Dry output must be independent of history.");
var preview = new SynthEngine(480, 270);
foreach (int bin in new[] { 2, 10, 64 })
{
    var analyzer = new AudioAnalysis();
    AudioBands measured = default;
    for (int sample = 0; sample < 512 * 40; sample++)
        measured = analyzer.Push(0.8f * MathF.Sin(2 * MathF.PI * bin * sample / 512), 32000);
    float[] levels = [measured.Bass, measured.Mid, measured.High];
    int expected = bin == 2 ? 0 : bin == 10 ? 1 : 2;
    Require(levels[expected] > 0.75f && levels[expected] < 0.85f, "FFT tone amplitude or band is wrong.");
    Require(levels.Where((_, index) => index != expected).All(value => value < 0.01f), "FFT leaks into an unrelated band.");
}
var dc = new AudioAnalysis();
AudioBands silence = default;
for (int sample = 0; sample < 1024; sample++) silence = dc.Push(0.5f, 44100);
Require(silence == default, "DC input must not produce audio modulation.");
for (int sample = 0; sample < 1024; sample++) silence = dc.Push(float.NaN, 48000);
Require(silence == default, "Non-finite input must not produce audio modulation.");
var noRoutes = preset with { AudioToOsc = 0, AudioToFractal = 0, AudioToCA = 0 };
engine.Reset(); engine.Render(1, noRoutes);
var unmodulated = engine.Pixels.ToArray();
engine.Reset(); engine.Render(1, noRoutes, new AudioBands(1, 1, 1));
Require(unmodulated.SequenceEqual(engine.Pixels), "Zero routing must bypass audio modulation.");
engine.Reset(); engine.Render(1, preset, new AudioBands(0.8f, 0.8f, 0.8f));
Require(!first.SequenceEqual(engine.Pixels), "Audio modulation must affect synthesis.");
var clock = Stopwatch.StartNew();
for (int i = 0; i < 5; i++) preview.Render(i / 30f, preset);
Console.WriteLine($"Preset and renderer checks passed. Preview average: {clock.Elapsed.TotalMilliseconds / 5:0.0} ms/frame.");
NdiChecks.Lighting(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(args.Length > 0 ? args[0] : "contracts/presets/classic-feedback.json")))!, "lighting-fixtures.json"));
if (args.Length > 1) { NdiChecks.RoundTrip(args[1]); NdiChecks.WorkerLifecycle(args[1]); }

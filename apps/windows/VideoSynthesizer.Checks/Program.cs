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
var clock = Stopwatch.StartNew();
for (int i = 0; i < 5; i++) preview.Render(i / 30f, preset);
Console.WriteLine($"Preset and renderer checks passed. Preview average: {clock.Elapsed.TotalMilliseconds / 5:0.0} ms/frame.");

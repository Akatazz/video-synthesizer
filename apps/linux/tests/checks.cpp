#include "synth.hpp"
#include <fstream>
#include <iostream>
#include <set>
#include <thread>
#include <chrono>

static void require(bool value, const char* message) {
    if (!value) throw std::runtime_error(message);
}
int main(int argc, char** argv) {
    gst_init(nullptr, nullptr);
    require(argc == 2, "Provide the canonical fixture path.");
    std::ifstream input(argv[1]);
    nlohmann::json json;
    input >> json;
    auto p = loadPreset(json);
    require(savePreset(p) == savePreset(Parameters{}), "Canonical defaults differ.");
    require(savePreset(loadPreset(savePreset(p))) == savePreset(p), "Round trip changed values.");
    auto routed = p;
    routed.audioToOsc = 0.15f; routed.audioToFractal = 0.75f; routed.audioToCA = 1;
    require(savePreset(loadPreset(savePreset(routed))) == savePreset(routed), "Audio routes must survive preset save/load.");
    for (const auto& item : json.items()) {
        auto missing = json;
        missing.erase(item.key());
        bool rejected = false;
        try { loadPreset(missing); } catch (const std::exception&) { rejected = true; }
        require(rejected, "Missing key accepted.");
    }
    auto invalid = json;
    invalid["fractalDetail"] = 0;
    bool rejected = false;
    try { loadPreset(invalid); } catch (const std::exception&) { rejected = true; }
    require(rejected, "Invalid range accepted.");
    SynthEngine engine(64, 36);
    engine.render(1, p);
    auto first = engine.pixels;
    require(std::set<std::uint32_t>(first.begin(), first.end()).size() > 20, "Preview is constant.");
    engine.render(1, p);
    require(first != engine.pixels, "Feedback has no effect.");
    engine.reset(); engine.render(1, p);
    require(first == engine.pixels, "Reset did not clear history.");
    p.fractalMorph = -0.8f;
    engine.reset(); engine.render(1, p);
    require(first != engine.pixels, "Controls have no effect.");
    for (int bin : {2, 10, 64}) {
        AudioAnalysis analysis;
        AudioBands bands;
        for (int sample = 0; sample < 512*40; ++sample)
            bands = analysis.push(0.8f * std::sin(6.28318530718f * bin * sample / 512), 32000);
        std::array<float, 3> values {bands.bass, bands.mid, bands.high};
        int expected = bin == 2 ? 0 : bin == 10 ? 1 : 2;
        require(values[expected] > 0.75f && values[expected] < 0.85f, "FFT tone amplitude or band is wrong.");
        for (int i = 0; i < 3; ++i) if (i != expected) require(values[i] < 0.01f, "FFT band leakage.");
    }
    AudioAnalysis dc;
    AudioBands dcBands;
    for (int sample = 0; sample < 1024; ++sample) dcBands = dc.push(0.5f, 44100);
    require(dcBands.bass == 0 && dcBands.mid == 0 && dcBands.high == 0, "DC must not modulate synthesis.");
    Parameters noRoutes;
    noRoutes.audioToOsc = noRoutes.audioToFractal = noRoutes.audioToCA = 0;
    engine.reset(); engine.render(1, noRoutes);
    auto bypass = engine.pixels;
    engine.reset(); engine.render(1, noRoutes, {1, 1, 1});
    require(bypass == engine.pixels, "Zero routing must bypass audio.");
    engine.reset(); engine.render(1, Parameters{}, {0.8f, 0.8f, 0.8f});
    require(first != engine.pixels, "Audio must change rendered output.");
    MicrophoneInput capture;
    capture.start(true); // audiotestsrc, never the microphone.
    AudioBands measured;
    for (int packet = 0; packet < 200 && measured.mid < 0.65f; ++packet) {
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
        measured = capture.poll();
    }
    require(measured.mid > 0.65f && measured.bass < 0.05f && measured.high < 0.05f, "GStreamer capture path did not deliver the test tone.");
    capture.stop();
    require(!capture.active() && capture.poll().mid == 0, "Stopping audio must clear levels.");
    std::cout << "Linux preset and renderer checks passed.\n";
}

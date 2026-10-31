#include "synth.hpp"
#include <fstream>
#include <iostream>
#include <set>

static void require(bool value, const char* message) {
    if (!value) throw std::runtime_error(message);
}
int main(int argc, char** argv) {
    require(argc == 2, "Provide the canonical fixture path.");
    std::ifstream input(argv[1]);
    nlohmann::json json;
    input >> json;
    auto p = loadPreset(json);
    require(savePreset(p) == savePreset(Parameters{}), "Canonical defaults differ.");
    require(savePreset(loadPreset(savePreset(p))) == savePreset(p), "Round trip changed values.");
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
    std::cout << "Linux preset and renderer checks passed.\n";
}

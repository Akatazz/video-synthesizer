#pragma once
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <stdexcept>
#include <vector>
#include <nlohmann/json.hpp>

struct Parameters {
    float oscFreq = 2, oscSpeed = 0.5f, fractalZoom = 1.5f, fractalDetail = 10;
    float fractalMorph = 0.2f, caFeed = 0.045f, caKill = 0.06f, feedbackMix = 0.95f, colorShift = 0;
};

struct Field {
    const char* key;
    const char* label;
    float Parameters::*member;
    float min, max;
};
inline constexpr std::array<Field, 9> fields {{
    {"oscFreq", "Oscillator frequency", &Parameters::oscFreq, 0, 30},
    {"oscSpeed", "Oscillator speed", &Parameters::oscSpeed, 0, 5},
    {"fractalZoom", "Fractal zoom", &Parameters::fractalZoom, 0.1f, 4},
    {"fractalDetail", "Fractal detail", &Parameters::fractalDetail, 1, 30},
    {"fractalMorph", "Fractal morph", &Parameters::fractalMorph, -1, 1},
    {"caFeed", "Cell feed", &Parameters::caFeed, 0, 0.1f},
    {"caKill", "Cell kill", &Parameters::caKill, 0, 0.1f},
    {"feedbackMix", "Feedback mix", &Parameters::feedbackMix, 0, 1},
    {"colorShift", "Color shift", &Parameters::colorShift, 0, 1}
}};

inline void validate(const Parameters& p) {
    for (const auto& f : fields) {
        float value = p.*f.member;
        if (!std::isfinite(value) || value < f.min || value > f.max)
            throw std::runtime_error(std::string(f.label) + " is outside the supported range.");
    }
}

inline Parameters loadPreset(const nlohmann::json& json) {
    if (!json.at("schemaVersion").is_number_integer() || json.at("schemaVersion") != 1)
        throw std::runtime_error("Unsupported preset version.");
    Parameters p;
    for (const auto& f : fields) {
        if (!json.at(f.key).is_number()) throw std::runtime_error("Preset values must be numbers.");
        p.*f.member = json.at(f.key).get<float>();
    }
    validate(p);
    return p;
}

inline nlohmann::json savePreset(const Parameters& p) {
    validate(p);
    nlohmann::json json = {{"schemaVersion", 1}};
    for (const auto& f : fields) json[f.key] = p.*f.member;
    return json;
}

class SynthEngine {
    int width, height;
    std::vector<float> history, next;

    float sample(float u, float v, int channel) const {
        float x = std::clamp(u * width - 0.5f, 0.0f, float(width - 1));
        float y = std::clamp(v * height - 0.5f, 0.0f, float(height - 1));
        int x0 = int(x), y0 = int(y), x1 = std::min(x0 + 1, width - 1), y1 = std::min(y0 + 1, height - 1);
        float a = x - x0, b = y - y0;
        float top = std::lerp(history[(y0 * width + x0) * 3 + channel], history[(y0 * width + x1) * 3 + channel], a);
        float bottom = std::lerp(history[(y1 * width + x0) * 3 + channel], history[(y1 * width + x1) * 3 + channel], a);
        return std::lerp(top, bottom, b);
    }

public:
    std::vector<std::uint32_t> pixels;
    SynthEngine(int w, int h) : width(w), height(h) {
        if (w < 1 || h < 1 || w > 1920 || h > 1080) throw std::runtime_error("Invalid preview dimensions.");
        history.resize(w * h * 3);
        next.resize(history.size());
        pixels.resize(w * h);
    }
    void reset() {
        std::fill(history.begin(), history.end(), 0);
        std::fill(next.begin(), next.end(), 0);
        std::fill(pixels.begin(), pixels.end(), 0);
    }
    void render(float time, const Parameters& p) {
        validate(p);
        if (!std::isfinite(time)) throw std::runtime_error("Invalid simulation time.");
        for (int y = 0; y < height; ++y) for (int x = 0; x < width; ++x) {
            float u = (x + 0.5f) / width, v = (y + 0.5f) / height;
            float wx = std::sin(v * p.oscFreq + time * p.oscSpeed) * 0.01f;
            float wy = std::cos(u * p.oscFreq + time * p.oscSpeed) * 0.01f;
            float zx = (u + wx - 0.5f) * p.fractalZoom * 5, zy = (v + wy - 0.5f) * p.fractalZoom * 5;
            float iterations = 0;
            for (int i = 0; i < 30 && i < p.fractalDetail; ++i) {
                float nx = zx * zx - zy * zy + p.fractalMorph;
                zy = 2 * zx * zy + 0.156f + std::sin(time * 0.1f) * 0.1f;
                zx = nx;
                if (zx * zx + zy * zy > 4) break;
                iterations++;
            }
            float smooth = iterations - std::log2(std::max(1.0f, std::log2(std::max(zx * zx + zy * zy, 1e-10f))));
            float value = smooth / p.fractalDetail, reactionRed = 0;
            int pixel = y * width + x;
            std::uint32_t packed = 0xff000000;
            for (int channel = 0; channel < 3; ++channel) {
                float laplacian = -sample(u, v, channel);
                for (int dy = -1; dy <= 1; ++dy) for (int dx = -1; dx <= 1; ++dx) {
                    if (dx == 0 && dy == 0) continue;
                    float weight = dx == 0 || dy == 0 ? 0.2f : 0.05f;
                    laplacian += sample(u + float(dx) / width, v + float(dy) / height, channel) * weight;
                }
                float last = sample(u + wx * 0.5f, v + wy * 0.5f, channel);
                float reaction = last + laplacian - last * last * last + p.caFeed * (1 - last) - p.caKill * last;
                if (channel == 0) reactionRed = reaction;
                float phase = channel == 0 ? 0 : channel == 1 ? 0.33f : 0.67f;
                float palette = 0.5f + 0.5f * std::cos(6.28318f * (reactionRed + p.colorShift + phase));
                float fractal = channel == 0 ? value : channel == 1 ? value * 0.5f : std::sin(value * 3.14f) * 0.5f + 0.5f;
                float composite = std::lerp(palette * reaction, fractal, 0.05f);
                float result = std::clamp(std::lerp(fractal, composite, p.feedbackMix), 0.0f, 1.0f);
                next[pixel * 3 + channel] = result;
                packed |= std::uint32_t(std::round(result * 255)) << (16 - channel * 8);
            }
            pixels[pixel] = packed;
        }
        history.swap(next);
    }
};

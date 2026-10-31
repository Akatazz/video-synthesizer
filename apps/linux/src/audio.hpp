#pragma once
#include <gst/app/gstappsink.h>
#include <gst/fft/gstfftf32.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>
#include <numeric>
#include <stdexcept>
#include <string>

struct AudioBands { float bass = 0, mid = 0, high = 0; };

class AudioAnalysis {
    static constexpr int size = 512;
    GstFFTF32* fft = gst_fft_f32_new(size, FALSE);
    std::array<float, size> samples {}, windowed {};
    std::array<GstFFTF32Complex, size/2+1> spectrum {};
    int count = 0;
    AudioBands levels;
public:
    AudioAnalysis() { if (!fft) throw std::runtime_error("Could not allocate FFT."); }
    ~AudioAnalysis() { gst_fft_f32_free(fft); }
    AudioAnalysis(const AudioAnalysis&) = delete;
    AudioAnalysis& operator=(const AudioAnalysis&) = delete;
    AudioBands push(float sample, int sampleRate) {
        if (sampleRate < 8000 || sampleRate > 192000) throw std::runtime_error("Unsupported audio sample rate.");
        samples[count++] = std::isfinite(sample) ? std::clamp(sample, -1.0f, 1.0f) : 0;
        if (count != size) return levels;
        count = 0;
        float mean = std::accumulate(samples.begin(), samples.end(), 0.0f) / size;
        for (int i = 0; i < size; ++i)
            windowed[i] = (samples[i] - mean) * (0.5f - 0.5f * std::cos(6.28318530718f * i / size));
        gst_fft_f32_fft(fft, windowed.data(), spectrum.data());
        std::array<float, 3> power {};
        for (int bin = 1; bin <= size/2; ++bin) {
            float hz = float(bin) * sampleRate / size;
            if (hz < 20) continue;
            int band = hz < 250 ? 0 : hz < 2000 ? 1 : 2;
            auto value = spectrum[bin];
            power[band] += (value.r*value.r + value.i*value.i) * 16 / (size * size * 1.5f);
        }
        levels = {levels.bass*0.8f + std::min(1.0f, std::sqrt(power[0]))*0.2f,
                  levels.mid*0.8f + std::min(1.0f, std::sqrt(power[1]))*0.2f,
                  levels.high*0.8f + std::min(1.0f, std::sqrt(power[2]))*0.2f};
        return levels;
    }
};

/// GStreamer owns the capture thread. GTK polls a bounded appsink queue.
class MicrophoneInput {
    GstElement* pipeline = nullptr;
    GstAppSink* sink = nullptr;
    std::unique_ptr<AudioAnalysis> analysis;
    AudioBands levels;
public:
    ~MicrophoneInput() { stop(); }
    bool active() const { return pipeline != nullptr; }
    void stop() {
        if (pipeline) gst_element_set_state(pipeline, GST_STATE_NULL);
        if (sink) gst_object_unref(sink);
        if (pipeline) gst_object_unref(pipeline);
        sink = nullptr; pipeline = nullptr; analysis.reset(); levels = {};
    }
    void start(bool testTone = false) {
        stop();
        GError* error = nullptr;
        const char* source = testTone ? "audiotestsrc is-live=true wave=sine freq=625 volume=0.8" : "autoaudiosrc";
        std::string description = std::string(source) + " ! audioconvert ! audioresample ! "
            "audio/x-raw,format=F32LE,channels=1,rate=48000,layout=interleaved ! "
            "appsink name=audio sync=false max-buffers=8 drop=true";
        pipeline = gst_parse_launch(description.c_str(), &error);
        if (error) {
            std::string message = error->message;
            g_error_free(error); stop(); throw std::runtime_error(message);
        }
        if (!pipeline) throw std::runtime_error("Could not create audio capture pipeline.");
        sink = GST_APP_SINK(gst_bin_get_by_name(GST_BIN(pipeline), "audio"));
        if (!sink) { stop(); throw std::runtime_error("Could not create audio analysis sink."); }
        analysis = std::make_unique<AudioAnalysis>();
        if (gst_element_set_state(pipeline, GST_STATE_PLAYING) == GST_STATE_CHANGE_FAILURE) {
            stop(); throw std::runtime_error("Audio input could not start. Check the audio device and installed GStreamer plugins.");
        }
    }
    AudioBands poll() {
        if (!pipeline) return {};
        auto* bus = gst_element_get_bus(pipeline);
        auto* message = gst_bus_pop_filtered(bus, GstMessageType(GST_MESSAGE_ERROR | GST_MESSAGE_EOS));
        gst_object_unref(bus);
        if (message) {
            std::string text = "Audio capture ended.";
            if (GST_MESSAGE_TYPE(message) == GST_MESSAGE_ERROR) {
                GError* error = nullptr;
                gst_message_parse_error(message, &error, nullptr);
                text = error ? error->message : "Audio capture failed.";
                g_clear_error(&error);
            }
            gst_message_unref(message); stop(); throw std::runtime_error(text);
        }
        for (int packet = 0; packet < 8; ++packet) {
            GstSample* sample = gst_app_sink_try_pull_sample(sink, 0);
            if (!sample) break;
            GstBuffer* buffer = gst_sample_get_buffer(sample);
            GstMapInfo map {};
            if (gst_buffer_map(buffer, &map, GST_MAP_READ)) {
                for (std::size_t offset = 0; offset + sizeof(float) <= map.size; offset += sizeof(float)) {
                    float value;
                    std::memcpy(&value, map.data + offset, sizeof(value));
                    levels = analysis->push(value, 48000);
                }
                gst_buffer_unmap(buffer, &map);
            }
            gst_sample_unref(sample);
        }
        return levels;
    }
};

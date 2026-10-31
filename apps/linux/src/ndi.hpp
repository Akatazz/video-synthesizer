#pragma once
#include <dlfcn.h>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdlib>
#include <filesystem>
#include <mutex>
#include <optional>
#include <thread>
#include "synth.hpp"

struct NdiSettings {
    int width = 512, height = 512, fps = 30, channel = 0, alpha = 0;
    float cutoff = 0.02f, gamma = 1;
    void validate() const {
        if (width < 2 || width > 1920 || width % 2 || height < 1 || height > 1080 ||
            (fps != 15 && fps != 25 && fps != 30) || !std::isfinite(cutoff) || cutoff < 0 || cutoff > 0.25f ||
            !std::isfinite(gamma) || gamma < 0.5f || gamma > 2.5f || channel < 0 || channel > 4 || alpha < 0 || alpha > 2)
            throw std::runtime_error("Invalid NDI output settings.");
    }
};

inline std::vector<std::uint32_t> lightingOutput(const std::vector<std::uint32_t>& source, int width, int height, const NdiSettings& p) {
    p.validate();
    if (width < 1 || height < 1 || width > 4096 || height > 4096 || source.size() != std::size_t(width*height))
        throw std::runtime_error("Invalid NDI source image.");
    std::vector<std::uint32_t> output(p.width*p.height);
    for (int y = 0; y < p.height; ++y) {
        float sy = std::clamp((y+0.5f)*height/p.height-0.5f, 0.0f, float(height-1));
        int y0 = int(sy), y1 = std::min(y0+1, height-1);
        for (int x = 0; x < p.width; ++x) {
            float sx = std::clamp((x+0.5f)*width/p.width-0.5f, 0.0f, float(width-1));
            int x0 = int(sx), x1 = std::min(x0+1, width-1);
            auto read = [&](int shift) {
                auto component = [&](int px, int py) { return float((source[py*width+px] >> shift) & 255); };
                float top = std::lerp(component(x0,y0), component(x1,y0), sx-x0);
                float bottom = std::lerp(component(x0,y1), component(x1,y1), sx-x0);
                return std::lerp(top,bottom,sy-y0)/255;
            };
            float r = read(16), g = read(8), b = read(0);
            if (r*0.2126f+g*0.7152f+b*0.0722f < p.cutoff) r = g = b = 0;
            r = std::pow(r,p.gamma); g = std::pow(g,p.gamma); b = std::pow(b,p.gamma);
            if (p.channel) {
                float mono = p.channel == 1 ? r*0.2126f+g*0.7152f+b*0.0722f : p.channel == 2 ? r : p.channel == 3 ? g : b;
                r = g = b = mono;
            }
            float luma = r*0.2126f+g*0.7152f+b*0.0722f;
            float alpha = p.alpha == 0 ? 1 : p.alpha == 1 ? luma : 1-luma;
            auto encode = [](float value) { return std::uint32_t(std::round(std::clamp(value,0.0f,1.0f)*255)); };
            output[y*p.width+x] = (encode(alpha)<<24) | (encode(r)<<16) | (encode(g)<<8) | encode(b);
        }
    }
    return output;
}

// Documented NDI C ABI subset. Runtime binaries are supplied separately.
struct NdiVideoFrame {
    int width, height, fourCC, frameRateN, frameRateD;
    float aspect;
    int format;
    std::int64_t timecode;
    unsigned char* data;
    int stride;
    const char* metadata;
    std::int64_t timestamp;
};
struct NdiCreate { const char* name; const char* groups; bool clockVideo, clockAudio; };
struct NdiTally { bool program, preview; };
static_assert(sizeof(NdiVideoFrame) == 72 && offsetof(NdiVideoFrame,data) == 40, "NDI requires the 64-bit video ABI.");

class NdiRuntime {
    void* library;
    explicit NdiRuntime(void* handle) : library(handle) {
        try {
            create = symbol<void*(*)(const NdiCreate*)>("NDIlib_send_create");
            destroy = symbol<void(*)(void*)>("NDIlib_send_destroy");
            send = symbol<void(*)(void*,const NdiVideoFrame*)>("NDIlib_send_send_video_v2");
            connections = symbol<int(*)(void*,std::uint32_t)>("NDIlib_send_get_no_connections");
            tally = symbol<bool(*)(void*,NdiTally*,std::uint32_t)>("NDIlib_send_get_tally");
            if (!symbol<bool(*)()>("NDIlib_initialize")()) throw std::runtime_error("NDI initialization failed.");
        } catch (...) { dlclose(library); throw; }
    }
public:
    void* (*create)(const NdiCreate*);
    void (*destroy)(void*);
    void (*send)(void*,const NdiVideoFrame*);
    int (*connections)(void*,std::uint32_t);
    bool (*tally)(void*,NdiTally*,std::uint32_t);
    template<class T> T symbol(const char* name) {
        void* address = dlsym(library,name);
        if (!address) throw std::runtime_error(std::string("Missing NDI runtime symbol: ")+name);
        return reinterpret_cast<T>(address);
    }
    static std::shared_ptr<NdiRuntime> load(const std::string& path = {}) {
        static std::mutex gate;
        static std::shared_ptr<NdiRuntime> instance;
        std::lock_guard lock(gate);
        if (instance) return instance;
        std::string configured = path;
        if (configured.empty()) if (const char* env = std::getenv("VIDEO_SYNTH_NDI_RUNTIME")) configured = env;
        if (!configured.empty() && !std::filesystem::path(configured).is_absolute())
            throw std::runtime_error("Use an absolute NDI runtime library path.");
        void* handle = nullptr;
        if (!configured.empty()) handle = dlopen(configured.c_str(),RTLD_NOW | RTLD_LOCAL);
        else for (const char* name : {"libndi.so.6", "libndi.so.5", "libndi.so"}) {
            handle = dlopen(name,RTLD_NOW | RTLD_LOCAL);
            if (handle) break;
        }
        if (!handle) throw std::runtime_error("NDI runtime unavailable. Install the official runtime or set VIDEO_SYNTH_NDI_RUNTIME to its absolute library path.");
        // The runtime stays initialized for process lifetime; stopping one sender
        // cannot invalidate another application's window or source.
        return instance = std::shared_ptr<NdiRuntime>(new NdiRuntime(handle));
    }
};

class NdiSender {
    std::shared_ptr<NdiRuntime> runtime;
    void* sender;
public:
    NdiSender(std::shared_ptr<NdiRuntime> api, const std::string& name) : runtime(std::move(api)) {
        if (name.empty() || name.size() > 120 || name.find('\0') != std::string::npos) throw std::runtime_error("Invalid NDI source name.");
        NdiCreate create {name.c_str(),nullptr,false,false};
        sender = runtime->create(&create);
        if (!sender) throw std::runtime_error("Could not create NDI sender.");
    }
    ~NdiSender() { runtime->destroy(sender); }
    NdiSender(const NdiSender&) = delete;
    NdiSender& operator=(const NdiSender&) = delete;
    void* handle() const { return sender; }
    void send(const std::vector<std::uint32_t>& pixels, int width, int height, int fps) {
        if (width < 2 || width > 1920 || width % 2 || height < 1 || height > 1080 || pixels.size() != std::size_t(width*height) || fps < 1 || fps > 60)
            throw std::runtime_error("Invalid NDI frame.");
        NdiVideoFrame frame {width,height,0x41524742,fps,1,float(width)/height,1,INT64_MAX,
            reinterpret_cast<unsigned char*>(const_cast<std::uint32_t*>(pixels.data())),width*4,nullptr,0};
        runtime->send(sender,&frame); // Synchronous: pixel storage can be reused on return.
    }
    std::string telemetry() {
        NdiTally tally {};
        runtime->tally(sender,&tally,0);
        return std::to_string(runtime->connections(sender,0))+" receiver(s) · "+(tally.program ? "PROGRAM" : tally.preview ? "PREVIEW" : "No tally");
    }
};

class NdiOutput {
    struct Frame { std::vector<std::uint32_t> pixels; int width, height; NdiSettings settings; };
    std::atomic<bool> running {false};
    std::mutex gate;
    std::condition_variable ready;
    std::optional<Frame> pending;
    std::thread worker;
    std::string message = "NDI off";
    std::chrono::steady_clock::time_point previous {};
public:
    ~NdiOutput() { stop(); }
    bool active() const { return running; }
    std::string status() { std::lock_guard lock(gate); return message; }
    void start(const std::string& name, const std::string& path) {
        stop();
        std::unique_ptr<NdiSender> sender;
        try { sender = std::make_unique<NdiSender>(NdiRuntime::load(path),name); }
        catch (const std::exception& error) { std::lock_guard lock(gate); message = std::string("NDI unavailable: ")+error.what(); throw; }
        running = true;
        previous = {};
        message = "NDI sending · waiting for receivers";
        worker = std::thread([this,sender=std::move(sender)] {
            try {
                while (running) {
                    std::unique_lock lock(gate);
                    ready.wait(lock,[this] { return !running || pending.has_value(); });
                    if (!running) break;
                    Frame frame = std::move(*pending); pending.reset();
                    lock.unlock();
                    auto pixels = lightingOutput(frame.pixels,frame.width,frame.height,frame.settings);
                    sender->send(pixels,frame.settings.width,frame.settings.height,frame.settings.fps);
                    std::string status = "NDI sending · "+std::to_string(frame.settings.width)+" × "+std::to_string(frame.settings.height)+" · "+sender->telemetry();
                    lock.lock(); message = std::move(status);
                }
            } catch (const std::exception& error) {
                std::lock_guard lock(gate); message = std::string("NDI error: ")+error.what();
            }
            running = false;
        });
    }
    void submit(const std::vector<std::uint32_t>& pixels, int width, int height, const NdiSettings& settings) {
        if (!running) return;
        std::lock_guard lock(gate);
        auto now = std::chrono::steady_clock::now();
        if (pending || std::chrono::duration<double>(now-previous).count() < 1.0/settings.fps) return;
        pending = Frame {pixels,width,height,settings}; previous = now; ready.notify_one();
    }
    void stop() {
        running = false; ready.notify_all();
        if (worker.joinable()) worker.join();
        std::lock_guard lock(gate); pending.reset(); message = "NDI off";
    }
};

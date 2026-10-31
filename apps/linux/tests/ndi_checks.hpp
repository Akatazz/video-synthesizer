#pragma once
#include "ndi.hpp"

inline void checkLighting() {
    std::vector<std::uint32_t> image {0xffff0000,0xff00ff00,0xff0000ff,0xff0a0a0a};
    NdiSettings settings; settings.width = settings.height = 2; settings.cutoff = 0;
    if (lightingOutput(image,2,2,settings) != image) throw std::runtime_error("Lighting identity changed channels or orientation.");
    settings.cutoff = 0.05f;
    if (lightingOutput(image,2,2,settings)[3] != 0xff000000) throw std::runtime_error("Black cutoff failed.");
    settings.cutoff = 0; settings.channel = 2; settings.alpha = 1;
    auto red = lightingOutput(image,2,2,settings);
    if (red[0] != 0xffffffff || red[1] != 0) throw std::runtime_error("Red routing or alpha failed.");
    settings.alpha = 2;
    auto inverted = lightingOutput(image,2,2,settings);
    if (inverted[0] != 0x00ffffff || inverted[1] != 0xff000000) throw std::runtime_error("Inverted alpha failed.");
}

inline void checkNdiRoundTrip(const std::string& path) {
    auto runtime = NdiRuntime::load(path);
    NdiSender sender(runtime,"LZX-V Native ABI Test");
    struct Source { const char* name; const char* address; };
    struct ReceiveCreate { Source source; int colorFormat, bandwidth; bool allowFields; const char* name; };
    auto* source = runtime->symbol<const Source*(*)(void*)>("NDIlib_send_get_source_name")(sender.handle());
    ReceiveCreate create {*source,0,100,false,nullptr};
    void* receiver = runtime->symbol<void*(*)(const ReceiveCreate*)>("NDIlib_recv_create_v3")(&create);
    if (!receiver) throw std::runtime_error("NDI test receiver could not be created.");
    auto destroy = runtime->symbol<void(*)(void*)>("NDIlib_recv_destroy");
    try {
        auto capture = runtime->symbol<int(*)(void*,NdiVideoFrame*,void*,void*,std::uint32_t)>("NDIlib_recv_capture_v2");
        auto free = runtime->symbol<void(*)(void*,NdiVideoFrame*)>("NDIlib_recv_free_video_v2");
        std::vector<std::uint32_t> pixels(64*36);
        for (int y = 0; y < 36; ++y) for (int x = 0; x < 64; ++x) pixels[y*64+x] = y < 18 ? 0xffff0000 : 0xff0000ff;
        auto deadline = std::chrono::steady_clock::now()+std::chrono::seconds(8);
        bool received = false;
        while (std::chrono::steady_clock::now() < deadline && !received) {
            sender.send(pixels,64,36,30);
            NdiVideoFrame frame {};
            if (capture(receiver,&frame,nullptr,nullptr,30) != 1) continue;
            bool valid = frame.width == 64 && frame.height == 36;
            if (valid) {
                const auto* top = frame.data+frame.stride*9+32*4;
                const auto* bottom = frame.data+frame.stride*27+32*4;
                valid = top[2] > 180 && top[0] < 80 && bottom[0] > 180 && bottom[2] < 80;
            }
            free(receiver,&frame);
            if (!valid) throw std::runtime_error("NDI round trip changed dimensions, BGRA channels or orientation.");
            received = true;
        }
        if (!received) throw std::runtime_error("No NDI frame received before timeout.");
    } catch (...) { destroy(receiver); throw; }
    destroy(receiver);
}

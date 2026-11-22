#include "NDIInterop.h"
#include <TargetConditionals.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#if TARGET_OS_OSX
#include <dlfcn.h>
#include <pthread.h>
#include <stdbool.h>
#include <time.h>

typedef struct {
    int width, height, fourcc, rate_n, rate_d;
    float aspect;
    int format;
    int64_t timecode;
    uint8_t* data;
    int stride;
    const char* metadata;
    int64_t timestamp;
} VideoFrame;
typedef struct { const char* name; const char* groups; bool video_clock, audio_clock; } SendCreate;
typedef struct { bool program, preview; } Tally;
_Static_assert(sizeof(VideoFrame) == 72 && offsetof(VideoFrame,data) == 40, "Incorrect NDI 64-bit video ABI");
struct VSNdiSender { void* instance; };
static void* library;
static void* (*create_sender)(const SendCreate*);
static void (*destroy_sender)(void*);
static void (*send_video)(void*, const VideoFrame*);
static int (*connections)(void*, uint32_t);
static bool (*read_tally)(void*, Tally*, uint32_t);
static pthread_mutex_t loader_lock = PTHREAD_MUTEX_INITIALIZER;

static int load_runtime(const char* path, char* error, size_t capacity) {
    pthread_mutex_lock(&loader_lock);
    if (library) { pthread_mutex_unlock(&loader_lock); return 1; }
    const char* configured = path && path[0] ? path : getenv("VIDEO_SYNTH_NDI_RUNTIME");
    void* candidate = NULL;
    if (configured && configured[0]) {
        if (configured[0] != '/') { snprintf(error,capacity,"Use an absolute NDI runtime library path."); pthread_mutex_unlock(&loader_lock); return 0; }
        candidate = dlopen(configured,RTLD_NOW | RTLD_LOCAL);
    } else {
        const char* paths[] = {"/usr/local/lib/libndi.dylib", "/Library/NDI SDK for Apple/lib/macOS/libndi.dylib", "libndi.dylib"};
        for (int i = 0; i < 3 && !candidate; ++i) candidate = dlopen(paths[i],RTLD_NOW | RTLD_LOCAL);
    }
    if (!candidate) {
        snprintf(error,capacity,"NDI runtime unavailable. Install the official runtime or choose its library. %s",dlerror());
        pthread_mutex_unlock(&loader_lock); return 0;
    }
    create_sender = dlsym(candidate,"NDIlib_send_create");
    destroy_sender = dlsym(candidate,"NDIlib_send_destroy");
    send_video = dlsym(candidate,"NDIlib_send_send_video_v2");
    connections = dlsym(candidate,"NDIlib_send_get_no_connections");
    read_tally = dlsym(candidate,"NDIlib_send_get_tally");
    bool (*initialize)(void) = dlsym(candidate,"NDIlib_initialize");
    if (!create_sender || !destroy_sender || !send_video || !connections || !read_tally || !initialize || !initialize()) {
        snprintf(error,capacity,"NDI runtime is incompatible or could not initialize.");
        dlclose(candidate); pthread_mutex_unlock(&loader_lock); return 0;
    }
    // Process lifetime: closing one window must not invalidate another sender.
    library = candidate;
    pthread_mutex_unlock(&loader_lock); return 1;
}

VSNdiSender* vs_ndi_open(const char* path, const char* name, char* error, size_t capacity) {
    if (!name || !name[0] || strlen(name) > 120) { snprintf(error,capacity,"Invalid NDI source name."); return NULL; }
    if (!load_runtime(path,error,capacity)) return NULL;
    SendCreate settings = {name,NULL,false,false};
    void* instance = create_sender(&settings);
    if (!instance) { snprintf(error,capacity,"Could not create NDI sender."); return NULL; }
    VSNdiSender* sender = malloc(sizeof(VSNdiSender));
    if (!sender) { destroy_sender(instance); snprintf(error,capacity,"Could not allocate NDI sender."); return NULL; }
    sender->instance = instance;
    return sender;
}
void vs_ndi_close(VSNdiSender* sender) { if (sender) { destroy_sender(sender->instance); free(sender); } }
void vs_ndi_send(VSNdiSender* sender, const uint8_t* bgra, int width, int height, int fps, int* receivers, int* program, int* preview) {
    if (!sender || !bgra || width < 2 || width > 1920 || width % 2 || height < 1 || height > 1080 || fps < 1 || fps > 60) return;
    VideoFrame frame = {width,height,0x41524742,fps,1,(float)width/height,1,INT64_MAX,(uint8_t*)bgra,width*4,NULL,0};
    send_video(sender->instance,&frame); // Synchronous buffer lifetime.
    Tally tally = {false,false};
    read_tally(sender->instance,&tally,0);
    if (receivers) *receivers = connections(sender->instance,0);
    if (program) *program = tally.program;
    if (preview) *preview = tally.preview;
}

int vs_ndi_test_roundtrip(const char* path, char* error, size_t capacity) {
    VSNdiSender* sender = vs_ndi_open(path,"LZX-V Native ABI Test",error,capacity);
    if (!sender) return 0;
    typedef struct { const char* name; const char* address; } Source;
    typedef struct { Source source; int color_format, bandwidth; bool fields; const char* name; } ReceiveCreate;
    const Source* (*get_source)(void*) = dlsym(library,"NDIlib_send_get_source_name");
    void* (*create_receive)(const ReceiveCreate*) = dlsym(library,"NDIlib_recv_create_v3");
    int (*capture)(void*,VideoFrame*,void*,void*,uint32_t) = dlsym(library,"NDIlib_recv_capture_v2");
    void (*free_video)(void*,VideoFrame*) = dlsym(library,"NDIlib_recv_free_video_v2");
    void (*destroy_receive)(void*) = dlsym(library,"NDIlib_recv_destroy");
    if (!get_source || !create_receive || !capture || !free_video || !destroy_receive) {
        snprintf(error,capacity,"Missing NDI receiver test functions."); vs_ndi_close(sender); return 0;
    }
    ReceiveCreate create = {*get_source(sender->instance),0,100,false,NULL};
    void* receiver = create_receive(&create);
    if (!receiver) { snprintf(error,capacity,"Could not create NDI test receiver."); vs_ndi_close(sender); return 0; }
    uint8_t pixels[64*36*4] = {0};
    for (int y = 0; y < 36; ++y) for (int x = 0; x < 64; ++x) {
        int i = (y*64+x)*4; pixels[i+(y<18 ? 2 : 0)] = 255; pixels[i+3] = 255;
    }
    int received = 0;
    for (int attempt = 0; attempt < 260 && !received; ++attempt) {
        vs_ndi_send(sender,pixels,64,36,30,NULL,NULL,NULL);
        VideoFrame frame = {0};
        if (capture(receiver,&frame,NULL,NULL,30) != 1) continue;
        int valid = frame.width == 64 && frame.height == 36;
        if (valid) {
            const uint8_t* top = frame.data+frame.stride*9+32*4;
            const uint8_t* bottom = frame.data+frame.stride*27+32*4;
            valid = top[2] > 180 && top[0] < 80 && bottom[0] > 180 && bottom[2] < 80;
        }
        free_video(receiver,&frame);
        if (!valid) { snprintf(error,capacity,"NDI round trip changed dimensions, channels or orientation."); break; }
        received = 1;
    }
    if (!received && !error[0]) snprintf(error,capacity,"No NDI frame received before timeout.");
    destroy_receive(receiver); vs_ndi_close(sender); return received;
}
#else
// Mobile requires the separately supplied, licensed platform SDK. No desktop
// dylib is loaded on iOS, and the app does not offer an unavailable sender UI.
struct VSNdiSender { int unused; };
VSNdiSender* vs_ndi_open(const char* path, const char* name, char* error, size_t capacity) {
    (void)path; (void)name; snprintf(error,capacity,"Mobile NDI SDK integration is not configured."); return NULL;
}
void vs_ndi_close(VSNdiSender* sender) { (void)sender; }
void vs_ndi_send(VSNdiSender* sender, const uint8_t* data, int width, int height, int fps, int* receivers, int* program, int* preview) {
    (void)sender; (void)data; (void)width; (void)height; (void)fps; (void)receivers; (void)program; (void)preview;
}
int vs_ndi_test_roundtrip(const char* path, char* error, size_t capacity) { (void)path; snprintf(error,capacity,"Requires macOS NDI runtime."); return 0; }
#endif

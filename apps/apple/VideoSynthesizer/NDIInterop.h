#pragma once
#include <stdint.h>
#include <stddef.h>

// A small application-owned bridge to the documented NDI C ABI. The NDI runtime
// is installed separately; no vendor implementation is compiled into the app.
typedef struct VSNdiSender VSNdiSender;
VSNdiSender* vs_ndi_open(const char* runtime_path, const char* name, char* error, size_t capacity);
void vs_ndi_close(VSNdiSender* sender);
void vs_ndi_send(VSNdiSender* sender, const uint8_t* bgra, int width, int height, int fps,
                 int* receivers, int* program, int* preview);
int vs_ndi_test_roundtrip(const char* runtime_path, char* error, size_t capacity);

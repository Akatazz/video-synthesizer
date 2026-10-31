# Linux native application

C++20 / GTK 4.10+ / Cairo, built independently with CMake. On a recent Ubuntu or Debian:

```sh
sudo apt-get install build-essential cmake pkg-config libgtk-4-dev nlohmann-json3-dev libgstreamer-plugins-base1.0-dev gstreamer1.0-plugins-good
cmake -S apps/linux -B apps/linux/build -DCMAKE_BUILD_TYPE=Release
cmake --build apps/linux/build --parallel
ctest --test-dir apps/linux/build --output-on-failure
apps/linux/build/video-synthesizer
```

The CPU preview runs at 480×270 with oscillator, Julia, feedback, color controls,
pause/reset, and native preset file dialogs. `--smoke-test` renders three frames
and exits for a headless CI launch check (`xvfb-run`). There is no Node.js runtime.

**Enable microphone** starts GStreamer's `autoaudiosrc`, converts the default
audio input to mono float samples, and analyzes bass/mid/high bands. The native
controls route them to synthesis parameters and preserve routing in presets.
Capture continues across focus/minimize changes until disabled or the window
closes. A working audio source
plugin (usually PulseAudio/PipeWire compatibility) is required; missing devices
or plugins appear as UI errors. The current F32LE pipeline targets little-endian
Linux systems (including x64 and Arm64).

GTK 4 and GStreamer shared libraries/plugins are required on the destination
system. DSP tests include a synthetic GStreamer input; CI does not open a real
microphone. See the [audio contract](../../contracts/AUDIO.md). GPU rendering,
camera input, PNG export, and distribution packages remain to
implement. Linux CI checks the contract, renderer, and a real GTK window launch;
file dialog interaction and desktop integration still need manual testing.

The scrollable rack includes native NDI output controls and lighting calibration.
Install a compatible NDI runtime and Avahi, or enter the absolute runtime path,
then choose a source name, output size and frame rate. Sending uses a bounded
C++ worker and the runtime's C API. Receiver count and tally are displayed below
the preview. See the [NDI output guide](../../contracts/NDI.md).

Rendering uses a monotonic GLib timer, independent of the window's frame clock.
The application-level checks `--background-smoke-test` and
`--background-ndi-smoke-test /absolute/path/libndi.so.6` hide/unmap the real GTK
window and verify that synthesis advances and output continues. They never open
the microphone. CI runs these checks with Xvfb; closing the window removes the
render timer and releases capture/output resources.

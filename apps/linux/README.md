# Linux native application

C++20 / GTK 4 / Cairo, built independently with CMake. On Ubuntu or Debian:

```sh
sudo apt-get install build-essential cmake pkg-config libgtk-4-dev nlohmann-json3-dev
cmake -S apps/linux -B apps/linux/build -DCMAKE_BUILD_TYPE=Release
cmake --build apps/linux/build --parallel
ctest --test-dir apps/linux/build --output-on-failure
apps/linux/build/video-synthesizer
```

The CPU preview runs at 480×270 with oscillator, Julia, feedback, color controls,
pause/reset, and native preset file dialogs. `--smoke-test` renders three frames
and exits for a headless CI launch check (`xvfb-run`). There is no Node.js runtime.

GTK 4 shared libraries are required on the destination system. GPU rendering,
audio/FFT, NDI, camera input, PNG export, and distribution packages remain to
implement. Linux CI checks the contract, renderer, and a real GTK window launch;
file dialog interaction and desktop integration still need manual testing.

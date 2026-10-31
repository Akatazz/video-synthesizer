# LZX-V Modular Video Synthesizer

A modular video synthesizer with an existing **TypeScript/WebGL** browser app
and independent native applications under development.

[![CI](https://github.com/Akatazz/video-synthesizer/actions/workflows/ci.yml/badge.svg)](https://github.com/Akatazz/video-synthesizer/actions/workflows/ci.yml)
[GitHub repository](https://github.com/Akatazz/video-synthesizer)

- **Windows:** [C# / WPF application](apps/windows/README.md), with a CPU synthesis preview, presets, and PNG export.
- **macOS:** [SwiftUI / Metal Xcode target](apps/apple/README.md), with native controls, GPU feedback, and presets.
- **iOS / iPadOS:** [Separate SwiftUI / Metal app target](apps/apple/README.md).
- **Linux:** [C++ / GTK application](apps/linux/README.md), with native controls and CPU feedback rendering.
- **Android:** [Kotlin / Android SDK application](apps/android/README.md), with OpenGL ES rendering and native controls.

Native applications are early previews with native microphone analysis and audio
modulation. NDI currently belongs to the browser implementation; native feature
parity and physical-device validation remain in progress. See
[migration status](MIGRATION_STATUS.md), [system specification](SYSTEM_SPEC.md),
and the [native preset contract](contracts/README.md).

Inspired by the analog video synthesis world — LZX Industries, Eurorack video modules, and classic video feedback — this project emulates modular patching, oscillators, and signal routing entirely in the browser.

![screenshot](https://img.shields.io/badge/status-alpha-orange)

## ✨ Browser Features

| Module                      | Description                                                                                                                                      |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Ping-Pong Feedback Loop** | Dual WebGL `RenderTarget` FBO architecture. Each frame feeds back into the next through a custom GLSL pipeline.                                  |
| **Cellular Automaton**      | Gray-Scott reaction-diffusion model running on the GPU via a 3×3 Laplacian convolution matrix.                                                   |
| **Fractal Generator**       | Real-time Julia Set computation in the complex domain (`Z² + C`). The `C` parameter is exposed as a knob and audio-reactive.                     |
| **Clifford Attractor LFO**  | A strange-attractor function modulates UV displacement, creating organic vortex patterns.                                                        |
| **Audio FFT Engine**        | Microphone input is analysed via Web Audio `AnalyserNode` (512-point FFT) and split into Bass / Mid / High bands.                                |
| **CV Patching**             | Audio bands can be routed (patched) to modulate any synthesis parameter — oscillator rate, fractal morph, cell feed — just like a real Eurorack. |
| **Hardware-Style UI**       | Rotary knobs, sliders, and module sections that emulate the look and feel of real video synthesis hardware.                                      |
| **NDI® Broadcaster**        | Real-time NDI 6 output for lighting consoles, media servers, and pixel mappers. One-click broadcast with zero external tools.                    |

## 🎛️ NDI Output for Lighting Consoles & Pixel Mapping

The synthesizer includes a built-in **NDI® (Network Device Interface) broadcaster** optimized for stage lighting workflows:

- **Zero Setup** — Running `npm run dev` automatically starts the NDI bridge. The synthesizer appears as a discoverable NDI source on your LAN.
- **Console Compatible** — Works with grandMA3, Obsidian ONYX, ChamSys MagicQ, Resolume Arena, MadMapper, TouchDesigner, QLC+, and any NDI receiver.
- **Fixture-Matched Resolutions** — Presets for LED matrices (128², 256², 512²), linear battens (512×64), wide truss strips (1024×128), Full HD, and custom W×H.
- **Lighting Calibration** — Black Level Floor Cutoff (eliminates stage LED glow in blackouts), Gamma correction, Monochrome/Luma mode, and Alpha masking.
- **Live Telemetry** — See receiver count, tally status (Program/Preview), and stream health in the hardware-styled rack UI.
- **In-App Setup Guide** — Step-by-step instructions for grandMA3, Resolume, MadMapper, and TouchDesigner are built into the UI.

### Quick grandMA3 Setup

1. Patch a **Bitmap** fixture in grandMA3.
2. Set **Input Mode → NDI** and select `LZX-V Synth Output`.
3. Assign to your layout view and adjust Bitmap Dimmer.
4. In the synthesizer, select the resolution matching your LED grid and set **Black Cutoff** to ~0.05.

## 🧮 Mathematical Algorithms

- **Julia Set Fractal** — `Z(n+1) = Z(n)² + C` iterated per-pixel with smooth iteration colouring.
- **Gray-Scott Reaction-Diffusion** — Continuous cellular automaton: `∂u/∂t = Dᵤ∇²u − uv² + F(1−u)`.
- **Clifford Attractor** — `x' = sin(a·y) + c·cos(a·x)`, `y' = sin(b·x) + d·cos(b·y)`.
- **Laplacian Convolution** — 3×3 kernel for edge-aware diffusion blurring.
- **Iq Cosine Palette** — Procedural colour via `a + b·cos(2π(c·t + d))`.

## 🚀 Quick Start

For the browser application, install Node.js 22.13+ (or Node.js 24+) and npm.
Native applications use their platform toolchains; follow the linked platform
README files above.

```bash
# Clone
git clone https://github.com/Akatazz/video-synthesizer.git
cd video-synthesizer

# Install
npm ci

# Run (includes NDI bridge)
npm run dev
```

Then open `http://localhost:5173`, click **Initialize Modular Engine (Mic)**, and allow microphone access.

### Standalone NDI Bridge

If you want to run the NDI bridge separately (e.g. headless or packaged):

```bash
npm run ndi:server
```

## 📁 Project Structure

```
src/
  main.ts        — Three.js scene, ping-pong FBO, GLSL shader, render loop
  audio.ts       — Web Audio API microphone FFT → { bass, mid, high }
  ui.ts          — Hardware-style rotary knobs, synth-rack builder, NDI module UI
  ndi-engine.ts  — Browser-side NDI manager (WebSocket, GPU readback, lighting shader)
server/
  ndi-bridge.ts  — Node.js WebSocket → NDI 6 sender bridge (grandi)
vite.config.ts   — Vite dev server with embedded NDI bridge plugin
index.html       — Entry point with dark-theme UI chrome & setup guide modal
```

## 🗺️ Roadmap

- [x] NDI Output for Lighting Consoles
- [ ] Luma-to-Displacement (Vasulka Effect)
- [ ] Kaleidoscopic Mirror module
- [ ] Bit-Crusher / Stairs Quantizer
- [ ] VHS Scanlines & CRT Bloom
- [ ] Multi-layer compositing
- [ ] Preset save / load system
- [ ] Embed into Lumos Portfolio (Astro)
- [ ] Native Apple (Metal / Swift) port
- [ ] Native Android (Vulkan / Kotlin) port

## Contribution

Read [CODING_GUIDELINES.md](CODING_GUIDELINES.md). For the browser application,
run `npm ci`, `npm run lint`, `npm run format:check`, and `npm run build`.
Native build and verification commands are in each platform's README. Submit
changes in a pull request against `master`, the existing default branch.

## 📄 License

MIT

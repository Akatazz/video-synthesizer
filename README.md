# LZX-V Modular Video Synthesizer

A real-time, audio-reactive video synthesizer built with **Three.js**, **WebGL / GLSL**, and **Web Audio API**.

Inspired by the analog video synthesis world — LZX Industries, Eurorack video modules, and classic video feedback — this project emulates modular patching, oscillators, and signal routing entirely in the browser.

![screenshot](https://img.shields.io/badge/status-alpha-orange)

## ✨ Features

| Module | Description |
|--------|-------------|
| **Ping-Pong Feedback Loop** | Dual WebGL `RenderTarget` FBO architecture. Each frame feeds back into the next through a custom GLSL pipeline. |
| **Cellular Automaton** | Gray-Scott reaction-diffusion model running on the GPU via a 3×3 Laplacian convolution matrix. |
| **Fractal Generator** | Real-time Julia Set computation in the complex domain (`Z² + C`). The `C` parameter is exposed as a knob and audio-reactive. |
| **Clifford Attractor LFO** | A strange-attractor function modulates UV displacement, creating organic vortex patterns. |
| **Audio FFT Engine** | Microphone input is analysed via Web Audio `AnalyserNode` (512-point FFT) and split into Bass / Mid / High bands. |
| **CV Patching** | Audio bands can be routed (patched) to modulate any synthesis parameter — oscillator rate, fractal morph, cell feed — just like a real Eurorack. |
| **Hardware-Style UI** | Rotary knobs, sliders, and module sections that emulate the look and feel of real video synthesis hardware. |

## 🧮 Mathematical Algorithms

- **Julia Set Fractal** — `Z(n+1) = Z(n)² + C` iterated per-pixel with smooth iteration colouring.
- **Gray-Scott Reaction-Diffusion** — Continuous cellular automaton: `∂u/∂t = Dᵤ∇²u − uv² + F(1−u)`.
- **Clifford Attractor** — `x' = sin(a·y) + c·cos(a·x)`, `y' = sin(b·x) + d·cos(b·y)`.
- **Laplacian Convolution** — 3×3 kernel for edge-aware diffusion blurring.
- **Iq Cosine Palette** — Procedural colour via `a + b·cos(2π(c·t + d))`.

## 🚀 Quick Start

```bash
# Clone
git clone https://github.com/Akatazz/video-synthesizer.git
cd video-synthesizer

# Install
npm install

# Run
npm run dev
```

Then open `http://localhost:5173`, click **Initialize Modular Engine (Mic)**, and allow microphone access.

## 📁 Project Structure

```
src/
  main.ts    — Three.js scene, ping-pong FBO, GLSL shader, render loop
  audio.ts   — Web Audio API microphone FFT → { bass, mid, high }
  ui.ts      — Hardware-style rotary knobs and synth-rack builder
index.html   — Entry point with dark-theme UI chrome
```

## 🗺️ Roadmap

- [ ] Luma-to-Displacement (Vasulka Effect)
- [ ] Kaleidoscopic Mirror module
- [ ] Bit-Crusher / Stairs Quantizer
- [ ] VHS Scanlines & CRT Bloom
- [ ] Multi-layer compositing
- [ ] Preset save / load system
- [ ] Embed into Lumos Portfolio (Astro)
- [ ] Native Apple (Metal / Swift) port
- [ ] Native Android (Vulkan / Kotlin) port

## 📄 License

MIT

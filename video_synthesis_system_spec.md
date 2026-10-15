# ARCHITECTURAL SPECIFICATION: MODULAR ANALOG VIDEO SYNTHESIZER & DIGITAL GLSL EMULATION ENGINE

> **System Target**: Full-Spectrum Hybrid Video Synthesis Platform (Pure Analog Circuit Design + WebGL/GLSL Web App Emulation)  
> **Destination**: Ready for AI Coding Agent Integration (Anti Gravity / Cursor / Auto-Coder IDEs)

---

## 1. PROJECT OVERVIEW & CORE PHILOSOPHY

This project specifies the design, mathematical framework, circuit architecture, and digital GLSL implementation for a **Modular Analog Video Synthesizer** and its corresponding **Web-Native JavaScript/WebGL Emulation Engine**.

### 1.1 Core Principles
1. **Continuous Voltage & Infinite Resolution**: Video signals are treated as continuous voltage sweeps rather than discrete pixel grids. On analog displays (CRTs, vector monitors), electron beam movement is driven by voltage, yielding infinite spatial resolution limited only by beam width and bandwidth [12, 178].
2. **Fluid Dynamics ("The Plastic Image")**: Inspired by scan processing pioneers (Steve Rutt, Bill Etra, Steina & Woody Vasulka, Dan Sandin, Stephen Beck), the video raster is treated as a malleable, 3D "time/energy object" [12, 49, 165].
3. **Organic Unpredictability**: Embracing circuit drift, wideband op-amp behavior, and self-organizing video feedback loops [12].
4. **1V DC-Coupled Patchable Video Standard**: Adhering to the LZX Industries standard where video, audio, and control voltage (CV) are unified as 0.0V–1.0V DC-coupled signals operating at video frame rates [6, 141, 162, 175].

---

## 2. HARDWARE & DISCRETE ANALOG CIRCUITRY

For physical hardware builds and analog circuit simulation, the signal path remains strictly continuous (no ADCs or digital discretization).

```
                      +-------------------+
                      | Composite/Camera  |
                      +---------+---------+
                                |
                                v
                      +-------------------+
                      |  LM1881 Sync      |
                      |  Stripper IC      |
                      +----+---------+----+
                           |         |
                  H-Sync   |         | V-Sync
                  Pulses   v         v Pulses
                     +-------+     +-------+
                     | OpAmp |     | OpAmp |
                     | H-Ramp|     | V-Ramp|
                     +---+---+     +---+---+
                         |             |
                         +------+------+
                                |
                                v
               +---------------------------------+
               |  AD633 Analog Multipliers &     |
               |  Summing Amplifier Matrix       |
               +----------------+----------------+
                                |
                                v
               +---------------------------------+
               |  Vector CRT / Deflection Yoke   |
               |  or RGB Encoder                 |
               +---------------------------------+
```

### 2.1 Master Clock & Raster Generation
* **LM1881 Sync Stripper**: Strips composite sync, producing Horizontal Sync ($H_{sync}$) and Vertical Sync ($V_{sync}$) pulses [39, 55].
* **Op-Amp Ramp Integrators**: Wideband op-amps (e.g., LM6172 or AD8001) integrate sync pulses into continuous horizontal ramp ($R_H \in [0, 1]$) and vertical ramp ($R_V \in [0, 1]$) waveforms, establishing the coordinate system [12, 72, 168].

### 2.2 Analog Multipliers & Summing Matrix
* **AD633 Four-Quadrant Analog Multiplier**: Multiplies ramps by control signals for spatial scaling, depth modulation, and 3D rotation [12, 74, 168]:
  $$V_{out} = \frac{(X_1 - X_2)(Y_1 - Y_2)}{10\text{V}} + Z$$
* **Summing Amplifiers**: Active op-amp summers combine ramps with audio/LFO signals to translate position or ripple scanlines into abstract "spaghetti" patterns [12, 74, 168].
* **Luminance-to-Displacement ("Vasulka Effect")**: Offsetting $R_V$ by input video luminance ($L_y$):
  $$Y_{\text{deflect}} = R_V + k \cdot L_y$$
  creates pseudo-3D topographical relief extrusions [12, 75, 80].

---

## 3. LZX DOUBLE VISION EMULATION & MODULE LOGIC

The digital emulation targets the **LZX Double Vision** 8-module system architecture [63, 64]. All module inputs and outputs are normalized floats in $[0.0, 1.0]$.

### 3.1 Module Breakdown & Functional Logic

1. **DWO3 (Dual Window Oscillator)**: Dual wideband VCO producing sine, triangle, square, and ramp signals locked to $H/V$ sync or free-running.
2. **DSG3 (Dual Shape Generator)**: Produces geometric ramps, parabolas, and 2D spatial gradients across horizontal and vertical axes.
3. **TBC2 & Expander (Time Base Corrector)**: Frame synchronizer, genlock, and video input interface (emulated as texture sampling and frame-rate phase locking) [63, 141].
4. **Stairs (Step Generator / 3-Bit Quantizer)**: Quantizes continuous voltage into discrete steps [4, 63, 150, 151]:
   $$V_{\text{quantized}} = \frac{\lfloor V_{in} \cdot 7.0 \rceil}{7.0}$$
5. **Ribbons (3-Bit Digitizer & Bitplane Processing)**: Slices luma into 3 parallel binary bitplanes ($D_0, D_1, D_2$), allowing individual Boolean gating or bit-reordering before weighted DAC summation [4, 63, 150, 151].
6. **FKG3 (Foreground Key Generator)**: High-speed analog comparator and voltage-controlled amplifier for hard/soft keying and luminance thresholding [56, 63].
7. **ESG3 (Electronic Shape Generator)**: Circle, ellipse, and polygon vector generator with edge softness and position control [63].
8. **Castle 101 Quad Gate Logic**: Boolean combination of binary signals [11, 12, 152]:
   $$\text{OUT}_1 = A_1 \text{ OP } B_1, \quad \text{OUT}_2 = A_2 \text{ OP } B_2$$
   $$\text{OUT}_3 = A_1 \text{ OP } \text{OUT}_2, \quad \text{OUT}_4 = \text{OUT}_1 \text{ OP } A_2$$

---

## 4. GLSL SHADER LIBRARY (WEBGL / HYDRA READY)

Below is production-ready GLSL shader code for core module emulation.

```glsl
// =================================================================
// GLSL VIDEO SYNTHESIS CORE MODULE LIBRARY
// Precision: Highp Float, Normalized Standard 0.0 to 1.0
// =================================================================
precision highp float;

uniform vec2 u_resolution;
uniform float u_time;
uniform sampler2D u_videoInput;

// -----------------------------------------------------------------
// MODULE 1: RAMP & RASTER GENERATOR (LM1881 + OpAmp Integrator)
// -----------------------------------------------------------------
vec2 generateRamps(vec2 st) {
    // Normalizes screen coordinates to 0.0 - 1.0
    return st;
}

// -----------------------------------------------------------------
// MODULE 2: DWO3 DUAL WINDOW OSCILLATOR
// -----------------------------------------------------------------
float dwo3_oscillator(vec2 ramp, float freq, float angle, int waveType) {
    // Rotate ramps to support diagonal / arbitrary scan angles
    float rad = angle * 3.14159265 / 180.0;
    mat2 rot = mat2(cos(rad), -sin(rad), sin(rad), cos(rad));
    vec2 p = rot * (ramp - 0.5);
    
    float phase = p.x * freq + u_time * 2.0;
    float val = 0.0;
    
    if (waveType == 0) {
        // Sine
        val = sin(phase * 6.2831853) * 0.5 + 0.5;
    } else if (waveType == 1) {
        // Sawtooth / Ramp
        val = fract(phase);
    } else if (waveType == 2) {
        // Triangle
        val = abs(fract(phase) - 0.5) * 2.0;
    } else if (waveType == 3) {
        // Square / Gate
        val = step(0.5, fract(phase));
    }
    return val;
}

// -----------------------------------------------------------------
// MODULE 3: STAIRS / STAIRCASE QUANTIZER (Castle 000 ADC + 001 DAC)
// -----------------------------------------------------------------
float stairs_quantizer(float inVoltage, float levels) {
    return floor(inVoltage * levels) / (levels - 1.0);
}

// -----------------------------------------------------------------
// MODULE 4: FKG3 FOREGROUND KEY GENERATOR
// -----------------------------------------------------------------
float fkg3_keyer(float signal, float threshold, float softness) {
    float edge = softness * 0.5 + 0.0001;
    return smoothstep(threshold - edge, threshold + edge, signal);
}

// -----------------------------------------------------------------
// MODULE 5: VASULKA SCAN PROCESSOR (Luminance-to-Displacement)
// -----------------------------------------------------------------
vec4 vasulka_scan_displacement(vec2 uv, float displaceAmount, float lineCount) {
    // Discrete line index based on vertical ramp
    float lineIdx = floor(uv.y * lineCount) / lineCount;
    vec2 sampleUV = vec2(uv.x, lineIdx);
    
    vec4 srcColor = texture2D(u_videoInput, sampleUV);
    float luma = dot(srcColor.rgb, vec3(0.299, 0.587, 0.114));
    
    // Displace vertical coordinate by luma voltage
    vec2 displacedUV = uv;
    displacedUV.y -= luma * displaceAmount;
    
    vec4 finalColor = texture2D(u_videoInput, displacedUV);
    return finalColor;
}

// -----------------------------------------------------------------
// MAIN FRAGMENT ENTRY POINT (EXAMPLE PATCH)
// -----------------------------------------------------------------
void main() {
    vec2 st = gl_FragCoord.xy / u_resolution.xy;
    vec2 ramps = generateRamps(st);
    
    // 1. Generate primary modulation wave from DWO3
    float osc1 = dwo3_oscillator(ramps, 12.0, 45.0, 0); // Sine
    float osc2 = dwo3_oscillator(ramps, 32.0, 90.0, 2); // Triangle
    
    // 2. Modulate signal through Stairs module (8 levels / 3-bit)
    float quantizedVal = stairs_quantizer(osc1 * osc2, 8.0);
    
    // 3. Key against background using FKG3
    float keyMask = fkg3_keyer(quantizedVal, 0.4, 0.05);
    
    // 4. Combine into final video color stream
    vec3 color = mix(vec3(0.05, 0.05, 0.1), vec3(0.9, 0.3, 0.1), keyMask);
    gl_FragColor = vec4(color, 1.0);
}
```

---

## 5. JASCRIPT WEBAPP ARCHITECTURE & PATCHING GRAPH

To run natively in browsers for portfolio hosting, the system uses WebGL framebuffers and a JavaScript DAG (Directed Acyclic Graph) node engine.

```
       +-------------------------------------------------------+
       |                  JavaScript Patch Graph               |
       +-------------------------------------------------------+
       | Node A: Oscillators (DWO3) -> Output Texture A        |
       | Node B: Quantizer (Stairs)  -> Input A, Output B      |
       | Node C: Feedback Loop       -> Input B + Framebuffer  |
       +---------------------------+---------------------------+
                                   |
                                   v
       +-------------------------------------------------------+
       |               WebGL Framebuffer Pipeline              |
       |  Render Pass 1 -> Render Pass 2 -> Screen Canvas      |
       +-------------------------------------------------------+
```

### 5.1 JS Engine Specifications
* **Engine Framework**: Raw WebGL2 / Three.js / Hydra-synth integration.
* **Audio Reactivity**: Web Audio API `AnalyserNode` streaming FFT frequency & waveform voltage directly to GLSL uniforms `u_audioLow`, `u_audioMid`, `u_audioHigh`.
* **Video Inputs**: HTML5 `webcam` / `<video>` elements fed directly to `gl.texImage2D`.

---

## 6. AI AGENT TASK ROADMAP (FOR ANTI GRAVITY / CURSOR)

This roadmap outlines sequential build instructions for AI agents executing this codebase:

- [ ] **Task 1: Project Setup & WebGL Boilerplate**
  - Initialize Vite + JavaScript project.
  - Create full-screen WebGL canvas with high-DPI scaling and frame loop (`requestAnimationFrame`).

- [ ] **Task 2: Implement Core Shader Module Library**
  - Implement GLSL helper functions for `generateRamps`, `dwo3_oscillator`, `stairs_quantizer`, `fkg3_keyer`, and `castle_quad_gate`.

- [ ] **Task 3: Build WebGL Framebuffer Feedback System**
  - Implement dual ping-pong render targets (Framebuffers) to allow recursive video feedback loops (Vasulka/Rutt-Etra feedback).

- [ ] **Task 4: Build Dynamic JS Node Patching Engine**
  - Create a lightweight JS graph parser allowing modules to be dynamically chained, modulated, and re-ordered in real-time.

- [ ] **Task 5: Add Web Audio API & Media Pipe Reactivity**
  - Wire microphone / audio file analyzer to GLSL uniform inputs for full audio-visual synchronization.

- [ ] **Task 6: Portfolio UI & Controls Interface**
  - Add lightweight UI controls (Lil-GUI or custom HUD) mapping parameter knobs, faders, and patch cable routing.

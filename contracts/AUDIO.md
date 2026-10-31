# Native audio analysis and routing

Each app captures microphone samples through its platform's audio stack:
WASAPI (C# / NAudio) on Windows, AVAudioEngine and Accelerate on Apple,
AudioRecord and a Kotlin FFT on Android, and GStreamer appsink/FFT on Linux.
No Node server, browser, or shared executable is involved.

Capture starts only from **Enable microphone**. Stop, loss of foreground,
permission failure, device errors, and view teardown clear the band levels.
Permission prompts may interrupt activation; after granting access, press Enable
microphone again if the app reports that capture is off. Microphone state and
live samples are never stored in presets, written to disk, or sent over a network.
Only numeric analysis levels reach the renderer.

The analysis operates on non-overlapping 512-sample mono blocks at the actual
capture sample rate. Multichannel Apple input is averaged; the other capture
paths request mono. Non-finite input samples become zero and finite input is
clamped to [-1, 1]. Each block has its mean removed and is multiplied by the
periodic Hann window `0.5 - 0.5*cos(2*pi*i/512)` before a forward, unscaled FFT.

Ignore frequencies below 20 Hz. Bass bins have frequencies [20, 250), mid bins
[250, 2000), and high bins [2000, Nyquist]. For each band, sum the squared
complex magnitudes, multiply by `16/(512*512*1.5)`, and take the square root.
Clamp the result to [0, 1], then smooth each analysis block as
`previous*0.8 + current*0.2`. The levels approximate peak sinusoid amplitude.
Hann energy spreads across neighbouring bins, especially at band boundaries;
512 samples cannot isolate every frequency precisely.

Reference checks use 40 blocks at 32 kHz, amplitude 0.8, at FFT bins 2, 10, and
64 (125 Hz bass, 625 Hz mid, and 4 kHz high). The target band should reach
0.75–0.85 and other bands stay below 0.01. Constant/DC and non-finite input
should produce zero. Linux additionally captures a GStreamer-generated tone
through the same appsink path used by microphone capture.

Native preset version 1 adds three optional finite [0,1] values:

- `audioToOsc`, default 0.5: mid level adds up to `5 * amount` to oscillator speed.
- `audioToFractal`, default 0.5: bass adds up to `0.5 * amount` to fractal morph.
- `audioToCA`, default 0.2: high adds up to `0.05 * amount` to cell feed.

All original keys remain required. Missing new routing keys use the defaults so
older version 1 presets continue to load. Explicit nulls and out-of-range route
values are rejected. Saving includes the three routing values. Older clients
ignore the new keys and discard them on save. A route set to zero bypasses that
band. These amounts mirror the current browser audio-CV destinations, while the
native amplitude normalization differs from Web Audio's byte-spectrum scale.

CI verifies DSP and compilation without enabling microphones. A physical-device
check remains necessary for permission denial/grant, input selection, unplugging,
foreground transitions, and the visual response to live sound on every platform.

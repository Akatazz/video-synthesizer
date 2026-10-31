# Native preset contract, version 1

Native applications exchange UTF-8 JSON presets with these required keys:

- `schemaVersion`: integer `1`; reject other versions.
- `oscFreq`: finite number, 0–30.
- `oscSpeed`: finite number, 0–5.
- `fractalZoom`: finite number, 0.1–4.
- `fractalDetail`: finite number, 1–30 (fractional values are allowed, matching the shader loop).
- `fractalMorph`: finite number, −1–1.
- `caFeed`, `caKill`: finite numbers, 0–0.1.
- `feedbackMix`, `colorShift`: finite numbers, 0–1.

Unknown keys are ignored to allow future metadata. Reject missing keys, invalid
JSON, non-finite values, and out-of-range numbers before changing live state.
Load resets the feedback history. Save records parameters, not simulation state.
The browser does not yet import this format.

`presets/classic-feedback.json` is the canonical default fixture. C#, Swift,
C++, and Kotlin checks load this same file and verify round trips and rejection
behavior.

The initial native renderer ports the current browser Julia seed, oscillator
warp, 3×3 feedback convolution, palette, and compositing. It uses a top-left
image origin, normalized linear sampling clamped at the edges, separate read and
write history, and clamped RGB output. C# and C++ keep float32 history; Metal
uses float16 textures, and Android uses universally renderable RGBA8 textures.
Pixel-identical output across GPUs is not promised.

The existing browser feedback formula is **not** a faithful two-species
Gray–Scott simulation: its RGB recurrence is retained for visual continuity.
True reaction–diffusion needs separate U/V state and validation in a later change.

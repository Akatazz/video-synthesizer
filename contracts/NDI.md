# Native desktop NDI output

Windows, macOS, and Linux have independent NDI sender implementations. They use
the documented C runtime API directly; the native applications do not start the
browser's Node/WebSocket bridge. The runtime is loaded only after **Start NDI
output**. Missing or incompatible libraries produce an error while synthesis
continues to work.

## Runtime setup

Install a compatible official [NDI Runtime/SDK](https://ndi.video/for-developers/ndi-sdk/).
Runtime binaries are not included in the native application artifacts.

- **Windows:** choose `Processing.NDI.Lib.x64.dll` with **NDI runtime…**, place the
  runtime alongside the application, or use the installed `NDI_RUNTIME_DIR_V6` /
  `NDI_RUNTIME_DIR_V5` directory. The x64 runtime is the validated Windows target.
- **macOS:** use **NDI runtime…** to select `libndi.dylib`. The sender also checks
  `/usr/local/lib/libndi.dylib` and the Apple SDK's `lib/macOS` location. The Xcode
  target declares local-network use, `_ndi._tcp.` Bonjour services, and incoming /
  outgoing sandbox network capabilities. Signed sandbox distribution still needs
  validation with the chosen runtime and installation layout.
- **Linux:** install the runtime in the system library search path, or enter its
  absolute library path in the NDI controls. The runtime requires Avahi client
  libraries and a running `avahi-daemon` for discovery.
- **All desktop platforms:** `VIDEO_SYNTH_NDI_RUNTIME` can point to an absolute
  runtime library file. Runtime instances are initialized once per process;
  restart the application to change a runtime that has already loaded.

The runtime is a third-party dependency. Follow NDI's
[licensing](https://docs.ndi.video/all/developing-with-ndi/sdk/licensing) and
[distribution instructions](https://docs.ndi.video/all/developing-with-ndi/sdk/software-distribution)
when preparing a release. The UI and documentation link to [NDI](https://ndi.video).
No vendor implementation or runtime binary is checked into this repository.

## Output workflow

Choose a source name, output resolution and frame rate, then start output. Source
names, resolution and frame rate require a restart of the sender. Windows/Linux
stop output when those controls change; macOS disables them during output.
Cutoff, gamma, channel selection and alpha mode can change while sending.

Available output sizes are 128², 256², 512², 512×64, 1024×128, 1280×720 and
1920×1080. Frame rates are 15, 25 and 30. These are requested rates; slow rendering,
encoding or readback drops frames rather than building an unbounded queue.
Windows/Linux allow at most one queued frame plus a frame being processed.
macOS allows one capture/readback/send operation at a time. Pausing synthesis
continues to send the held frame. Stopping output or closing the view joins the
sender worker and releases the sender. Output is never automatically enabled.
NDI settings are session controls and are not currently stored in synth presets.

Desktop output continues when a lighting console takes focus or the synth window
is minimized/hidden. Windows uses its dispatcher timer, Linux uses a GLib timer
independent of the widget frame clock, and macOS has an offscreen Metal loop when
the view has no drawable. The last nonzero macOS render size is retained. The
offscreen loop stops when output is disabled or its renderer is torn down.
Minimizing does not stop an explicitly enabled desktop microphone. System sleep
or OS-level suspension can still interrupt processing.

All frames are top-down, straight-alpha, 8-bit BGRA and use progressive video.
The lighting pass performs these operations in order:

1. Resize with normalized bilinear sampling clamped at the source edges.
2. Calculate luma with weights R=0.2126, G=0.7152, B=0.0722. RGB below the selected
   luma cutoff becomes black.
3. Apply the selected gamma as `pow(channel, gamma)`.
4. Keep RGB, or broadcast post-gamma luma/red/green/blue as monochrome.
5. Use opaque, post-processing luma, or inverted luma as alpha; clamp to [0,1].

`lighting-fixtures.json` checks gamma, luma, alpha, blackout and bilinear resize
against the same expected pixels in C#, Swift and C++. A one-byte tolerance
allows different midpoint-rounding rules. CPU previews on Windows/Linux are
480×270; macOS readback follows the preview and is capped at 1280×720. Larger NDI
sizes upscale that source. Full-resolution synthesis and production throughput
are separate remaining requirements.

## Verification

The native check programs can use a real runtime to create a temporary sender
and receiver, transmit a 64×36 test image, and verify dimensions, BGRA channels
and vertical orientation. These tests use generated pixels, not microphone or
camera data. CI obtains runtime binaries from the project's existing pinned
`@grandi/*@2.0.2` dependency packages, outside the app build/publish directories;
it does not ship those binaries. This test-only npm use does not become a native
application dependency.

On Windows, from the repository root:

```powershell
dotnet run --project apps/windows/VideoSynthesizer.Checks -c Release -- contracts/presets/classic-feedback.json 'C:\path\to\Processing.NDI.Lib.x64.dll'
```

On Linux, after the normal native build:

```sh
apps/linux/build/synth-checks contracts/presets/classic-feedback.json /absolute/path/libndi.so.6
```

On macOS, add the absolute `libndi.dylib` path as the second argument to the
`check-presets` command shown in the Apple README.

CI also runs the real Windows/GTK application with its window hidden/unmapped and
verifies changing synthesis frames reach the NDI worker. The macOS check uses the
compiled app's Metal library, renders without a drawable, checks changing readback
pixels and paused output, then tests a window minimize/restore cycle and teardown.
It runs when a Metal device is available and explicitly reports a skip otherwise.

These checks do not establish production-resolution throughput, cross-machine
discovery, third-party console compatibility, or live tally/alpha behavior. Test
these with NDI receivers and the intended lighting console before a show.

## Mobile

iOS/iPadOS and Android do not yet have native NDI senders. They require their
platform SDK libraries and integration, including the platform's networking and
discovery setup. Desktop runtime libraries cannot be used in a mobile app.
The iOS target continues to build without a mobile NDI SDK and does not expose
an unavailable NDI sender control.

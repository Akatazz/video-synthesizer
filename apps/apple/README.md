# Native macOS and iOS applications

Open `VideoSynthesizer.xcodeproj` in Xcode. Select **VideoSynthesizerMac** to run
on macOS 13+, or **VideoSynthesizerIOS** to run on iPhone/iPad with iOS 16+.
These are separate app targets with native SwiftUI controls and a Metal compute
renderer. Each target builds a platform-specific application and shader library.
There is no JavaScript runtime or WebView in either app.

From the repository root on a Mac with Xcode and its platform SDKs installed:

```sh
xcodebuild -project apps/apple/VideoSynthesizer.xcodeproj -scheme VideoSynthesizerMac -configuration Release -destination 'generic/platform=macOS' CODE_SIGNING_ALLOWED=NO build
xcodebuild -project apps/apple/VideoSynthesizer.xcodeproj -scheme VideoSynthesizerIOS -configuration Debug -destination 'generic/platform=iOS Simulator' CODE_SIGNING_ALLOWED=NO build
clang -c apps/apple/VideoSynthesizer/NDIInterop.c -o /tmp/ndi-interop.o
swiftc -import-objc-header apps/apple/VideoSynthesizer/NDIInterop.h apps/apple/VideoSynthesizer/SynthParameters.swift apps/apple/VideoSynthesizer/AudioAnalysis.swift apps/apple/VideoSynthesizer/NdiOutput.swift apps/apple/Checks/CheckPresets.swift /tmp/ndi-interop.o -o /tmp/check-presets
/tmp/check-presets contracts/presets/classic-feedback.json
```

For physical iOS devices or distribution, select your development team under
Signing & Capabilities. Release signing and App Store packaging are not yet set
up. CI builds unsigned applications and compiles both Metal shader libraries.

Implemented: oscillator warping, Julia seed, recursive feedback convolution,
color controls, pause/reset, and validated preset import/export through native
file pickers. Preview resolution follows the view, capped at 1280×720. GPU
history resets on size changes; parameter changes preserve feedback.

**Enable microphone** requests system permission and starts an AVAudioEngine
input tap. Accelerate computes the bass/mid/high bands; the audio routing sliders
control modulation, and their amounts are saved in presets. The app declares the
microphone usage description and macOS audio-input sandbox entitlement. Capture
continues across desktop focus/minimize changes until disabled or the view closes;
iOS capture stops on scene deactivation. Audio interruption/device changes also
stop capture.
After a permission prompt interrupts activation, enable capture again if needed.
See the [audio contract](../../contracts/AUDIO.md).

macOS has a native NDI sender with source/resolution/frame-rate controls and live
lighting calibration. It uses asynchronous Metal readback and a bounded Swift
worker with a small C bridge to the installed runtime. An offscreen timer keeps
synthesis/readback running when the window has no drawable, and holds the last
image when synthesis is paused. Choose the runtime with
**NDI runtime…**; the runtime is not bundled with the app. See the
[NDI output guide](../../contracts/NDI.md). iOS NDI still needs its platform SDK
and integration. Camera/video input and PNG export remain to implement.
Build success is separate from runtime validation: check visuals, reset while
paused, file pickers, rotation, background/foreground, and device performance on
Apple hardware before distributing.

CI additionally compiles `Checks/CheckBackgroundRendering.swift` with the renderer
and runs it against the built Metal library. With a Metal device, it verifies
changing offscreen frames, paused output, minimize/restore continuity and teardown
through the native NDI runtime. A runner without a Metal device reports a skip.

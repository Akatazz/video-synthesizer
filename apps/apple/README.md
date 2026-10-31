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
swiftc apps/apple/VideoSynthesizer/SynthParameters.swift apps/apple/VideoSynthesizer/AudioAnalysis.swift apps/apple/Checks/CheckPresets.swift -o /tmp/check-presets
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
stops on scene deactivation, audio interruption/device changes, and view teardown.
After a permission prompt interrupts activation, enable capture again if needed.
See the [audio contract](../../contracts/AUDIO.md).

NDI, camera/video input, and PNG export remain to implement.
Build success is separate from runtime validation: check visuals, reset while
paused, file pickers, rotation, background/foreground, and device performance on
Apple hardware before distributing.

# Android native application

Kotlin / Android SDK / OpenGL ES 3.0. Open this directory in Android Studio,
use JDK 17, and install Android SDK 36. The Gradle wrapper pins Gradle 8.13.
From this directory:

```sh
./gradlew testDebugUnitTest assembleDebug
./gradlew connectedDebugAndroidTest
```

On Windows use `gradlew.bat`. The debug APK is at
`app/build/outputs/apk/debug/app-debug.apk`. It supports Android 8+ devices with
OpenGL ES 3.0. It contains no WebView or Node.js runtime.

The Kotlin renderer owns two RGBA8 framebuffers and native GLSL ES shaders.
It has oscillator/Julia synthesis, recursive feedback, color controls,
pause/reset, and native document-picker preset import/export. RGBA8 history
trades precision for guaranteed ES 3 render-target support. Internal resolution
is capped at 1280×720. Rotation retains parameters and pause state, and clears
history; GL resources are recreated after context loss.

**Enable microphone** requests `RECORD_AUDIO` and starts foreground AudioRecord
capture. A Kotlin FFT produces bass/mid/high levels for the native shader's
audio routing controls. Capture stops and clears levels when leaving the activity;
it never resumes automatically after a background transition. If a permission
prompt interrupts activation, tap Enable microphone again after granting access.
See the [audio contract](../../contracts/AUDIO.md).

CI checks preset conformance and synthetic FFT signals, builds a debug APK, and runs a renderer launch/reset
test on an emulator. Physical-device performance, picker interaction,
background/foreground behavior, live microphone input, NDI, Vulkan, camera input, PNG export,
release signing, and Play Store packaging remain to implement.

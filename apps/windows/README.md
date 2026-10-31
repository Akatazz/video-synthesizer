# Windows native application

C# / WPF, .NET 10, Windows 10 or later. Install the **.NET 10 SDK** (the runtime
alone cannot build), or Visual Studio with the .NET desktop development workload.

From the repository root:

```powershell
dotnet run --project apps/windows/VideoSynthesizer
dotnet run --project apps/windows/VideoSynthesizer.Checks -c Release
dotnet publish apps/windows/VideoSynthesizer -c Release -r win-x64 --self-contained true -o artifacts/windows
```

Open `VideoSynthesizer.csproj` in Visual Studio, or use the commands above. The
published `VideoSynthesizer.exe` runs without Node.js or a .NET installation.
Use `win-arm64` when publishing for Windows on Arm.

The native preview has Julia synthesis, oscillator warping, recursive feedback,
color controls, pause/reset, JSON preset import/export, and PNG snapshots. All
controls work without microphone permission. **Enable microphone** starts WASAPI
capture via NAudio and shows bass/mid/high levels. Route those bands with the
three audio modulation sliders; presets retain the routing amounts. Capture
stops when the window loses focus or closes. Input permission/device failures are
shown below the preview. See the [audio contract](../../contracts/AUDIO.md).

The CPU renderer uses a fixed
480×270 internal preview, scaled to the window. It is an initial implementation;
Direct3D rendering, full-resolution output,
installers, and signing remain on the roadmap.

Native NDI output is available in the scrollable rack: select an installed NDI
runtime, set the source name, resolution and frame rate, then start output.
Lighting cutoff/gamma, channel and alpha controls apply live. Receiver count and
program/preview tally appear beneath the preview. Sending runs on a bounded
background worker; pausing synthesis holds the output frame. See the
[NDI output guide](../../contracts/NDI.md) for runtime setup and validation limits.

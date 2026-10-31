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
controls work without microphone permission. The CPU renderer uses a fixed
480×270 internal preview, scaled to the window. It is an initial implementation;
Direct3D rendering, native audio capture, NDI output, full-resolution output,
installers, and signing remain on the roadmap.

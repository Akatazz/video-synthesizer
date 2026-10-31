# Migration and native development status

This tracks implementation against `migration_plan.md` and the user's additional
requirement for independently built native applications. The supplied plan is
preserved as a reference; the source repository is authoritative for current code.

## Repository migration

- [x] Existing GitHub repository verified: `Akatazz/video-synthesizer`.
- [x] Local source matches the imported GitHub baseline `b8b2b63`.
- [x] Keep the existing `master` default branch and work on a `codex/` feature branch.
- [x] Preserve browser source and its NDI bridge; ignore build outputs and dependencies.
- [x] Rename the original system specification to `SYSTEM_SPEC.md`.
- [x] Add overview, native entry points, installation, contribution guidance, and CI badge.
- [x] Add ESLint flat config, Prettier, npm checks, and GitHub Actions.
- [x] Add `codex.yaml` as a human-readable index, without claiming automatic agent configuration.
- [ ] Verify all new GitHub CI jobs on the feature branch.

The plan's ZIP archives and generated inventory are not present in this checkout.
The source is already imported, so importing an older archive is unnecessary.
The repository is already public and accessible; no visibility or account changes
are required. Never commit access tokens.

## Native development

- [x] Windows C#/WPF application: independent build, live synthesis, controls,
      float feedback buffers, pause/reset, validated JSON presets, and PNG export.
- [x] Windows preset and renderer checks: malformed/missing/range rejection,
      round trip, nonblank output, feedback, reset, parameter changes, dry rendering.
- [x] macOS SwiftUI/Metal application source and shared Xcode scheme.
- [x] Separate iOS/iPadOS target with UIKit-hosted Metal surface and mobile controls.
- [x] Versioned preset fixture and C#/Swift contract checks.
- [ ] Verify Apple compilation and shader compilation on the macOS CI runner.
- [ ] Verify Apple rendering, file pickers, rotation, and lifecycle on hardware.
- [ ] Linux C++/GTK application and Linux build checks.
- [ ] Android Kotlin application with native GPU rendering and Android build checks.
- [ ] Native audio capture, FFT bands, and modulation routing on each platform.
- [ ] Native NDI integration and lighting calibration on supported platforms.
- [ ] Camera/video input, patch graph, remaining synthesis modules, and parity checks.
- [ ] GPU renderer for Windows; full-resolution output and performance measurements.
- [ ] Platform installers, icons, signing, and release packaging.

Apple targets share Swift/Metal source because both use Apple's frameworks, but
produce separate platform binaries. Windows, Linux, Android, and browser clients
own their implementations. Shared contracts contain data and documented behavior.

## Validation recorded locally

- Browser baseline and migrated production builds pass.
- ESLint and Prettier checks pass.
- Windows Release build passes with warnings treated as errors.
- Windows renderer and preset checks pass; initial five-frame 480×270 sample
  averaged about 11 ms/frame on the development machine. This is a short CPU
  preview measurement, not a production performance guarantee.

The goal remains active until outstanding platform work and validation are done.

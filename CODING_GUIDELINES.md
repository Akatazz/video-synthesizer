# Contributing

Use a `codex/` feature branch and open a pull request against the repository's
current default branch (`master`). Keep build outputs and dependencies out of Git.

## Browser application

Use TypeScript in strict mode, Vite, and ES modules. Run `npm ci`, `npm run lint`,
`npm run format:check`, and `npm run build` before opening a PR. Use
`npm run format` to apply the checked-in Prettier rules. ESLint uses a flat config
(`eslint.config.js`); do not add a legacy `.eslintrc` alongside it.

New code should use explicit parameter and protocol types. Existing `any` usage
is tolerated during migration. Native NDI dependencies stay on the server side.

## Native applications

Each platform owns its UI, renderer, audio lifecycle, permissions, and packaging.
Use C#/WPF for Windows, SwiftUI/Metal and Xcode for Apple, C++/GTK for Linux,
and Kotlin/Android SDK for Android. Share documented behavior, preset data, and
test fixtures; compile each application independently. Do not require Node.js or
a browser engine in a native application.

Keep frame history in two separate buffers. Clear history on reset and size
changes, clamp final output, reject non-finite preset values, and release capture
and graphics resources when leaving a view. Expose errors through the native UI.

Document build commands and actual validation per platform. A successful build
does not prove audio, NDI, device performance, or distribution signing works.

# AGENTS.md

Instructions for **any** coding agent or automated assistant working in this
repository (Cursor, Claude Code, Codex, Copilot, Gemini CLI, etc.). Humans can
follow the same steps.

## Product overview

Aphelion is a cross-platform browser product with three code components:

| Path | Stack | Maturity |
| --- | --- | --- |
| `backend/` | .NET 10, ASP.NET Core | Scaffold + `GET /health` |
| `desktop/` | .NET 10, Avalonia UI | Full browser client |
| `mobile/` | Flutter / Dart | Scaffold + widget test |

Shared design/product notes live under `shared/` and `docs/`. Canonical
per-component commands are also in `backend/README.md`, `desktop/README.md`, and
`mobile/README.md`.

## First-time machine setup (all agents)

From the repository root, run the portable bootstrap once (or whenever toolchains
are missing):

```bash
./scripts/setup-dev.sh
```

This installs (idempotently):

- .NET SDK **10.0.302** (pinned by `backend/global.json` and `desktop/global.json`) into `~/.dotnet`
- Flutter **stable** into `~/flutter` (provides Dart ≥ 3.12 for `mobile/`)
- On Linux: `libwebkit2gtk-4.1` plus unversioned `libwebkit2gtk.so` symlinks required by the desktop WebView
- Package restore: `dotnet restore` for both `.slnx` solutions and `flutter pub get` for mobile

If toolchains are already installed and only NuGet/pub packages need refresh:

```bash
./scripts/setup-dev.sh --deps-only
```

Put `~/.dotnet` and `~/flutter/bin` on `PATH` (the setup script appends this to
`~/.bashrc` when possible).

## Lint / test / run

`Directory.Build.props` in `backend/` and `desktop/` sets `TreatWarningsAsErrors`.
Treat any compiler warning as a failure.

### Backend

```bash
dotnet build backend/Aphelion.Backend.slnx
dotnet run --project backend/src/Aphelion.Api
# Default URL from launchSettings.json: http://localhost:5270
curl -sS http://localhost:5270/health   # → {"status":"healthy"}
```

`ASPNETCORE_URLS` is overridden by `launchSettings.json` unless you pass
`--no-launch-profile`. No automated backend tests yet (`backend/tests/README.md`).

### Desktop

```bash
dotnet build desktop/Aphelion.Desktop.slnx
dotnet run --project desktop/src/Aphelion.Desktop.UI
```

GUI app. Requires a display. On Linux the native WebView needs WebKitGTK (installed
by `scripts/setup-dev.sh`). No automated desktop tests yet.

### Mobile

```bash
cd mobile
flutter analyze
flutter test
flutter run    # needs Android/iOS device or emulator
```

There is no Linux/web desktop Flutter target configured; use `flutter test` for
headless verification.

## Important gotchas (every agent)

- **Linux desktop WebView:** Avalonia loads `libwebkit2gtk` (unversioned). Without
  the package + symlinks created by `scripts/setup-dev.sh`, the app crashes when a
  tab creates a WebView.
- **Headless / no-GPU displays:** Navigation can succeed (URL and tab title update)
  while the embedded WebKit surface stays blank. Avalonia UI (new tab, chrome,
  weather widget) still renders. This is a display/GPU limitation, not an app bug.
- **`flutter pub get` churn:** May rewrite `mobile/pubspec.lock` and
  `mobile/analysis_options.yaml`. Safe to discard if unintended
  (`git checkout -- mobile/...`).
- **LibVLC sounds:** NuGet ships Windows/macOS natives only; on Linux the desktop
  app stays silent and continues normally.
- **Do not commit secrets.** Prefer environment secrets / local env files that are
  gitignored.

## Cursor Cloud notes

Cursor Cloud Agents may already have toolchains from a saved environment snapshot.
In that case only dependency refresh is needed:

```bash
./scripts/setup-dev.sh --deps-only
```

If a Cursor environment panel install script is configured separately, keep it
aligned with `--deps-only` (restore / `pub get` only). Full toolchain bootstrap
belongs in the snapshot or in `./scripts/setup-dev.sh` for cold machines.

Unset `WEBKIT_DEBUG` if logs spam `Unknown logging channel: 1`.

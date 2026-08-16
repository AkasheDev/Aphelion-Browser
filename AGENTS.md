# AGENTS.md

Aphelion is a cross-platform browser product. This repository has three code
components:

- `backend/` — optional sync API (.NET 10, ASP.NET Core minimal API).
- `desktop/` — Windows/Linux/macOS browser client (.NET 10 + Avalonia UI).
- `mobile/` — Android/iOS client (Flutter + Dart).

## Cursor Cloud specific instructions

The base VM snapshot already has the toolchains installed and on `PATH` (via
`~/.bashrc`). The startup update script only refreshes project dependencies
(`dotnet restore` for both solutions and `flutter pub get` for mobile). You
normally do not need to install anything.

### Toolchains

- .NET 10 SDK (`10.0.302`, pinned by each `global.json`) lives at `~/.dotnet`
  (`dotnet` on `PATH`). Both `.slnx` solutions require an SDK new enough to parse
  the XML solution format; .NET 10 handles it.
- Flutter stable (`~/flutter`, `flutter`/`dart` on `PATH`) provides Dart `3.13`,
  which satisfies mobile's `sdk: ^3.12.2` constraint.
- `Directory.Build.props` in `backend/` and `desktop/` sets
  `TreatWarningsAsErrors`, so builds fail on any warning — keep changes clean.

### Standard commands

Per-component build/run/test/lint commands are already documented and correct in
`backend/README.md`, `desktop/README.md`, and `mobile/README.md`. Key entry
points:

- Backend: `dotnet run --project backend/src/Aphelion.Api` → serves on
  `http://localhost:5270` (port comes from
  `backend/src/Aphelion.Api/Properties/launchSettings.json`, which overrides
  `ASPNETCORE_URLS` unless you pass `--no-launch-profile`). Smoke test:
  `curl http://localhost:5270/health` → `{"status":"healthy"}`. There are no
  automated backend tests yet (see `backend/tests/README.md`).
- Desktop: `dotnet run --project desktop/src/Aphelion.Desktop.UI` (GUI, uses
  `DISPLAY=:1`). No automated desktop tests yet.
- Mobile: `flutter analyze` (lint) and `flutter test` (one widget test). There is
  no Linux/web desktop target configured, so `flutter run` needs an
  Android/iOS device; use `flutter test` to exercise the app headlessly.

### Non-obvious desktop gotchas

- The desktop WebView uses the platform native engine. On Linux that is
  WebKitGTK, loaded by the unversioned name `libwebkit2gtk`. The snapshot has
  `libwebkit2gtk-4.1-0` plus `libwebkit2gtk.so` / `liblibwebkit2gtk.so` symlinks
  in `/usr/lib/x86_64-linux-gnu`. Without them the app hard-crashes the moment a
  tab's WebView is created.
- On the headless, software-rendered X display (`:1`, no GPU — expect a
  `libEGL ... DRI3` warning), the browser's navigation layer works fully (typing a
  URL resolves and loads it; the tab title and X11 window title update to the
  site), but WebKitGTK does not composite page pixels into the embedded native
  surface, so the page area looks blank. This is an environment/GPU limitation,
  not a code bug; it does not reproduce on a real desktop with a GPU. The
  new-tab page, tabs, address bar, bookmarks, and the live weather widget (fetched
  from Open-Meteo) all render normally via Avalonia's software renderer.
- `WEBKIT_DEBUG=1` is pre-set in the VM environment and spams
  `Unknown logging channel: 1`; `unset WEBKIT_DEBUG` for clean logs.
- LibVLC (tab interaction sounds) has no Linux native package here (the NuGet
  natives are Windows/macOS only); the app degrades gracefully to silence.

### Mobile gotcha

- `flutter pub get` may rewrite `mobile/pubspec.lock` and append analyzer
  excludes to `mobile/analysis_options.yaml`. That churn is normal Flutter
  behavior and safe to discard (`git checkout -- mobile/...`).

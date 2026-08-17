# ADR-0002: Desktop tests and engine follow-up

- Status: Accepted
- Date: 2026-08-18
- Owners: Project owner and desktop implementation

## Context

ADR-0001 required spikes for downloads, custom context menus and tab lifecycle
on Windows, Linux and macOS before those features were treated as done. It also
left a hole: `desktop/tests/` existed only as a README waiting for a test
framework ADR. The bulk-development branch now implements those surfaces, so
the findings and the test decision need to be recorded.

## Decision

**xunit** is the desktop test framework. `Microsoft.NET.Test.Sdk` and
`xunit.runner.visualstudio` are approved for `desktop/tests/`. Tests target
Domain and Application only; they do not host a native WebView.

Engine follow-up findings:

| Capability | Windows (WebView2) | macOS (WKWebView) | Linux (WPE WebKit) |
| --- | --- | --- | --- |
| Tab switch without reload | Keep the native view in the visual tree; do not dispose on `Unloaded`. | Same | Same |
| Tear-off | Reloads. A native view cannot move between windows. | Same | Same |
| Downloads | `ICoreWebView2_4` DownloadStarting, pause/resume/cancel. Custom folder via `ResultFilePath`. | No engine event. HttpClient download plus `a[download]` intercept. Pause/resume via HTTP Range. | Same as macOS |
| HTML fullscreen | `ContainsFullScreenElementChanged` | `fullscreenchange` polled through the page message queue | Same as macOS |
| Context menu | Page `contextmenu` is cancelled and raised as a chrome menu | Same | Same |
| Find / print / mute | Script (`window.find` / `window.print` / media `.muted`) | Same | Same |
| DevTools | `OpenDevToolsWindow` | Not exposed | Not exposed |
| Private profile | `IsInPrivateModeEnabled` plus a per-window `UserDataFolder` deleted on close | `NonPersistentDataStore` plus isolated folder when the host exposes one | Isolated folder when the host exposes one; otherwise clear-on-close |

If a gap is fatal for a platform, this ADR is superseded. It is not: every
listed feature has a portable fallback except DevTools, which remains Windows-only
and is labelled as such in the UI.

## Consequences

- `dotnet test desktop/Aphelion.Desktop.slnx` is the desktop unit-test entry.
- CI builds the desktop solution on Windows, Ubuntu and macOS.
- Linux still needs WPE WebKit installed at runtime; the compile is verified in CI.

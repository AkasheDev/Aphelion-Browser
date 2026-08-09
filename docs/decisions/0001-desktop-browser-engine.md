# ADR-0001: Desktop browser engine

- Status: Accepted
- Date: 2026-08-09
- Owners: Project owner and Claude (desktop implementation)

## Context

Aphelion Desktop targets Windows, Linux and macOS on .NET 10 with Avalonia UI 12.
Avalonia ships no rendering engine of its own, so the engine that renders web
content has to be chosen explicitly. This decision defines the entire
`Aphelion.Desktop.BrowserEngine` layer and is expensive to reverse once features
are built on top of it.

The project owner has prior production experience with CefSharp on .NET 4.5 and
asked for it. **CefSharp cannot be used here.** Its rendering is bound to WinForms
and WPF host controls built on Windows native window handles (HWND). Avalonia uses
its own rendering pipeline, and HWND does not exist on Linux or macOS. Using
CefSharp would mean abandoning two of the three target platforms, which contradicts
the goals in `project-goals.md`.

What the owner actually wants is CEF — embedded Chromium. CefSharp is one .NET
wrapper over it; the question is which wrapper, or whether to use platform engines
instead.

Two credible options were evaluated in August 2026.

### Option A — CefGlue (embedded Chromium)

[OutSystems/CefGlue](https://github.com/OutSystems/CefGlue), MIT licensed, is the
CEF wrapper with an Avalonia control (`AvaloniaCefBrowser`). It is conceptually the
closest thing to CefSharp: same CEF concepts, same handler model, so existing
CefSharp knowledge transfers.

Its current state is the problem:

| Fact | Value | Verified via |
| --- | --- | --- |
| Latest NuGet version | `CefGlue.Avalonia 120.6099.211` | nuget.org |
| Published | 31 March 2025 (~17 months old) | nuget.org |
| Bundled Chromium | 120 — current CEF runtime is 150 | nuget.org version string |
| Avalonia dependency | `Avalonia.ReactiveUI >= 11.0.9` | package nuspec |
| Target framework | `net8.0` only | package nuspec |
| Avalonia 12 support | Open issue since 10 April 2026, unresolved | GitHub issues |

It does not work with our stack as it stands: we are on Avalonia 12 and `net10.0`,
CefGlue targets Avalonia 11 and `net8.0`.

The Chromium version gap is the more serious issue. Chromium 120 shipped in
December 2023 and has since accumulated a long list of publicly documented,
actively exploited vulnerabilities. Shipping a *browser* on a 2.5-year-old engine
means shipping known-exploitable code to users, and the upstream project is not
currently closing that gap.

### Option B — Platform WebViews via the official Avalonia WebView

[`Avalonia.Controls.WebView`](https://github.com/AvaloniaUI/Avalonia.Controls.WebView)
is the Avalonia team's own component. It was previously a paid Accelerate product
and became open source (MIT) with Avalonia 12.

| Fact | Value | Verified via |
| --- | --- | --- |
| Latest version | `12.0.1`, published 13 May 2026 | nuget.org |
| License | MIT | repository, nuspec |
| Avalonia dependency | `>= 12.0.0` — matches our stack | nuget.org |
| Target frameworks | `net8.0`, `net10.0`, Android, Browser | nuget.org |
| Repository activity | Last push 24 July 2026 | GitHub API |
| Maintainer | Avalonia UI team | repository owner |

It wraps each platform's own engine: WebView2 (Windows), WKWebView (macOS),
WPE WebKit (Linux), plus Android and iOS backends.

Documented capabilities relevant to a browser: navigation and navigation events,
bidirectional JavaScript interop, cookie management, HTTP request interception via
`WebResourceRequested` (header injection, domain blocking), printing
(`ShowPrintUI`, `PrintToPdfStreamAsync`), and a `WebAuthenticationBroker` for OAuth.

Runtime prerequisites per platform: WebView2 runtime on Windows (preinstalled on
Windows 11, may need installing on Windows 10); WKWebView on macOS 10.15+
(preinstalled); WPE WebKit libraries on Linux (`libwpewebkit-2.0-1` on Debian and
Ubuntu 24.04+), which the user must have installed.

The trade-off is rendering divergence: three engines mean three behaviors. Linux is
the sharpest edge — WPE WebKit is not Chromium and renders noticeably differently
from Windows and macOS on complex sites. Download handling, custom context menus
and tab lifecycle are not documented as first-class features and will need
verification against each backend.

## Decision

**Use the official `Avalonia.Controls.WebView` (Option B) and abstract it behind
ports in `Aphelion.Desktop.BrowserEngine`.**

The abstraction is the substance of this decision. `Aphelion.Desktop.Application`
defines the ports it needs — navigation, page lifecycle, script execution, request
interception, cookies — and `Aphelion.Desktop.BrowserEngine` provides the adapter
that implements them over `NativeWebView`. No layer above the adapter references
`Avalonia.Controls.WebView` types.

Two reasons this matters more than the engine choice itself:

1. The engine is the component most likely to be replaced. If CefGlue catches up to
   Avalonia 12 and a current Chromium, or if WPE WebKit proves unacceptable on
   Linux, swapping the adapter must not touch business logic.
2. Mobile already uses platform WebViews (Android WebView, WKWebView). Choosing
   platform engines on desktop keeps rendering behavior closer across the two
   clients, which serves the cross-client parity goal.

## Consequences

### Positive

- Works with our current stack today: Avalonia 12, `net10.0`, MIT license,
  AGPLv3-compatible.
- Security patches for the rendering engine arrive through OS updates rather than
  being our responsibility. For a browser this is a substantial reduction in
  ongoing risk.
- Distribution stays small — no ~150-200 MB Chromium payload per platform.
- Maintained by the Avalonia team against the Avalonia version we target, so
  framework upgrades are unlikely to strand us.

### Negative and risks

- **Rendering differs across platforms.** Three engines, three behaviors. Linux
  (WPE WebKit) will diverge most. Cross-platform rendering tests are required from
  the start, not retrofitted.
- **Linux has a runtime prerequisite.** WPE WebKit libraries must be installed;
  packaging and first-run diagnostics must handle their absence with a clear
  message rather than a crash.
- **Windows 10 may need the WebView2 runtime installed.** The installer must detect
  and handle this.
- **Feature coverage is unverified in three areas** — download handling, custom
  context menus, and tab lifecycle are not documented as first-class. These must be
  spiked against all three backends before browser features are built on them. If
  a gap is found, it is recorded and this ADR is superseded if the gap is fatal.
- **Less control than embedded Chromium.** Deep customization of the rendering
  engine is not available; the platform engine is what it is.

### Follow-up work

1. Spike downloads, context menus and tab lifecycle on Windows, Linux and macOS
   before implementing browser features. Record findings.
2. Define the engine ports in `Aphelion.Desktop.Application` and the adapter in
   `Aphelion.Desktop.BrowserEngine`.
3. Add `Avalonia.Controls.WebView` to `desktop/Directory.Packages.props`.
4. Establish a cross-platform rendering test set early.
5. Document the Linux WPE WebKit and Windows 10 WebView2 prerequisites in
   `desktop/README.md`.

## Alternatives considered

**CefSharp** — rejected: Windows-only by construction, incompatible with Avalonia
and with the Linux and macOS targets in `project-goals.md`.

**CefGlue** — rejected for now, and the rejection is about maintenance state rather
than the approach. Embedded Chromium is genuinely attractive: identical rendering
on all three platforms and full control. But the package targets Avalonia 11 and
`net8.0` while we are on Avalonia 12 and `net10.0`; Avalonia 12 support has been an
open issue since April 2026; and it bundles Chromium 120 when 150 is current.
Shipping a browser on a 2.5-year-old engine with known exploited vulnerabilities is
not defensible. If CefGlue reaches Avalonia 12 with a current Chromium, this
decision should be revisited through a new ADR — the port abstraction is what keeps
that option open.

**Writing our own CEF binding** — rejected: maintaining a CEF binding is a project
in itself, larger than the browser we intend to build.

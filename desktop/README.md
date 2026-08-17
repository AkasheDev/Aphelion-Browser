# Aphelion Desktop

Windows, Linux and macOS desktop client built on .NET 10 and Avalonia UI.

## Dependency rule

- `Aphelion.Desktop.Domain` has no project dependencies.
- `Aphelion.Desktop.Application` depends only on Domain.
- `Aphelion.Desktop.Infrastructure` implements Application ports and depends on Application and Domain.
- `Aphelion.Desktop.BrowserEngine` implements the per-platform engine ports and depends on Application and Domain.
- `Aphelion.Desktop.UI` is the composition and presentation boundary; it depends on all inner layers and holds no business rules.

## Packages

Versions are managed centrally in `Directory.Packages.props`. UI packages: Avalonia
(core, Desktop, Fluent theme, Inter fonts, WebView), `CommunityToolkit.Mvvm`
and `Microsoft.Extensions.DependencyInjection`. Tests use xunit (ADR-0002).

## Runtime prerequisites

- Windows: WebView2 Runtime (preinstalled on Windows 11).
- macOS: WKWebView (macOS 10.15+).
- Linux: WPE WebKit (`libwpewebkit-2.0-1` on Debian/Ubuntu 24.04+).

## Build, test and run

```bash
dotnet build desktop/Aphelion.Desktop.slnx
dotnet test desktop/Aphelion.Desktop.slnx
dotnet run --project desktop/src/Aphelion.Desktop.UI
```

# Aphelion Desktop

Windows, Linux and macOS desktop client built on .NET 10 and Avalonia UI.

## Dependency rule

- `Aphelion.Desktop.Domain` has no project dependencies.
- `Aphelion.Desktop.Application` depends only on Domain.
- `Aphelion.Desktop.Infrastructure` implements Application ports and depends on Application and Domain.
- `Aphelion.Desktop.BrowserEngine` implements the per-platform engine ports and depends on Application and Domain.
- `Aphelion.Desktop.UI` is the composition and presentation boundary; it depends on all inner layers and holds no business rules.

## Packages

Versions are managed centrally in `Directory.Packages.props`. Approved set: Avalonia
(core, Desktop, Fluent theme, Inter fonts, Diagnostics in Debug), `CommunityToolkit.Mvvm`
and `Microsoft.Extensions.DependencyInjection`. Persistence, identity, observability and
test packages require explicit approval and an architecture decision record.

## Build and run

```bash
dotnet build desktop/Aphelion.Desktop.slnx
dotnet run --project desktop/src/Aphelion.Desktop.UI
```

# Aphelion Browser

A cross-platform web browser: one product, two native clients.

> **Status: early development.** The project is in the scaffolding stage. No browser
> features are implemented yet and there is nothing usable to install.

## Clients

| Client | Platforms | Technology |
| --- | --- | --- |
| [`desktop/`](desktop/) | Windows, Linux, macOS | .NET 10 + Avalonia UI |
| [`mobile/`](mobile/) | Android, iOS | Flutter + Dart |

The two clients share no code. What they share is behavior: feature definitions,
design tokens, data schemas and API contracts, all held in [`shared/`](shared/).
Each client implements Clean Architecture independently, in its own language.

## Repository layout

```text
desktop/    Desktop client — .NET 10, Avalonia UI
mobile/     Mobile client — Flutter, Android and iOS
shared/     Cross-client contracts, design tokens, product specifications
backend/    Optional synchronization API — .NET 10, activated only if device sync ships
docs/       Architecture notes and architecture decision records
```

## Building

Each client builds independently.

**Desktop** — requires the .NET 10 SDK:

```shell
dotnet build desktop/Aphelion.Desktop.slnx
dotnet run --project desktop/src/Aphelion.Desktop.UI
```

**Mobile** — requires the Flutter SDK:

```shell
cd mobile
flutter pub get
flutter run
```

iOS compilation and signing require macOS with Xcode.

## Contributing

Contributions are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) first — the
repository has strict architectural boundaries, and pull requests that cross them
will be turned away regardless of code quality.

## License

Licensed under the [GNU Affero General Public License v3.0](LICENSE).

You are free to use, study, modify and redistribute this software, including
commercially. In exchange, any derivative work you distribute — or make available
to users over a network — must also be released under the AGPLv3 with its complete
source code. This keeps Aphelion and everything built from it open.

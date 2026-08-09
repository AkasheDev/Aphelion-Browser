# Aphelion

Aphelion is an open-source multi-platform product designed to provide a consistent experience across mobile and desktop devices.

This repository is structured for a future-ready development workflow with:

- A Flutter and Dart mobile application for iOS and Android
- An Avalonia desktop application for Linux, macOS, and Windows
- A shared foundation for product design, architecture, and collaboration

## Vision

Aphelion aims to bring a polished, modern, and cross-platform experience to users while remaining open for community contribution and transparent development.

## Project Structure

- mobile/ - Flutter application for iOS and Android
- desktop/ - Avalonia application for Linux, macOS, and Windows
- docs/ - Product notes, architecture references, and planning documents

## Technology Stack

- Mobile: Flutter, Dart
- Desktop: Avalonia, .NET
- Development workflow: Git, GitHub, VS Code

## Getting Started

### Prerequisites

- Flutter SDK
- Dart SDK
- Android Studio or Xcode for mobile builds
- .NET SDK for desktop development

### Mobile App

```bash
cd mobile
flutter pub get
flutter run
```

### Desktop App

```bash
cd desktop
dotnet restore
dotnet run
```

## Roadmap

- Set up the shared architecture foundation
- Implement core UI and navigation for mobile and desktop
- Add cross-platform state management and API integration
- Improve testing, documentation, and release workflows

## Contributing

Contributions are welcome. Please read the contributing guide before opening issues or pull requests.

## License

This project is licensed under the PolyForm Noncommercial License 1.0.0. Commercial use is not permitted without a separate commercial agreement.

See the LICENSE file for details and the official license page: https://polyformproject.org/licenses/noncommercial/1.0.0

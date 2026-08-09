# Aphelion Mobile

Android and iOS browser client built with Flutter and Dart using Clean Architecture.

## Dependency rule

Dependencies point inward: Presentation and Infrastructure depend on Application; Application depends on Domain; Domain depends on no outer layer. Platform integrations implement Application ports.

## Commands

```shell
flutter pub get
flutter analyze
flutter test
flutter run
```

iOS compilation and signing require macOS with Xcode.

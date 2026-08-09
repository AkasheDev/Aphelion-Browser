import 'package:flutter/material.dart';

abstract final class AphelionTheme {
  static ThemeData get light => ThemeData(
    colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFF5A4FCF)),
    useMaterial3: true,
  );

  static ThemeData get dark => ThemeData(
    colorScheme: ColorScheme.fromSeed(
      seedColor: const Color(0xFF8C83FF),
      brightness: Brightness.dark,
    ),
    useMaterial3: true,
  );
}

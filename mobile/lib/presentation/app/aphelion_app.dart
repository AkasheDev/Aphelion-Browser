import 'package:flutter/material.dart';
import 'package:aphelion_mobile/presentation/theme/aphelion_theme.dart';

class AphelionApp extends StatelessWidget {
  const AphelionApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Aphelion',
      debugShowCheckedModeBanner: false,
      theme: AphelionTheme.light,
      darkTheme: AphelionTheme.dark,
      home: const Scaffold(body: Center(child: Text('Aphelion'))),
    );
  }
}

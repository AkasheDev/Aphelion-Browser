import 'package:flutter/widgets.dart';
import 'package:aphelion_mobile/presentation/app/aphelion_app.dart';

void bootstrap() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const AphelionApp());
}

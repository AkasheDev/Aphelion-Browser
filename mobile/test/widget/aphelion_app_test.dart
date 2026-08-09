import 'package:aphelion_mobile/presentation/app/aphelion_app.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('renders the Aphelion application shell', (tester) async {
    await tester.pumpWidget(const AphelionApp());

    expect(find.text('Aphelion'), findsOneWidget);
  });
}

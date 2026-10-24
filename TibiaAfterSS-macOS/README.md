# Tibia After SS — macOS (źródła)

Natywna wersja macOS w SwiftUI. Ten projekt buduje się **na Macu** (macOS 13+), nie na Windows.

## Uruchomienie na Macu

1. Zainstaluj Xcode z App Store i uruchom go raz, aby zaakceptować licencję.
2. Skopiuj folder `TibiaAfterSS-macOS` na Maca.
3. W Terminalu, w tym folderze, uruchom `swift run`.
4. Aby utworzyć aplikację do codziennego użycia, otwórz folder w Xcode i wybierz **Product → Archive**.

## Wymagane uprawnienia

Przed kalibracją i testem otwórz **Ustawienia systemowe → Prywatność i ochrona → Dostępność** i dodaj tam zbudowaną aplikację. Jest to wymagane przez macOS do wysyłania kliknięć oraz znaków do klienta Tibii.

## Bezpieczeństwo

- hasło jest zapisywane wyłącznie w macOS Keychain;
- ustawienia profilu są w `~/Library/Application Support/TibiaAfterSSMac/profile.json` i nie zawierają hasła;
- zamknięcie okna chowa aplikację do paska menu; z menu **Zakończ** można ją zakończyć.

## Stan funkcji

Projekt obejmuje profile, rotację postaci, harmonogram, kalibrację, Keychain, uruchamianie/zamykanie klienta, pasek menu i autostart przez Login Items. Dopasowywanie obrazu PNG sukcesu nie zostało tu jeszcze przeniesione: na macOS wymaga osobnego mechanizmu analizy zrzutu ekranu i powinno zostać sprawdzone na docelowym Macu.


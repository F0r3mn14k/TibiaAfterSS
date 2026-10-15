# Tibia After SS — .NET / Windows

Natywna wersja Windows prototypu, bez Pythona i pakietów pip. Hasło jest przechowywane wyłącznie jako ogólne poświadczenie `TibiaAfterSS:<e-mail>` w Menedżerze poświadczeń Windows. `settings.json` zawiera tylko ustawienia i współrzędne; podczas zapisu usuwa również nieznane pola ze starszych konfiguracji, w tym ewentualne pole hasła.

## Uruchomienie w trybie deweloperskim

```powershell
cd TibiaAfterSS.Net
dotnet run
```

## Samodzielny plik EXE

Uruchom `build-release.bat`. Wynik znajdziesz w `TibiaAfterSS.Net\publish\TibiaAfterSS.Net.exe`. Jest samodzielny (nie wymaga instalacji .NET na komputerze docelowym).

## Różnice względem prototypu Python

- nie wymaga Pythona, pip ani OpenCV;
- obsługa kliknięć i tekstu używa Windows `SendInput`;
- weryfikacja PNG jest prostym, lokalnym dopasowaniem kolorów bez OpenCV; używaj małego, nieruchomego i charakterystycznego wzorca;
- harmonogram toleruje maksymalnie dwuminutowe opóźnienie pętli aplikacji, aby nie ominąć zadania przy chwilowym zajęciu UI.

Pozostałe ograniczenia bezpieczeństwa i zasad Tibii z głównego README nadal obowiązują.

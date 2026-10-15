# Tibia After SS — prototyp Windows 10/11

Aplikacja pomocnicza wykonująca **restart oficjalnego klienta Tibii** po codziennym server save. Opcjonalnie może **spróbować zalogować postać przy użyciu zwykłych kliknięć**. Nie modyfikuje klienta, jego pamięci ani komunikacji z serwerem i nie próbuje omijać BattlEye. Automatyczne logowanie może jednak naruszać regulamin CipSoft. Korzystasz na własne ryzyko.

## Jak działa

- **10:02** — zamyka proces gry (domyślnie `Tibia.exe`).
- **10:16** — uruchamia wskazany plik EXE, wpisuje e-mail i hasło, wybiera postać oraz klika przycisk wejścia do gry.
- **Do 11:00** — może ponawiać próby co 3 minuty, ale **wyłącznie wtedy**, gdy skonfigurujesz obraz potwierdzający wejście do świata. Bez niego próbuje tylko raz. Na czas testu też wykonuje tylko jedną próbę.
- Wszystkie godziny są lokalnymi godzinami Windows. Na komputerze ustawionym na czas polski będą zgodne z godzinami server save.

**Ważne:** czas trwania server save bywa przedłużony, np. podczas konserwacji. W takim przypadku aplikacja spróbuje ponownie pod warunkiem prawidłowo ustawionego obrazu sukcesu.

## Instalacja i uruchomienie

1. Użyj komputera z Windows 10/11 i zainstalowanym Pythonem 3.11 lub nowszym. W instalatorze zaznacz „Add Python to PATH”.
2. W folderze programu otwórz terminal i wykonaj `python -m pip install -r requirements.txt`.
3. Uruchom `start.bat`, albo w terminalu `python app.py`.
4. Możesz wykonać `build_exe.bat`, aby zbudować `dist\TibiaAfterSS.exe` na swoim komputerze. Program `.exe` buduje się **na Windows**.
5. Jeśli program ma uruchamiać się sam przy zalogowaniu do Windows, naciśnij `Win+R`, wpisz `shell:startup` i dodaj do otwartego folderu skrót do zbudowanego `TibiaAfterSS.exe`.

## Konfiguracja

1. Wybierz właściwy plik EXE startujący klienta Tibii. Jeśli przy uruchomieniu otwiera się dodatkowy launcher, sprawdź, czy domyślne 25 sekund oczekiwania wystarcza.
2. Upewnij się, że pole „Nazwa procesu do zamknięcia” odpowiada rzeczywistemu procesowi gry w Menedżerze zadań Windows (domyślnie `Tibia.exe`).
3. Wpisz e-mail i hasło, następnie kliknij „Zapisz bezpiecznie”. Hasło jest przechowywane przez `keyring` w magazynie poświadczeń użytkownika Windows. Nie jest zapisywane do `settings.json`.
4. Otwórz klienta i ustaw go na ekranie logowania. Skalibruj pola e-mail, hasła i przycisk logowania: kliknij „Ustaw (5 s)” i ustaw kursor nad danym elementem przed upływem pięciu sekund.
5. Ręcznie zaloguj się do listy postaci i skalibruj jej pozycję oraz przycisk wejścia do gry.
6. Opcjonalnie przygotuj plik PNG z **niewielkim, unikalnym, nieruchomym fragmentem interfejsu widocznym tylko w świecie gry**. Wybierz go w polu „Obraz potwierdzający wejście do gry”. Dzięki temu program może odróżnić powodzenie od awarii i ponawiać próby. Wzorzec nie powinien zawierać zmiennych cyfr ani animacji.
7. Zapisz ustawienia i przetestuj. **Uwaga:** przycisk testowania zamknie działającego klienta.

## Bezpieczeństwo i ograniczenia

- **Nie podawaj nikomu swoich danych logowania.** Aplikacja nie wysyła ich do zewnętrznych serwerów, ale wprowadza je w oficjalnym oknie klienta.
- Nie obsługuje samodzielnie **uwierzytelnienia 2FA**. Jeżeli Tibia wyświetli monit o kod, musisz dokończyć logowanie ręcznie. Automatyzacja 2FA i obchodzenie zabezpieczeń nie są częścią projektu.
- Ekran Windows musi pozostać **odblokowany**, klient musi być widoczny, a rozdzielczość, skalowanie i pozycja jego okna nie mogą się zmieniać od momentu kalibracji.
- Jeżeli elementy interfejsu logowania się zmienią, trzeba ponownie skalibrować współrzędne. Skrypt nie gwarantuje poprawnego logowania na każdej wersji klienta; nie był testowany z zalogowanym kontem Tibii.
- Gdy obraz potwierdzenia ma fałszywe niedopasowanie, kolejna próba może zamknąć już zalogowaną grę. Użyj **Testu** i sprawdź rozpoznawanie wzorca przed włączeniem ponawiania.
- **Przerwanie awaryjne:** przesuń kursor do lewego górnego rogu ekranu (mechanizm PyAutoGUI FailSafe) lub zamknij program.
- Aplikacja musi działać o zaprogramowanej godzinie. Nie jest usługą działającą na ekranie blokady.
- Zakończenie procesu przy użyciu systemowego `terminate`/`kill` może przerwać inne czynności, jeśli klient nie wylogował się automatycznie. Domyślną godzinę 10:02 wybrano dlatego, że wtedy postać zazwyczaj jest już wylogowana przez server save.

## Miejsce przechowywania konfiguracji

`%LOCALAPPDATA%\TibiaAfterSS\settings.json`. Znajdziesz tam adres EXE, e-mail i punkty kliknięć, **ale bez hasła**.

## Regulamin

Przed użyciem przeczytaj aktualne [Tibia Rule 3b](https://www.tibia.com/support/?rule=3b&subtopic=tibiarules). CipSoft może karać za używanie nieoficjalnego oprogramowania do gry. Brak modyfikacji klienta nie oznacza automatycznie, że automatyczne logowanie jest akceptowane przez wydawcę.

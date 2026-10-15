@echo off
cd /d "%~dp0"
python -m pip install -r requirements.txt
if errorlevel 1 goto error
python -m PyInstaller --noconfirm --onefile --windowed --name "TibiaAfterSS" app.py
if errorlevel 1 goto error
echo.
echo Gotowe: dist\TibiaAfterSS.exe
pause
exit /b 0
:error
echo Wystapil blad. Sprawdz instalacje Python 3.11+ i srodowisko Windows.
pause
exit /b 1

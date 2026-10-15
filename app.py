"""Tibia After Server Save - local Windows desktop helper.

No client modification, memory access or anti-cheat bypass.
Uses regular UI input to restart and (optionally) attempt login.
"""
from __future__ import annotations

import ctypes
from ctypes import wintypes
from datetime import datetime, time as dtime, timedelta
import json
import os
from pathlib import Path
import queue
import re
import subprocess
import sys
import threading
import time
import tkinter as tk
from tkinter import filedialog, messagebox, ttk

import keyring
import psutil
import pyautogui

if sys.platform != 'win32':
    raise SystemExit('Aplikacja jest przeznaczona dla Windows 10/11.')

SERVICE = 'TibiaAfterSS'
BASE_DIR = Path(os.environ.get('LOCALAPPDATA', str(Path.home()))) / 'TibiaAfterSS'
CONFIG_PATH = BASE_DIR / 'settings.json'
FIELDS = [
    ('email', 'Pole e-mail'),
    ('password', 'Pole hasła'),
    ('login', 'Przycisk logowania'),
    ('character', 'Postać na liście'),
    ('enter', 'Przycisk wejścia do gry'),
]
DEFAULT = {
    'exe_path': '',
    'process_name': 'Tibia.exe',
    'account': '',
    'close_time': '10:02',
    'login_time': '10:16',
    'retry_until': '11:00',
    'retry_seconds': 180,
    'client_load_seconds': 25,
    'after_login_seconds': 8,
    'success_marker': '',
    'coords': {},
}

pyautogui.FAILSAFE = True  # Move cursor to top-left to interrupt PyAutoGUI actions.
pyautogui.PAUSE = 0.15


def load_config() -> dict:
    BASE_DIR.mkdir(parents=True, exist_ok=True)
    if not CONFIG_PATH.exists():
        return dict(DEFAULT)
    try:
        supplied = json.loads(CONFIG_PATH.read_text(encoding='utf-8'))
        return {**DEFAULT, **supplied}
    except (OSError, ValueError):
        return dict(DEFAULT)


def parse_hhmm(value: str) -> dtime:
    if not re.fullmatch(r'(?:[01]\d|2[0-3]):[0-5]\d', value):
        raise ValueError(f'Niepoprawna godzina: {value!r}; użyj HH:MM')
    h, m = map(int, value.split(':'))
    return dtime(h, m)


# Send Unicode keystrokes through Windows SendInput (no password in clipboard).
ULONG_PTR = ctypes.c_size_t
class MOUSEINPUT(ctypes.Structure):
    _fields_ = [('dx', wintypes.LONG), ('dy', wintypes.LONG), ('mouseData', wintypes.DWORD),
                ('dwFlags', wintypes.DWORD), ('time', wintypes.DWORD), ('dwExtraInfo', ULONG_PTR)]
class KEYBDINPUT(ctypes.Structure):
    _fields_ = [('wVk', wintypes.WORD), ('wScan', wintypes.WORD), ('dwFlags', wintypes.DWORD),
                ('time', wintypes.DWORD), ('dwExtraInfo', ULONG_PTR)]
class HARDWAREINPUT(ctypes.Structure):
    _fields_ = [('uMsg', wintypes.DWORD), ('wParamL', wintypes.WORD), ('wParamH', wintypes.WORD)]
class INPUTUNION(ctypes.Union):
    _fields_ = [('mi', MOUSEINPUT), ('ki', KEYBDINPUT), ('hi', HARDWAREINPUT)]
class INPUT(ctypes.Structure):
    _fields_ = [('type', wintypes.DWORD), ('u', INPUTUNION)]

_SendInput = ctypes.windll.user32.SendInput
_SendInput.argtypes = [wintypes.UINT, ctypes.POINTER(INPUT), ctypes.c_int]
_SendInput.restype = wintypes.UINT


def type_unicode(value: str) -> None:
    for character in value:
        raw = character.encode('utf-16-le')
        for j in range(0, len(raw), 2):
            codepoint = int.from_bytes(raw[j:j+2], 'little')
            inputs = (INPUT * 2)(
                INPUT(1, INPUTUNION(ki=KEYBDINPUT(0, codepoint, 0x0004, 0, 0))),
                INPUT(1, INPUTUNION(ki=KEYBDINPUT(0, codepoint, 0x0004 | 0x0002, 0, 0))),
            )
            if _SendInput(2, inputs, ctypes.sizeof(INPUT)) != 2:
                raise RuntimeError('Windows nie przyjął wpisywania znaków SendInput.')


def focus_tibia_window() -> None:
    # Activation is best-effort. Official launcher/window titles may change.
    try:
        import pygetwindow as gw
        matches = [w for w in gw.getWindowsWithTitle('Tibia')
                   if w.width >= 500 and w.height >= 300]
        if matches:
            w = matches[-1]
            if w.isMinimized:
                w.restore()
            w.activate()
            time.sleep(0.7)
    except Exception:
        pass


class App:
    def __init__(self, root: tk.Tk):
        self.root = root
        self.cfg = load_config()
        self.messages = queue.Queue()
        self.stop_event = threading.Event()
        self.running = False
        self.last_actions: set[str] = set()
        self.root.title('Tibia After SS  —  Windows')
        self.root.geometry('730x805')
        self.root.minsize(700, 710)
        self.build_ui()
        self.read_fields()
        self.root.after(200, self.process_messages)
        self.root.after(1000, self.tick)
        self.root.protocol('WM_DELETE_WINDOW', self.on_close)

    def build_ui(self):
        main = ttk.Frame(self.root, padding=14)
        main.pack(fill='both', expand=True)
        title = ttk.Label(main, text='Tibia After Server Save', font=('Segoe UI', 17, 'bold'))
        title.pack(anchor='w')
        ttk.Label(main, text='Automatyczny restart klienta po SS oraz próba zalogowania postaci.').pack(anchor='w', pady=(0, 12))

        rows = ttk.Frame(main)
        rows.pack(fill='x')
        self.vars = {}
        descriptors = [
            ('exe_path', 'Plik EXE klienta Tibii'),
            ('process_name', 'Nazwa procesu do zamknięcia'),
            ('account', 'E-mail konta'),
            ('close_time', 'Zamknij klienta o'),
            ('login_time', 'Uruchom/loguj od'),
            ('retry_until', 'Ponawiaj do'),
            ('success_marker', 'Obraz potwierdzający wejście do gry (PNG)'),
        ]
        for i, (key, label) in enumerate(descriptors):
            ttk.Label(rows, text=label).grid(row=i, column=0, sticky='w', pady=5)
            self.vars[key] = tk.StringVar()
            ttk.Entry(rows, textvariable=self.vars[key], width=52).grid(row=i, column=1, sticky='ew', padx=7, pady=5)
            if key in ('exe_path', 'success_marker'):
                ttk.Button(rows, text='Wybierz', command=lambda k=key: self.browse(k)).grid(row=i, column=2)
        rows.columnconfigure(1, weight=1)

        password_frame = ttk.Frame(main)
        password_frame.pack(fill='x', pady=(11, 6))
        ttk.Label(password_frame, text='Hasło konta').pack(side='left')
        self.password_var = tk.StringVar()
        ttk.Entry(password_frame, textvariable=self.password_var, show='●', width=35).pack(side='left', padx=11)
        ttk.Button(password_frame, text='Zapisz bezpiecznie', command=self.save_password).pack(side='left')
        self.pass_state = ttk.Label(main, text='Hasło: nie sprawdzono')
        self.pass_state.pack(anchor='w')

        ttk.Separator(main).pack(fill='x', pady=13)
        ttk.Label(main, text='Kalibracja kliknięć (stała rozdzielczość i układ okien)', font=('Segoe UI', 11, 'bold')).pack(anchor='w')
        ttk.Label(main, text='Kliknij „Ustaw”, a przez 5 sekund przesuń kursor nad dany element klienta. Nie klikaj.').pack(anchor='w', pady=(3, 8))
        capture = ttk.Frame(main)
        capture.pack(fill='x')
        self.coord_labels = {}
        for i, (key, caption) in enumerate(FIELDS):
            ttk.Label(capture, text=caption).grid(row=i, column=0, sticky='w', pady=4)
            self.coord_labels[key] = ttk.Label(capture, text='nie ustawiono', width=22)
            self.coord_labels[key].grid(row=i, column=1, padx=12)
            ttk.Button(capture, text='Ustaw (5 s)', command=lambda k=key: self.capture(k)).grid(row=i, column=2)

        ttk.Separator(main).pack(fill='x', pady=12)
        ttk.Label(main, text='Weryfikacja logowania', font=('Segoe UI', 11, 'bold')).pack(anchor='w')
        ttk.Label(main, wraplength=665, text=(
            'Zalecane: dodaj mały zrzut charakterystycznego elementu interfejsu widocznego wyłącznie '
            'po wejściu postaci do gry (np. fragment minimapy lub panelu ekwipunku). '
            'Bez obrazu program wykona tylko jedną próbę; nie wie, czy logowanie się udało.'
        )).pack(anchor='w', pady=(4, 10))

        buttons = ttk.Frame(main)
        buttons.pack(fill='x', pady=3)
        ttk.Button(buttons, text='Zapisz ustawienia', command=self.save_config).pack(side='left')
        ttk.Button(buttons, text='Test: uruchom i zaloguj', command=self.test_run).pack(side='left', padx=9)
        self.status = tk.StringVar(value='Oczekiwanie na harmonogram. Program musi pozostać uruchomiony.')
        ttk.Label(main, textvariable=self.status, wraplength=665, foreground='#23517b').pack(anchor='w', pady=(13, 5))
        self.log = tk.Text(main, height=7, state='disabled', font=('Consolas', 9))
        self.log.pack(fill='both', expand=True)
        ttk.Label(main, text='Uwaga: używanie automatyzacji może naruszać regulamin Tibii; stosujesz ją na własne ryzyko.',
                  wraplength=660).pack(anchor='w', pady=(10, 0))

    def browse(self, key):
        if key == 'exe_path':
            path = filedialog.askopenfilename(title='Wybierz plik klienta', filetypes=[('Programy Windows', '*.exe')])
        else:
            path = filedialog.askopenfilename(title='Wybierz fragment ekranu gry', filetypes=[('Obrazy PNG', '*.png')])
        if path:
            self.vars[key].set(path)

    def read_fields(self):
        for key, variable in self.vars.items():
            variable.set(str(self.cfg.get(key, '')))
        for key, _ in FIELDS:
            pt = self.cfg.get('coords', {}).get(key)
            self.coord_labels[key].configure(text=str(pt) if pt else 'nie ustawiono')
        self.update_password_indicator()

    def update_password_indicator(self):
        account = self.vars['account'].get().strip()
        try:
            exists = bool(account and keyring.get_password(SERVICE, account))
            self.pass_state.configure(text=('Hasło zapisane w magazynie poświadczeń Windows.' if exists
                                            else 'Hasło nie jest zapisane dla tego konta.'))
        except Exception:
            self.pass_state.configure(text='Nie można odczytać magazynu poświadczeń.')

    def save_password(self):
        account = self.vars['account'].get().strip()
        password = self.password_var.get()
        if not account or not password:
            messagebox.showwarning('Brak danych', 'Wpisz e-mail i hasło.')
            return
        try:
            keyring.set_password(SERVICE, account, password)
            self.password_var.set('')
            self.update_password_indicator()
            self.append_log('Hasło zapisane w magazynie Windows (nie w pliku konfiguracyjnym).')
        except Exception as ex:
            messagebox.showerror('Nie udało się zapisać hasła', str(ex))

    def capture(self, key):
        self.status.set('Za 5 sekund zostanie zapamiętana pozycja kursora. Ustaw go na elemencie gry.')
        self.root.withdraw()
        self.root.after(5000, lambda: self.finish_capture(key))

    def finish_capture(self, key):
        try:
            pt = pyautogui.position()
            self.cfg.setdefault('coords', {})[key] = [int(pt.x), int(pt.y)]
            self.coord_labels[key].configure(text=f'({pt.x}, {pt.y})')
            self.status.set(f'Zapisano pozycję: {key}.')
        finally:
            self.root.deiconify()
            self.root.lift()

    def save_config(self, silent=False):
        try:
            for key in ('close_time', 'login_time', 'retry_until'):
                parse_hhmm(self.vars[key].get().strip())
            exe = self.vars['exe_path'].get().strip()
            if exe and (not Path(exe).is_file() or Path(exe).suffix.lower() != '.exe'):
                raise ValueError('Wybierz istniejący plik EXE klienta Tibii.')
            marker = self.vars['success_marker'].get().strip()
            if marker and not Path(marker).is_file():
                raise ValueError('Nie znaleziono obrazu potwierdzenia logowania.')
            for key, variable in self.vars.items():
                self.cfg[key] = variable.get().strip()
            BASE_DIR.mkdir(parents=True, exist_ok=True)
            CONFIG_PATH.write_text(json.dumps(self.cfg, ensure_ascii=False, indent=2), encoding='utf-8')
            if not silent:
                self.append_log('Ustawienia zapisane.')
            return True
        except Exception as ex:
            messagebox.showerror('Błąd ustawień', str(ex))
            return False

    def validate_run(self):
        if not self.cfg.get('exe_path') or not Path(self.cfg['exe_path']).is_file():
            return 'Wybierz ścieżkę do EXE klienta.'
        if not self.cfg.get('account'):
            return 'Wpisz e-mail konta.'
        if any(k not in self.cfg.get('coords', {}) for k, _ in FIELDS):
            return 'Skalibruj pięć pozycji kliknięć.'
        if not keyring.get_password(SERVICE, self.cfg['account']):
            return 'Zapisz hasło konta.'
        return None

    def test_run(self):
        if not self.save_config(silent=True):
            return
        error = self.validate_run()
        if error:
            messagebox.showwarning('Brakuje konfiguracji', error)
            return
        if self.running:
            messagebox.showinfo('Trwa działanie', 'Program wykonuje już zadanie.')
            return
        if not messagebox.askyesno('Test logowania', 'Program zamknie teraz Tibię i spróbuje zalogować postać. Kontynuować?'):
            return
        self.start_worker('login', test=True)

    def append_log(self, msg):
        line = f'[{datetime.now():%H:%M:%S}] {msg}\n'
        self.log.configure(state='normal')
        self.log.insert('end', line)
        self.log.see('end')
        self.log.configure(state='disabled')
        self.status.set(msg)

    def send(self, msg):
        self.messages.put(('log', msg))

    def process_messages(self):
        while True:
            try:
                kind, value = self.messages.get_nowait()
            except queue.Empty:
                break
            if kind == 'log':
                self.append_log(value)
            elif kind == 'done':
                self.running = False
        if not self.stop_event.is_set():
            self.root.after(200, self.process_messages)

    def tick(self):
        if self.stop_event.is_set():
            return
        now = datetime.now()
        day = now.strftime('%Y-%m-%d')
        hhmm = now.strftime('%H:%M')
        if not self.running:
            # Application must be running at the scheduled time.
            if hhmm == self.vars['close_time'].get().strip():
                key = day + ':close'
                if key not in self.last_actions:
                    self.last_actions.add(key)
                    if self.save_config(silent=True):
                        self.start_worker('close')
            elif hhmm == self.vars['login_time'].get().strip():
                key = day + ':login'
                if key not in self.last_actions:
                    self.last_actions.add(key)
                    if self.save_config(silent=True):
                        self.start_worker('login')
        self.root.after(1000, self.tick)

    def start_worker(self, action, test=False):
        if self.running:
            return
        self.running = True
        threading.Thread(target=self.worker, args=(action, test), daemon=True).start()

    def worker(self, action, test):
        try:
            if action == 'close':
                self.send('Godzina server save: zamykam klienta Tibii.')
                self.close_client()
            else:
                error = self.validate_run()
                if error:
                    self.send('Nie można rozpocząć: ' + error)
                    return
                self.reconnect(test=test)
        except pyautogui.FailSafeException:
            self.send('Przerwano awaryjnie: kursor przesunięto w lewy górny róg.')
        except Exception as ex:
            self.send(f'Błąd: {type(ex).__name__}: {ex}')
        finally:
            self.messages.put(('done', None))

    def wait(self, seconds: float) -> bool:
        return not self.stop_event.wait(seconds)

    def close_client(self):
        wanted = self.cfg['process_name'].lower().strip()
        if not wanted:
            self.send('Nie ustawiono nazwy procesu — pomijam zamykanie.')
            return
        procs = []
        for p in psutil.process_iter(['pid', 'name']):
            try:
                if (p.info.get('name') or '').lower() == wanted:
                    p.terminate()
                    procs.append(p)
            except (psutil.AccessDenied, psutil.NoSuchProcess):
                pass
        if procs:
            _, alive = psutil.wait_procs(procs, timeout=6)
            for p in alive:
                try:
                    p.kill()
                except (psutil.AccessDenied, psutil.NoSuchProcess):
                    pass
            self.send(f'Zamknięto {len(procs)} proces(ów) {wanted}.')
        else:
            self.send('Klient nie działał; nie trzeba go zamykać.')

    def see_success(self):
        marker = self.cfg.get('success_marker')
        if not marker:
            return False
        if not Path(marker).is_file():
            raise ValueError('Nie istnieje obraz potwierdzający logowanie.')
        try:
            return pyautogui.locateOnScreen(marker, confidence=0.85) is not None
        except pyautogui.ImageNotFoundException:
            return False

    def reconnect(self, test=False):
        account = self.cfg['account']
        password = keyring.get_password(SERVICE, account)
        if not password:
            self.send('Brak hasła w magazynie poświadczeń Windows.')
            return
        cutoff = datetime.combine(datetime.now().date(), parse_hhmm(self.cfg['retry_until']))
        if cutoff < datetime.now() and not test:
            self.send('Minął czas zakończenia prób logowania.')
            return
        attempt = 0
        marker = bool(self.cfg.get('success_marker'))
        while not self.stop_event.is_set():
            attempt += 1
            if marker and self.see_success():
                self.send('Wykryto ekran gry; postać prawdopodobnie jest zalogowana.')
                return
            self.send(f'Próba logowania nr {attempt}.')
            self.close_client()
            subprocess.Popen([self.cfg['exe_path']], cwd=str(Path(self.cfg['exe_path']).parent))
            self.send('Uruchomiono klienta; oczekiwanie na okno logowania...')
            if not self.wait(int(self.cfg['client_load_seconds'])):
                return
            focus_tibia_window()
            coords = self.cfg['coords']
            for step, value in [('email', account), ('password', password)]:
                x, y = coords[step]
                pyautogui.click(x, y)
                pyautogui.hotkey('ctrl', 'a')
                type_unicode(value)
                if not self.wait(0.7):
                    return
            pyautogui.click(*coords['login'])
            if not self.wait(int(self.cfg['after_login_seconds'])):
                return
            pyautogui.click(*coords['character'])
            if not self.wait(0.65):
                return
            pyautogui.click(*coords['enter'])
            self.send('Wybrano postać; oczekiwanie na wejście do świata...')
            if not marker:
                self.send('Wykonano jedną próbę. Bez obrazu potwierdzenia program nie ponawia logowania.')
                return
            for _ in range(12):
                if not self.wait(3):
                    return
                if self.see_success():
                    self.send('Wykryto ekran gry — logowanie zakończone.')
                    return
            if test or datetime.now() + timedelta(seconds=int(self.cfg['retry_seconds'])) > cutoff:
                self.send('Nie potwierdzono zalogowania — zakończono próby.')
                return
            delay = int(self.cfg['retry_seconds'])
            self.send(f'Brak potwierdzenia. Ponowienie za {delay} sekund (najpóźniej {self.cfg["retry_until"]}).')
            if not self.wait(delay):
                return

    def on_close(self):
        self.stop_event.set()
        self.root.destroy()


if __name__ == '__main__':
    root = tk.Tk()
    try:
        style = ttk.Style(root)
        if 'vista' in style.theme_names():
            style.theme_use('vista')
    except Exception:
        pass
    App(root)
    root.mainloop()

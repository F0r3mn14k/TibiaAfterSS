using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace TibiaAfterSS;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(initiallyOwned: true, "Local\\TibiaAfterSS-1.1.0", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Tibia After SS 1.1.0 już działa w zasobniku systemowym.", "Aplikacja jest już uruchomiona", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class AppConfig
{
    [JsonPropertyName("exe_path")]
    public string ExePath { get; set; } = "";
    [JsonPropertyName("process_name")]
    public string ProcessName { get; set; } = "Tibia.exe";
    [JsonPropertyName("account")]
    public string Account { get; set; } = "";
    [JsonPropertyName("close_time")]
    public string CloseTime { get; set; } = "10:02";
    [JsonPropertyName("login_time")]
    public string LoginTime { get; set; } = "10:16";
    [JsonPropertyName("retry_until")]
    public string RetryUntil { get; set; } = "11:00";
    [JsonPropertyName("retry_seconds")]
    public int RetrySeconds { get; set; } = 180;
    [JsonPropertyName("client_load_seconds")]
    public int ClientLoadSeconds { get; set; } = 25;
    [JsonPropertyName("after_login_seconds")]
    public int AfterLoginSeconds { get; set; } = 8;
    [JsonPropertyName("success_marker")]
    public string SuccessMarker { get; set; } = "";
    [JsonPropertyName("coords")]
    public Dictionary<string, PointConfig> Coords { get; set; } = new();
    [JsonPropertyName("characters")]
    public List<CharacterProfile> Characters { get; set; } = new();
}

[JsonConverter(typeof(PointConfigConverter))]
internal sealed record PointConfig(int X, int Y);

internal sealed class CharacterProfile
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public PointConfig? Position { get; set; }
    public override string ToString() => Position is null ? $"{Name} — brak pozycji" : $"{Name} — ({Position.X}, {Position.Y})";
}

internal sealed class PointConfigConverter : JsonConverter<PointConfig>
{
    public override PointConfig Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray) {
            reader.Read(); var x = reader.GetInt32(); reader.Read(); var y = reader.GetInt32(); reader.Read();
            return new PointConfig(x, y);
        }
        if (reader.TokenType == JsonTokenType.StartObject) {
            using var document = JsonDocument.ParseValue(ref reader);
            return new PointConfig(document.RootElement.GetProperty("X").GetInt32(), document.RootElement.GetProperty("Y").GetInt32());
        }
        throw new JsonException("Współrzędna musi być tablicą [x, y] lub obiektem.");
    }
    public override void Write(Utf8JsonWriter writer, PointConfig value, JsonSerializerOptions options)
    {
        writer.WriteStartArray(); writer.WriteNumberValue(value.X); writer.WriteNumberValue(value.Y); writer.WriteEndArray();
    }
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TibiaAfterSS-1.1.0");
    public static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), JsonOptions) ?? new AppConfig();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A usable default is safer than failing at application start; saving will replace malformed data.
        }
        return new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(DirectoryPath);
        // AppConfig intentionally has no password property. Unknown/legacy JSON fields are discarded on save.
        File.WriteAllText(FilePath, JsonSerializer.Serialize(config, JsonOptions), new UTF8Encoding(false));
    }
}

internal static class CredentialStore
{
    private const string Service = "TibiaAfterSS";
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    private static string Target(string account) => $"{Service}:{account}";

    public static void Save(string account, string password)
    {
        var bytes = Encoding.Unicode.GetBytes(password);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential {
                Type = CredTypeGeneric, TargetName = Target(account), CredentialBlob = blob,
                CredentialBlobSize = (uint)bytes.Length, Persist = CredPersistLocalMachine, UserName = account
            };
            if (!CredWrite(ref credential, 0))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Nie można zapisać poświadczenia Windows.");
        }
        finally
        {
            Array.Clear(bytes, 0, bytes.Length);
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    public static string? Read(string account)
    {
        if (!CredRead(Target(account), CredTypeGeneric, 0, out var pointer))
            return null;
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            return credential.CredentialBlobSize == 0 ? "" : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally { CredFree(pointer); }
    }
}

internal static class StartupRegistration
{
    private const string ShortcutName = "Tibia After SS 1.1.0.lnk";
    private static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName);
    public static bool IsEnabled => File.Exists(ShortcutPath);
    public static void SetEnabled(bool enabled)
    {
        if (!enabled) { if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath); return; }
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Nie można utworzyć skrótu Windows.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try {
            dynamic shortcut = shell.CreateShortcut(ShortcutPath);
            shortcut.TargetPath = Application.ExecutablePath;
            shortcut.WorkingDirectory = AppContext.BaseDirectory;
            shortcut.Description = "Tibia After SS 1.1.0";
            shortcut.Save();
        }
        finally { if (Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
    }
}

internal static class NativeInput
{
    private const uint InputKeyboard = 1, KeyUp = 0x0002, Unicode = 0x0004;
    private const ushort Ctrl = 0x11, A = 0x41;
    // INPUT contains a union sized for MOUSEINPUT even when we send keyboard input.
    // Keeping the mouse member here makes Marshal.SizeOf<INPUT>() match Win32 on 64-bit Windows.
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mi; [FieldOffset(0)] public KeybdInput Ki; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int Dx, Dy; public uint MouseData, Flags, Time; public IntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KeybdInput { public ushort Vk, Scan; public uint Flags, Time; public IntPtr ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);

    private static void Send(params Input[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Windows nie przyjął wprowadzania znaków.");
    }
    private static Input Key(ushort key, bool up = false) => new() { Type = InputKeyboard, U = new InputUnion { Ki = new KeybdInput { Vk = key, Flags = up ? KeyUp : 0 } } };
    public static void SelectAll() => Send(Key(Ctrl), Key(A), Key(A, true), Key(Ctrl, true));
    public static void TypeUnicode(string value)
    {
        foreach (var c in value)
            Send(new Input { Type = InputKeyboard, U = new InputUnion { Ki = new KeybdInput { Scan = c, Flags = Unicode } } },
                 new Input { Type = InputKeyboard, U = new InputUnion { Ki = new KeybdInput { Scan = c, Flags = Unicode | KeyUp } } });
    }
}

internal sealed class MainForm : Form
{
    private readonly AppConfig config = SettingsStore.Load();
    private readonly Dictionary<string, Control> fields = new();
    private readonly Dictionary<string, Label> coordLabels = new();
    private readonly TextBox password = new() { UseSystemPasswordChar = true, Width = 270 };
    private readonly TextBox passwordConfirmation = new() { UseSystemPasswordChar = true, Width = 270 };
    private readonly CheckBox showPasswords = new() { Text = "Pokaż hasła", AutoSize = true };
    private readonly TextBox characterName = new() { Width = 220 };
    private readonly ListBox characterList = new() { Width = 500, Height = 100 };
    private readonly Label rotationState = new() { AutoSize = true };
    private readonly CheckBox startWithWindows = new() { Text = "Uruchamiaj aplikację automatycznie po zalogowaniu do Windows", AutoSize = true };
    private readonly NotifyIcon trayIcon = new();
    private readonly Label passwordState = new() { AutoSize = true };
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.FromArgb(35, 81, 123), MaximumSize = new Size(680, 0) };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly HashSet<string> completedActions = new();
    private CancellationTokenSource cancellation = new();
    private bool running;
    private bool exitRequested;
    private bool changingStartup;
    private static readonly (string Key, string Caption)[] CoordinateFields = [
        ("email", "Pole e-mail — ekran logowania"),
        ("password", "Pole hasła — ekran logowania"),
        ("login", "Przycisk „Zaloguj” — ekran logowania"),
        ("enter", "Przycisk „Wejdź do gry” — lista postaci")];

    public MainForm()
    {
        Text = "Tibia After SS 1.1.0 — rotacja postaci"; StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(735, 800); MinimumSize = new Size(700, 700);
        BuildUi(); ReadConfig(); RefreshProcessList(); RefreshCharacterList(); timer.Tick += (_, _) => Tick(); timer.Start();
        ConfigureTray();
        startWithWindows.Checked = StartupRegistration.IsEnabled;
        startWithWindows.CheckedChanged += (_, _) => SetStartup();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) HideToTray(); };
        FormClosing += HandleFormClosing;
    }

    private void BuildUi()
    {
        var outer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14), AutoScroll = true };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Width = 690 };
        outer.Controls.Add(panel); Controls.Add(outer);
        panel.Controls.Add(new Label { Text = "Tibia After Server Save", Font = new Font("Segoe UI", 17, FontStyle.Bold), AutoSize = true });
        panel.Controls.Add(TextLabel("Ustawienia wykonuj od góry do dołu. Najpierw wskaż klienta, potem konto i pozycje kliknięć."));
        panel.Controls.Add(BuildSettings());
        panel.Controls.Add(startWithWindows);
        panel.Controls.Add(TextLabel("Minimalizacja lub zamknięcie okna pozostawia aplikację działającą w zasobniku systemowym obok zegara."));
        panel.Controls.Add(new Label { BorderStyle = BorderStyle.Fixed3D, Height = 2, Width = 680 });
        panel.Controls.Add(TextLabel("2. Kalibracja kliknięć", true));
        panel.Controls.Add(TextLabel("Otwórz Tibię i ustaw ją w docelowym miejscu. Dla każdego wiersza kliknij „Ustaw pozycję”, a następnie w ciągu 5 sekund najedź kursorem na opisany element. Nie klikaj."));
        var coords = new TableLayoutPanel { ColumnCount = 3, RowCount = CoordinateFields.Length, AutoSize = true, Width = 650 };
        coords.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        coords.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        coords.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var row = 0; row < CoordinateFields.Length; row++) {
            var (key, caption) = CoordinateFields[row];
            coords.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            coords.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(3, 7, 12, 3) }, 0, row);
            var label = new Label { Text = "nie ustawiono", AutoSize = true, MinimumSize = new Size(140, 0), Margin = new Padding(3, 7, 12, 3) };
            coordLabels[key] = label; coords.Controls.Add(label, 1, row);
            var button = new Button { Text = "Ustaw pozycję (5 s)", AutoSize = true }; button.Click += async (_, _) => await CaptureAsync(key);
            coords.Controls.Add(button, 2, row);
        }
        panel.Controls.Add(coords);
        panel.Controls.Add(BuildCharacterRotation());
        panel.Controls.Add(new Label { BorderStyle = BorderStyle.Fixed3D, Height = 2, Width = 680 });
        panel.Controls.Add(TextLabel("3. Sprawdzenie powodzenia — opcjonalne", true));
        panel.Controls.Add(TextLabel("PNG wybierasz w sekcji pierwszej. Bez tego obrazu aplikacja wykona tylko jedną próbę logowania; z obrazem może ponawiać próby do podanej godziny."));
        var buttons = new FlowLayoutPanel { AutoSize = true };
        var save = new Button { Text = "Zapisz profil „Domyślny”", AutoSize = true }; save.Click += (_, _) => SaveConfig(false);
        var test = new Button { Text = "Test: uruchom i zaloguj", AutoSize = true }; test.Click += async (_, _) => await TestAsync();
        buttons.Controls.AddRange([save, test]); panel.Controls.Add(buttons);
        panel.Controls.Add(TextLabel("Profil „Domyślny” jest automatycznie wczytywany przy następnym uruchomieniu. Zawiera ścieżki, godziny i pozycje kliknięć, ale nigdy hasło."));
        panel.Controls.Add(status);
        log.Height = 135; log.Width = 680; panel.Controls.Add(log);
        panel.Controls.Add(TextLabel("Uwaga: automatyzacja może naruszać regulamin Tibii; używasz jej na własne ryzyko."));
    }

    private FlowLayoutPanel BuildSettings()
    {
        var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 12, 0, 6) };
        var client = NewGrid();
        AddFileRow(client, "ExePath", "1. Plik klienta Tibii (.exe)", "Wybierz");
        var processRow = client.RowCount++; client.Controls.Add(RowLabel("2. Proces do zamknięcia"), 0, processRow);
        var process = new ComboBox { Width = 350, DropDownStyle = ComboBoxStyle.DropDown }; fields["ProcessName"] = process; client.Controls.Add(process, 1, processRow);
        var refresh = new Button { Text = "Odśwież listę", AutoSize = true }; refresh.Click += (_, _) => RefreshProcessList(); client.Controls.Add(refresh, 2, processRow);
        AddTextRow(client, "Account", "3. E-mail konta Tibii");
        var passRow = client.RowCount++; client.Controls.Add(RowLabel("4. Hasło konta"), 0, passRow); client.Controls.Add(password, 1, passRow);
        var savePass = new Button { Text = "Zapisz bezpiecznie", AutoSize = true }; savePass.Click += (_, _) => SavePassword(); client.Controls.Add(savePass, 2, passRow);
        var confirmationRow = client.RowCount++; client.Controls.Add(RowLabel("5. Powtórz hasło"), 0, confirmationRow); client.Controls.Add(passwordConfirmation, 1, confirmationRow);
        showPasswords.CheckedChanged += (_, _) => { password.UseSystemPasswordChar = !showPasswords.Checked; passwordConfirmation.UseSystemPasswordChar = !showPasswords.Checked; };
        client.Controls.Add(showPasswords, 2, confirmationRow);
        passwordState.Text = "Hasło: nie sprawdzono"; client.Controls.Add(passwordState, 1, client.RowCount++);
        root.Controls.Add(Section("1. Klient i konto", "Wybierz plik uruchamiający grę, proces gry z listy oraz zapisz hasło w Windows Credential Manager. Hasło trzeba wpisać dwa razy.", client));

        var schedule = NewGrid();
        AddTextRow(schedule, "CloseTime", "Zamknij klienta o (HH:mm)");
        AddTextRow(schedule, "LoginTime", "Zacznij logowanie o (HH:mm)");
        AddTextRow(schedule, "RetryUntil", "Nie ponawiaj po (HH:mm)");
        root.Controls.Add(Section("Harmonogram", "Godziny są lokalnymi godzinami Windows. Domyślnie: 10:02, 10:16 i 11:00.", schedule));

        var marker = NewGrid(); AddFileRow(marker, "SuccessMarker", "Obraz sukcesu (PNG, opcjonalnie)", "Wybierz PNG");
        root.Controls.Add(Section("Potwierdzenie wejścia do gry", "Wskaż mały nieruchomy fragment interfejsu widoczny wyłącznie w świecie gry. Bez pliku PNG aplikacja nie ponawia logowania.", marker));
        return root;
    }

    private GroupBox BuildCharacterRotation()
    {
        var group = new GroupBox { Text = "Rotacja postaci", Width = 680, Height = 270, Padding = new Padding(9, 20, 9, 9) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "Dodaj postacie w kolejności rotacji. Każdej przypisz pozycję na liście postaci w Tibii. Przy każdym kolejnym dniu aplikacja wybierze następną pozycję.", AutoSize = true, MaximumSize = new Size(640, 0) }, 0, 0);
        characterList.Dock = DockStyle.Fill;
        layout.Controls.Add(characterList, 0, 1);
        var controls = new FlowLayoutPanel { AutoSize = true };
        var add = new Button { Text = "Dodaj postać", AutoSize = true }; add.Click += (_, _) => AddCharacter();
        var capture = new Button { Text = "Ustaw pozycję wybranej (5 s)", AutoSize = true }; capture.Click += async (_, _) => await CaptureCharacterAsync();
        var remove = new Button { Text = "Usuń wybraną", AutoSize = true }; remove.Click += (_, _) => RemoveCharacter();
        controls.Controls.AddRange([new Label { Text = "Nazwa postaci:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) }, characterName, add, capture, remove]);
        layout.Controls.Add(controls, 0, 2); layout.Controls.Add(rotationState, 0, 3); group.Controls.Add(layout); return group;
    }

    private static TableLayoutPanel NewGrid()
    {
        var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Width = 650, Padding = new Padding(8, 4, 8, 8) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return grid;
    }
    private static Label RowLabel(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 7, 14, 3) };
    private void AddTextRow(TableLayoutPanel grid, string key, string label)
    {
        var row = grid.RowCount++; grid.Controls.Add(RowLabel(label), 0, row); var box = new TextBox { Width = 350 }; fields[key] = box; grid.Controls.Add(box, 1, row);
    }
    private void AddFileRow(TableLayoutPanel grid, string key, string label, string buttonText)
    {
        var row = grid.RowCount++; grid.Controls.Add(RowLabel(label), 0, row); var box = new TextBox { Width = 350 }; fields[key] = box; grid.Controls.Add(box, 1, row);
        var button = new Button { Text = buttonText, AutoSize = true }; button.Click += (_, _) => Browse(key); grid.Controls.Add(button, 2, row);
    }
    private static GroupBox Section(string title, string help, Control content)
    {
        var group = new GroupBox { Text = title, Width = 680, AutoSize = false, Padding = new Padding(9, 20, 9, 9) };
        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Width = 650 };
        flow.Controls.Add(new Label { Text = help, AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(3, 0, 3, 7) });
        flow.Controls.Add(content);
        flow.PerformLayout();
        group.Height = Math.Max(90, flow.PreferredSize.Height + 28);
        group.Controls.Add(flow);
        return group;
    }

    private void ConfigureTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Pokaż okno", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Zakończ aplikację", null, (_, _) => { exitRequested = true; Close(); });
        trayIcon.Icon = SystemIcons.Application;
        trayIcon.Text = "Tibia After SS 1.1.0";
        trayIcon.ContextMenuStrip = menu;
        trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        trayIcon.Visible = true;
    }
    private void SetStartup()
    {
        if (changingStartup) return;
        try { StartupRegistration.SetEnabled(startWithWindows.Checked); WriteLog(startWithWindows.Checked ? "Autostart z Windows włączony." : "Autostart z Windows wyłączony."); }
        catch (Exception ex) {
            changingStartup = true; startWithWindows.Checked = StartupRegistration.IsEnabled; changingStartup = false;
            MessageBox.Show(this, ex.Message, "Nie udało się zmienić autostartu", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    private void HideToTray()
    {
        Hide(); trayIcon.ShowBalloonTip(2000, "Tibia After SS", "Aplikacja działa w zasobniku systemowym.", ToolTipIcon.Info);
    }
    private void RestoreFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void HandleFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!exitRequested && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideToTray(); return; }
        cancellation.Cancel(); trayIcon.Visible = false;
    }

    private static Label TextLabel(string text, bool bold = false) => new() { Text = text, AutoSize = true, MaximumSize = new Size(680, 0), Font = bold ? new Font("Segoe UI", 11, FontStyle.Bold) : SystemFonts.DefaultFont, Margin = new Padding(3, 3, 3, 7) };
    private void Browse(string key) { using var d = new OpenFileDialog { Filter = key == "SuccessMarker" ? "Obraz PNG|*.png" : "Programy Windows|*.exe" }; if (d.ShowDialog(this) == DialogResult.OK) fields[key].Text = d.FileName; }
    private void RefreshProcessList()
    {
        if (fields["ProcessName"] is not ComboBox list) return;
        var selected = list.Text;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try { names.Add(process.ProcessName + ".exe"); }
            catch { /* A process may end while the list is refreshed. */ }
            finally { process.Dispose(); }
        }
        list.BeginUpdate(); list.Items.Clear();
        foreach (var name in names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) list.Items.Add(name);
        list.EndUpdate(); list.Text = selected;
    }
    private void RefreshCharacterList(CharacterProfile? select = null)
    {
        var chosen = select ?? characterList.SelectedItem as CharacterProfile;
        characterList.BeginUpdate(); characterList.Items.Clear();
        foreach (var character in config.Characters) characterList.Items.Add(character);
        characterList.EndUpdate();
        if (chosen is not null) characterList.SelectedItem = chosen;
        var next = GetCharacterForToday();
        rotationState.Text = next is null ? "Dodaj przynajmniej jedną postać z ustawioną pozycją." : $"Dzisiejszy wybór: {next.Name}. Jutro zostanie wybrana kolejna postać.";
    }
    private CharacterProfile? GetCharacterForToday()
    {
        var active = config.Characters.Where(c => c.Enabled && c.Position is not null).ToList();
        return active.Count == 0 ? null : active[DateOnly.FromDateTime(DateTime.Today).DayNumber % active.Count];
    }
    private void AddCharacter()
    {
        var name = characterName.Text.Trim();
        if (name.Length == 0) { MessageBox.Show(this, "Wpisz nazwę postaci.", "Brak nazwy", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (config.Characters.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show(this, "Ta postać jest już na liście.", "Duplikat", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        var character = new CharacterProfile { Name = name }; config.Characters.Add(character); characterName.Clear(); RefreshCharacterList(character); SaveConfig(true); WriteLog($"Dodano postać: {name}.");
    }
    private void RemoveCharacter()
    {
        if (characterList.SelectedItem is not CharacterProfile character) { MessageBox.Show(this, "Wybierz postać z listy.", "Brak wyboru", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        config.Characters.Remove(character); RefreshCharacterList(); SaveConfig(true); WriteLog($"Usunięto postać: {character.Name}.");
    }
    private async Task CaptureCharacterAsync()
    {
        if (characterList.SelectedItem is not CharacterProfile character) { MessageBox.Show(this, "Najpierw wybierz postać z listy.", "Brak wyboru", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        status.Text = $"Za 5 sekund zapamiętam pozycję postaci „{character.Name}”. Ustaw kursor nad jej nazwą na liście postaci.";
        Hide(); await Task.Delay(TimeSpan.FromSeconds(5));
        var point = Cursor.Position; character.Position = new PointConfig(point.X, point.Y);
        Show(); Activate(); RefreshCharacterList(character); SaveConfig(true);
        status.Text = $"Zapisano pozycję postaci „{character.Name}” w profilu „Domyślny”.";
    }
    private void ReadConfig() { foreach (var p in fields) p.Value.Text = config.GetType().GetProperty(p.Key)!.GetValue(config)?.ToString() ?? ""; UpdateCoordinateLabels(); UpdatePasswordIndicator(); }
    private void UpdateCoordinateLabels() { foreach (var (key, _) in CoordinateFields) coordLabels[key].Text = config.Coords.TryGetValue(key, out var p) ? $"({p.X}, {p.Y})" : "nie ustawiono"; }
    private void UpdatePasswordIndicator() { try { passwordState.Text = string.IsNullOrWhiteSpace(fields["Account"].Text) || CredentialStore.Read(fields["Account"].Text.Trim()) is null ? "Hasło nie jest zapisane dla tego konta." : "Hasło zapisane w magazynie poświadczeń Windows."; } catch { passwordState.Text = "Nie można odczytać magazynu poświadczeń Windows."; } }
    private void SavePassword()
    {
        var account = fields["Account"].Text.Trim();
        if (account.Length == 0 || password.Text.Length == 0 || passwordConfirmation.Text.Length == 0) { MessageBox.Show(this, "Wpisz e-mail, hasło i jego powtórzenie.", "Brak danych", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (!string.Equals(password.Text, passwordConfirmation.Text, StringComparison.Ordinal)) { MessageBox.Show(this, "Hasła nie są identyczne.", "Sprawdź hasło", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        try { CredentialStore.Save(account, password.Text); password.Clear(); passwordConfirmation.Clear(); showPasswords.Checked = false; UpdatePasswordIndicator(); WriteLog("Hasło zapisane w magazynie poświadczeń Windows."); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Nie udało się zapisać hasła", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    private async Task CaptureAsync(string key)
    {
        status.Text = "Za 5 sekund zostanie zapamiętana pozycja kursora. Ustaw go nad elementem gry.";
        Hide(); await Task.Delay(TimeSpan.FromSeconds(5));
        var p = Cursor.Position; config.Coords[key] = new PointConfig(p.X, p.Y); UpdateCoordinateLabels();
        Show(); Activate(); SaveConfig(true);
        status.Text = $"Zapisano pozycję: {key} w profilu „Domyślny”.";
    }
    private bool SaveConfig(bool quiet)
    {
        try {
            foreach (var name in new[] { "CloseTime", "LoginTime", "RetryUntil" }) _ = TimeOnly.ParseExact(fields[name].Text.Trim(), "HH:mm");
            var exe = fields["ExePath"].Text.Trim(); if (exe.Length > 0 && (!File.Exists(exe) || !string.Equals(Path.GetExtension(exe), ".exe", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Wybierz istniejący plik EXE klienta Tibii.");
            var marker = fields["SuccessMarker"].Text.Trim(); if (marker.Length > 0 && !File.Exists(marker)) throw new InvalidOperationException("Nie znaleziono obrazu potwierdzenia logowania.");
            foreach (var p in fields) config.GetType().GetProperty(p.Key)!.SetValue(config, p.Value.Text.Trim()); SettingsStore.Save(config); if (!quiet) WriteLog("Profil „Domyślny” zapisany."); return true;
        } catch (Exception ex) { if (!quiet) MessageBox.Show(this, ex.Message, "Błąd ustawień", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
    }
    private string? ValidateRun()
    {
        if (string.IsNullOrWhiteSpace(config.ExePath) || !File.Exists(config.ExePath)) return "Wybierz ścieżkę do EXE klienta.";
        if (string.IsNullOrWhiteSpace(config.Account)) return "Wpisz e-mail konta.";
        if (CoordinateFields.Any(x => !config.Coords.ContainsKey(x.Key))) return "Skalibruj wszystkie pozycje kliknięć.";
        if (config.Characters.Count == 0) return "Dodaj przynajmniej jedną postać do rotacji.";
        if (config.Characters.Any(c => c.Enabled && c.Position is null)) return "Ustaw pozycję każdej aktywnej postaci.";
        try { if (CredentialStore.Read(config.Account) is null) return "Zapisz hasło konta."; } catch (Exception ex) { return "Nie można odczytać magazynu poświadczeń: " + ex.Message; }
        return null;
    }
    private async Task TestAsync() { if (!SaveConfig(true)) return; var error = ValidateRun(); if (error is not null) { MessageBox.Show(this, error, "Brakuje konfiguracji", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } if (running) return; if (MessageBox.Show(this, "Program zamknie teraz Tibię i spróbuje zalogować postać. Kontynuować?", "Test logowania", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) await RunAsync("login", true); }
    private void Tick()
    {
        if (running) return; var now = DateTime.Now;
        foreach (var action in new[] { ("close", fields["CloseTime"].Text), ("login", fields["LoginTime"].Text) })
            if (TimeOnly.TryParseExact(action.Item2.Trim(), "HH:mm", out var at)) { var when = now.Date + at.ToTimeSpan(); var key = $"{now:yyyy-MM-dd}:{action.Item1}"; if (!completedActions.Contains(key) && now >= when && now - when < TimeSpan.FromMinutes(2)) { completedActions.Add(key); if (SaveConfig(true)) _ = RunAsync(action.Item1, false); } }
    }
    private async Task RunAsync(string action, bool test)
    {
        if (running) return; running = true;
        try { if (action == "close") { WriteLog("Godzina server save: zamykam klienta Tibii."); await CloseClientAsync(); } else { var error = ValidateRun(); if (error is not null) { WriteLog("Nie można rozpocząć: " + error); return; } await ReconnectAsync(test, cancellation.Token); } }
        catch (OperationCanceledException) { } catch (Exception ex) { WriteLog($"Błąd: {ex.GetType().Name}: {ex.Message}"); } finally { running = false; }
    }
    private async Task CloseClientAsync()
    {
        var name = Path.GetFileNameWithoutExtension(config.ProcessName.Trim()); if (name.Length == 0) { WriteLog("Nie ustawiono nazwy procesu — pomijam zamykanie."); return; }
        var processes = Process.GetProcessesByName(name); foreach (var p in processes) { try { p.CloseMainWindow(); } catch { } }
        await Task.Delay(1500, cancellation.Token); foreach (var p in processes.Where(p => { try { return !p.HasExited; } catch { return false; } })) { try { p.Kill(entireProcessTree: true); } catch { } }
        WriteLog(processes.Length == 0 ? "Klient nie działał; nie trzeba go zamykać." : $"Zamknięto {processes.Length} proces(ów) {config.ProcessName}.");
    }
    private async Task ReconnectAsync(bool test, CancellationToken token)
    {
        var secret = CredentialStore.Read(config.Account); if (secret is null) { WriteLog("Brak hasła w magazynie poświadczeń Windows."); return; }
        var cutoff = DateTime.Today + TimeOnly.ParseExact(config.RetryUntil, "HH:mm").ToTimeSpan(); if (!test && DateTime.Now > cutoff) { WriteLog("Minął czas zakończenia prób logowania."); return; }
        var marker = !string.IsNullOrWhiteSpace(config.SuccessMarker); var attempt = 0;
        while (!token.IsCancellationRequested) {
            if (marker && ScreenMarker.IsVisible(config.SuccessMarker)) { WriteLog("Wykryto ekran gry; postać prawdopodobnie jest zalogowana."); return; }
            attempt++; WriteLog($"Próba logowania nr {attempt}."); await CloseClientAsync();
            Process.Start(new ProcessStartInfo(config.ExePath) { WorkingDirectory = Path.GetDirectoryName(config.ExePath)! }); WriteLog("Uruchomiono klienta; oczekiwanie na okno logowania...");
            await Task.Delay(TimeSpan.FromSeconds(config.ClientLoadSeconds), token); WindowFocus.TryFocus("Tibia");
            foreach (var (field, value) in new[] { ("email", config.Account), ("password", secret) }) { var p = config.Coords[field]; Cursor.Position = new Point(p.X, p.Y); MouseButtonsClick(); NativeInput.SelectAll(); NativeInput.TypeUnicode(value); await Task.Delay(700, token); }
            ClickInput("login"); await Task.Delay(TimeSpan.FromSeconds(config.AfterLoginSeconds), token);
            var character = GetCharacterForToday() ?? throw new InvalidOperationException("Brak postaci z ustawioną pozycją.");
            Cursor.Position = new Point(character.Position!.X, character.Position.Y); MouseButtonsClick();
            await Task.Delay(650, token); ClickInput("enter"); WriteLog($"Wybrano postać „{character.Name}”; oczekiwanie na wejście do świata...");
            if (!marker) { WriteLog("Wykonano jedną próbę. Bez obrazu potwierdzenia program nie ponawia logowania."); return; }
            for (var i = 0; i < 12; i++) { await Task.Delay(3000, token); if (ScreenMarker.IsVisible(config.SuccessMarker)) { WriteLog("Wykryto ekran gry — logowanie zakończone."); return; } }
            if (test || DateTime.Now.AddSeconds(config.RetrySeconds) > cutoff) { WriteLog("Nie potwierdzono zalogowania — zakończono próby."); return; }
            WriteLog($"Brak potwierdzenia. Ponowienie za {config.RetrySeconds} sekund."); await Task.Delay(TimeSpan.FromSeconds(config.RetrySeconds), token);
        }
    }
    private void ClickInput(string key) { var p = config.Coords[key]; Cursor.Position = new Point(p.X, p.Y); MouseButtonsClick(); }
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    private static void MouseButtonsClick() { mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); }
    private void WriteLog(string message) { var line = $"[{DateTime.Now:HH:mm:ss}] {message}"; if (InvokeRequired) { BeginInvoke(() => WriteLog(message)); return; } log.AppendText(line + Environment.NewLine); status.Text = message; }
}

internal static class WindowFocus
{
    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    public static void TryFocus(string phrase) { EnumWindows((h, _) => { var text = new StringBuilder(512); GetWindowText(h, text, text.Capacity); if (IsWindowVisible(h) && text.ToString().Contains(phrase, StringComparison.OrdinalIgnoreCase)) { SetForegroundWindow(h); return false; } return true; }, IntPtr.Zero); }
}

internal static class ScreenMarker
{
    public static bool IsVisible(string filename)
    {
        using var template = new Bitmap(filename); var area = SystemInformation.VirtualScreen;
        using var screen = new Bitmap(area.Width, area.Height); using (var g = Graphics.FromImage(screen)) g.CopyFromScreen(area.Left, area.Top, 0, 0, area.Size);
        // Small, static patterns are intended. Sampling keeps a full-screen check responsive without external OpenCV.
        var anchors = new[] { new Point(0, 0), new Point(template.Width - 1, 0), new Point(0, template.Height - 1), new Point(template.Width - 1, template.Height - 1), new Point(template.Width / 2, template.Height / 2) };
        var step = Math.Max(1, Math.Min(template.Width, template.Height) / 12);
        for (var y = 0; y <= screen.Height - template.Height; y += step)
            for (var x = 0; x <= screen.Width - template.Width; x += step)
                if (anchors.All(a => Similar(template.GetPixel(a.X, a.Y), screen.GetPixel(x + a.X, y + a.Y))) && Matches(template, screen, x, y, step)) return true;
        return false;
    }
    private static bool Matches(Bitmap template, Bitmap screen, int x, int y, int step) { for (var ty = 0; ty < template.Height; ty += step) for (var tx = 0; tx < template.Width; tx += step) if (!Similar(template.GetPixel(tx, ty), screen.GetPixel(x + tx, y + ty))) return false; return true; }
    private static bool Similar(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) < 45;
}



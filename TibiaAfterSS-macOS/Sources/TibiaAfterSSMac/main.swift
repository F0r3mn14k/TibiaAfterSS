import AppKit
import CoreGraphics
import Security
import ServiceManagement
import SwiftUI
import UniformTypeIdentifiers

@main
struct TibiaAfterSSMacApp: App {
    @StateObject private var model = AppModel()

    var body: some Scene {
        WindowGroup("Tibia After SS") { ContentView().environmentObject(model) }
            .windowResizability(.contentSize)
        MenuBarExtra("Tibia After SS", systemImage: "clock.arrow.circlepath") {
            Button("Pokaż okno") { NSApp.activate(ignoringOtherApps: true) }
            Divider()
            Text(model.nextActionText)
            Button("Zakończ") { NSApp.terminate(nil) }
        }
    }
}

struct PointValue: Codable, Hashable { var x: CGFloat; var y: CGFloat }
struct CharacterProfile: Codable, Identifiable, Hashable {
    var id = UUID(); var name: String; var enabled = true; var position: PointValue?
}
struct Profile: Codable {
    var appPath = ""; var processName = "Tibia"; var account = ""
    var closeTime = "10:02"; var loginTime = "10:16"; var retryUntil = "11:00"
    var loadSeconds = 25; var afterLoginSeconds = 8
    var emailPoint: PointValue?; var passwordPoint: PointValue?; var loginPoint: PointValue?; var enterPoint: PointValue?
    var characters: [CharacterProfile] = []
}

@MainActor
final class AppModel: ObservableObject {
    @Published var profile = Profile() { didSet { save() } }
    @Published var password = ""; @Published var passwordAgain = ""; @Published var showPassword = false
    @Published var selectedCharacter: UUID?; @Published var log = ""; @Published var isBusy = false
    @Published var startAtLogin = false { didSet { updateLoginItem() } }
    private var timer: Timer?
    private var actionsToday = Set<String>()
    private let service = "TibiaAfterSSMac"
    private var profileURL: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("TibiaAfterSSMac")
        try? FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        return base.appendingPathComponent("profile.json")
    }

    init() {
        load(); startAtLogin = SMAppService.mainApp.status == .enabled
        timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in Task { @MainActor in self?.tick() } }
    }
    var nextActionText: String { "Następna postać: \(characterForToday()?.name ?? "brak")" }
    var selected: CharacterProfile? { profile.characters.first { $0.id == selectedCharacter } }
    func write(_ text: String) { log += "[\(Date.now.formatted(date: .omitted, time: .standard))] \(text)\n" }
    func load() { if let data = try? Data(contentsOf: profileURL), let loaded = try? JSONDecoder().decode(Profile.self, from: data) { profile = loaded }; password = Keychain.read(service: service, account: profile.account) ?? "" }
    func save() { if let data = try? JSONEncoder().encode(profile) { try? data.write(to: profileURL, options: .atomic) } }
    func savePassword() -> String? {
        guard !profile.account.isEmpty, !password.isEmpty, !passwordAgain.isEmpty else { return "Wpisz e-mail i hasło dwa razy." }
        guard password == passwordAgain else { return "Hasła nie są identyczne." }
        guard Keychain.save(service: service, account: profile.account, secret: password) else { return "Nie udało się zapisać hasła w Keychain." }
        password = ""; passwordAgain = ""; write("Hasło zapisane w macOS Keychain."); return nil
    }
    func updateLoginItem() { do { if startAtLogin { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() } } catch { write("Nie udało się zmienić autostartu: \(error.localizedDescription)") } }
    func addCharacter(named name: String) { let clean = name.trimmingCharacters(in: .whitespacesAndNewlines); guard !clean.isEmpty else { return }; let item = CharacterProfile(name: clean); profile.characters.append(item); selectedCharacter = item.id; write("Dodano postać \(clean).") }
    func removeSelected() { guard let id = selectedCharacter, let item = selected else { return }; profile.characters.removeAll { $0.id == id }; selectedCharacter = nil; write("Usunięto postać \(item.name).") }
    func capture(_ field: WritableKeyPath<Profile, PointValue?>, label: String) async {
        guard Automation.hasAccessibilityPermission(prompt: true) else { write("Nadaj aplikacji uprawnienie Dostępność w Ustawieniach systemowych."); return }
        NSApp.hide(nil); try? await Task.sleep(for: .seconds(5)); profile[keyPath: field] = Automation.cursorPoint(); NSApp.activate(ignoringOtherApps: true); write("Zapisano pozycję: \(label).")
    }
    func captureCharacter() async {
        guard let id = selectedCharacter, let index = profile.characters.firstIndex(where: { $0.id == id }) else { return }
        guard Automation.hasAccessibilityPermission(prompt: true) else { write("Nadaj aplikacji uprawnienie Dostępność."); return }
        NSApp.hide(nil); try? await Task.sleep(for: .seconds(5)); profile.characters[index].position = Automation.cursorPoint(); NSApp.activate(ignoringOtherApps: true); write("Zapisano pozycję postaci \(profile.characters[index].name).")
    }
    func characterForToday() -> CharacterProfile? { let list = profile.characters.filter { $0.enabled && $0.position != nil }; return list.isEmpty ? nil : list[Calendar.current.ordinality(of: .day, in: .era, for: .now)! % list.count] }
    func test() { Task { await reconnect(test: true) } }
    private func tick() {
        guard !isBusy else { return }; let now = Date.now; let day = now.formatted(.dateTime.year().month().day())
        if matches(profile.closeTime, now), actionsToday.insert("\(day)-close").inserted { Task { await closeClient() } }
        if matches(profile.loginTime, now), actionsToday.insert("\(day)-login").inserted { Task { await reconnect(test: false) } }
    }
    private func matches(_ hhmm: String, _ date: Date) -> Bool { date.formatted(date: .omitted, time: .shortened) == hhmm }
    private func closeClient() async {
        let name = profile.processName.trimmingCharacters(in: .whitespacesAndNewlines); guard !name.isEmpty else { return }
        let apps = NSWorkspace.shared.runningApplications.filter { $0.localizedName?.lowercased() == name.lowercased() || $0.bundleURL?.lastPathComponent.lowercased() == "\(name.lowercased()).app" }
        apps.forEach { $0.terminate() }; try? await Task.sleep(for: .seconds(2)); apps.filter { !$0.isTerminated }.forEach { $0.forceTerminate() }; write(apps.isEmpty ? "Klient nie działał." : "Zamknięto klienta.")
    }
    private func reconnect(test: Bool) async {
        guard !isBusy else { return }; isBusy = true; defer { isBusy = false }
        guard let email = profile.emailPoint, let passwordPoint = profile.passwordPoint, let login = profile.loginPoint, let enter = profile.enterPoint, let character = characterForToday(), let characterPoint = character.position else { write("Skalibruj wszystkie pola i dodaj postać do rotacji."); return }
        guard let secret = Keychain.read(service: service, account: profile.account), !profile.appPath.isEmpty else { write("Brakuje hasła w Keychain lub ścieżki do klienta."); return }
        guard Automation.hasAccessibilityPermission(prompt: true) else { write("Brakuje uprawnienia Dostępność."); return }
        write("Próba logowania postaci \(character.name)."); await closeClient()
        try? await NSWorkspace.shared.openApplication(at: URL(fileURLWithPath: profile.appPath), configuration: .init())
        try? await Task.sleep(for: .seconds(profile.loadSeconds)); Automation.activateTibia()
        Automation.click(email); Automation.selectAll(); Automation.type(profile.account)
        Automation.click(passwordPoint); Automation.selectAll(); Automation.type(secret)
        Automation.click(login); try? await Task.sleep(for: .seconds(profile.afterLoginSeconds)); Automation.click(characterPoint); try? await Task.sleep(for: .milliseconds(650)); Automation.click(enter)
        write("Wybrano postać \(character.name).")
    }
}

struct ContentView: View {
    @EnvironmentObject var model: AppModel
    @State private var newCharacter = ""
    @State private var message: String?
    var body: some View {
        Form {
            Section("1. Klient i konto") {
                fileField; TextField("Nazwa procesu / aplikacji", text: $model.profile.processName)
                TextField("E-mail konta", text: $model.profile.account)
                if model.showPassword {
                    TextField("Hasło", text: $model.password); TextField("Powtórz hasło", text: $model.passwordAgain)
                } else {
                    SecureField("Hasło", text: $model.password); SecureField("Powtórz hasło", text: $model.passwordAgain)
                }
                Toggle("Pokaż hasła", isOn: $model.showPassword)
                Button("Zapisz hasło w Keychain") { message = model.savePassword() }
                Toggle("Uruchamiaj po zalogowaniu do macOS", isOn: $model.startAtLogin)
            }
            Section("Harmonogram") { TextField("Zamknij klienta o (HH:mm)", text: $model.profile.closeTime); TextField("Loguj od (HH:mm)", text: $model.profile.loginTime) }
            Section("2. Kalibracja") {
                calibration("Pole e-mail", \.emailPoint); calibration("Pole hasła", \.passwordPoint); calibration("Przycisk Zaloguj", \.loginPoint); calibration("Przycisk Wejdź do gry", \.enterPoint)
            }
            Section("Rotacja postaci") {
                List(selection: $model.selectedCharacter) { ForEach(model.profile.characters) { item in Text(item.name).tag(item.id) } }.frame(height: 110)
                HStack { TextField("Nazwa postaci", text: $newCharacter); Button("Dodaj") { model.addCharacter(named: newCharacter); newCharacter = "" }; Button("Ustaw pozycję") { Task { await model.captureCharacter() } }; Button("Usuń", role: .destructive) { model.removeSelected() } }
                Text(model.nextActionText)
            }
            Section("Test i dziennik") { Button("Test: uruchom i zaloguj") { model.test() }; TextEditor(text: $model.log).font(.system(.caption, design: .monospaced)).frame(height: 120) }
        }.padding().frame(minWidth: 700, minHeight: 720).alert("Tibia After SS", isPresented: Binding(get: { message != nil }, set: { if !$0 { message = nil } })) { Button("OK") {} } message: { Text(message ?? "") }
    }
    private var fileField: some View { HStack { Text(model.profile.appPath.isEmpty ? "Wybierz aplikację Tibii" : URL(fileURLWithPath: model.profile.appPath).lastPathComponent); Spacer(); Button("Wybierz") { let panel = NSOpenPanel(); panel.allowedContentTypes = [.applicationBundle]; panel.canChooseFiles = true; if panel.runModal() == .OK { model.profile.appPath = panel.url?.path ?? "" } } } }
    private func calibration(_ label: String, _ key: WritableKeyPath<Profile, PointValue?>) -> some View { HStack { Text(label); Spacer(); Text(model.profile[keyPath: key].map { "(\(Int($0.x)), \(Int($0.y)))" } ?? "nie ustawiono").foregroundStyle(.secondary); Button("Ustaw (5 s)") { Task { await model.capture(key, label: label) } } } }
}

enum Keychain {
    static func save(service: String, account: String, secret: String) -> Bool {
        let data = Data(secret.utf8); let query: [CFString: Any] = [kSecClass: kSecClassGenericPassword, kSecAttrService: service, kSecAttrAccount: account]
        SecItemDelete(query as CFDictionary)
        var add = query; add[kSecValueData] = data; return SecItemAdd(add as CFDictionary, nil) == errSecSuccess
    }
    static func read(service: String, account: String) -> String? {
        let query: [CFString: Any] = [kSecClass: kSecClassGenericPassword, kSecAttrService: service, kSecAttrAccount: account, kSecReturnData: true, kSecMatchLimit: kSecMatchLimitOne]
        var result: CFTypeRef?; guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess, let data = result as? Data else { return nil }; return String(data: data, encoding: .utf8)
    }
}

enum Automation {
    static func hasAccessibilityPermission(prompt: Bool) -> Bool { AXIsProcessTrustedWithOptions([kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: prompt] as CFDictionary) }
    static func cursorPoint() -> PointValue { let p = NSEvent.mouseLocation; let height = NSScreen.screens.map { $0.frame.maxY }.max() ?? 0; return PointValue(x: p.x, y: height - p.y) }
    static func click(_ point: PointValue) { let p = CGPoint(x: point.x, y: point.y); CGEvent(mouseEventSource: nil, mouseType: .leftMouseDown, mouseCursorPosition: p, mouseButton: .left)?.post(tap: .cghidEventTap); CGEvent(mouseEventSource: nil, mouseType: .leftMouseUp, mouseCursorPosition: p, mouseButton: .left)?.post(tap: .cghidEventTap) }
    static func selectAll() { key(0x00, flags: .maskCommand) }
    static func type(_ text: String) { let event = CGEvent(keyboardEventSource: nil, virtualKey: 0, keyDown: true); event?.keyboardSetUnicodeString(stringLength: text.utf16.count, unicodeString: Array(text.utf16)); event?.post(tap: .cghidEventTap); event?.copy()?.setIntegerValueField(.keyboardEventAutorepeat, value: 0); let up = CGEvent(keyboardEventSource: nil, virtualKey: 0, keyDown: false); up?.keyboardSetUnicodeString(stringLength: text.utf16.count, unicodeString: Array(text.utf16)); up?.post(tap: .cghidEventTap) }
    static func key(_ code: CGKeyCode, flags: CGEventFlags) { let down = CGEvent(keyboardEventSource: nil, virtualKey: code, keyDown: true); down?.flags = flags; down?.post(tap: .cghidEventTap); let up = CGEvent(keyboardEventSource: nil, virtualKey: code, keyDown: false); up?.flags = flags; up?.post(tap: .cghidEventTap) }
    static func activateTibia() { NSWorkspace.shared.runningApplications.first { $0.localizedName?.localizedCaseInsensitiveContains("tibia") == true }?.activate(options: [.activateAllWindows, .activateIgnoringOtherApps]) }
}


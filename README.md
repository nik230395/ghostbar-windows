# Ghostbar für Windows

Windows-Port von [rbc33/Ghostbar](https://github.com/rbc33/Ghostbar): ein KI-Chat, der für Screen-Sharing und Aufnahmen unsichtbar ist.
C# / .NET 10 (WinForms) + WebView2. Die Chat-Oberfläche ist das HTML des Originals.

## Unsichtbar

- Kein Taskleisten-Button, kein Tray-Icon, kein Alt+Tab-Eintrag (`WS_EX_TOOLWINDOW`).
- Fenster ist von Capture ausgeschlossen (`SetWindowDisplayAffinity` / `WDA_EXCLUDEFROMCAPTURE`, Windows 10 2004+).
- Bedienung nur über globale Hotkeys. Im Task-Manager erscheint `Ghostbar.exe` weiterhin.

## Voraussetzungen

- Windows 10 (Version 2004) oder neuer
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64)
- WebView2 Runtime (auf Windows 10/11 meist vorinstalliert)

## Bauen

Auf Windows:

```powershell
./build.ps1
```

Oder auf macOS/Linux (Cross-Build):

```bash
./build.sh
```

Ergebnis: `publish/Ghostbar.exe` (den ganzen `publish/`-Ordner kopieren, `WebView2Loader.dll` liegt daneben).

## Bedienung

| Aktion | Standard (anpassbar in Settings) |
|---|---|
| Fenster ein/aus (global) | Ctrl+Shift+Space |
| Mikrofon (im Chat) | Ctrl+Shift+M |
| Screenshot anhängen (im Chat) | Ctrl+Shift+S |
| Nachricht senden | Enter |
| Neue Zeile | Shift+Enter |

Settings: Zahnrad im Chat. Dort auch **Quit Ghostbar** zum Beenden.
Einstellungen liegen in `%APPDATA%\Ghostbar\settings.json`, der API-Key ist per DPAPI verschlüsselt.

## Backends

Ollama, OpenAI, Anthropic, Google Gemini, OpenRouter sowie alle OpenAI-kompatiblen Server (NVIDIA NIM, llama.cpp, LM Studio, vLLM).

## Beispiel: Gemini (kostenloser Key)

1. [Ghostbar-Release](../../releases) bzw. `publish/`-Ordner auf den Windows-Rechner kopieren und `Ghostbar.exe` starten (es erscheint nichts, das ist gewollt).
2. Auf <https://aistudio.google.com/api-keys> anmelden und **Create API key** wählen (Name z. B. `ghostbar`, Standardprojekt). Key kopieren.
3. **Ctrl+Shift+Space** drücken, dann das Zahnrad im Chat öffnen.
4. Backend **Google Gemini** auswählen, Key einfügen, **Test**, dann **Save**.
5. Im Chat-Header ein Modell wählen, z. B. eines mit `flash` im Namen.
6. Fertig: Das App-Icon verstecken ist nicht nötig, es gibt keins.

Gemini wird über den OpenAI-kompatiblen Endpunkt `https://generativelanguage.googleapis.com/v1beta/openai` angesprochen. Für andere Anbieter gilt derselbe Ablauf: Backend wählen, Key einfügen, Save.

## Spracheingabe (optional)

`whisper-cli.exe` ([whisper.cpp Releases](https://github.com/ggml-org/whisper.cpp/releases)) und ein Modell (`ggml-base.bin`) nach `%LOCALAPPDATA%\Ghostbar\whisper\` legen (oder neben `Ghostbar.exe` / in den PATH).

## Test-Checkliste

1. `Ghostbar.exe` starten: nichts sichtbar (kein Fenster, kein Taskleisten-/Tray-Symbol).
2. Ctrl+Shift+Space zeigt/versteckt den Chat; nicht in Alt+Tab.
3. Win+Shift+S, OBS, Teams/Zoom-Freigabe: Fenster nicht sichtbar.
4. Header ziehen, Ränder ziehen; Position bleibt nach Neustart.
5. Ollama oder API-Backend streamt Antworten, Modellliste lädt.
6. Screenshot-Hotkey hängt Bild an, Vision-Modell antwortet.
7. Settings → Quit beendet den Prozess.

## Unterschiede zum macOS-Original

- Hotkeys nutzen Ctrl+Shift statt ⌘⌥ (Ctrl+Alt ist unter Windows AltGr).
- Hotkeys brauchen immer eine Taste; Modifier-only-Kürzel gibt es nicht.
- Modellauswahl erfolgt im Chat-Header; sie wird nicht über Neustarts gespeichert.
- Lizenz: MIT, Original © rbc33.

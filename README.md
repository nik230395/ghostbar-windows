# Ghostbar für Windows

Windows-Port von [rbc33/Ghostbar](https://github.com/rbc33/Ghostbar): ein KI-Chat, der für Screen-Sharing und Aufnahmen unsichtbar ist.
C# / .NET 10 (WinForms) + WebView2. Die Chat-Oberfläche ist das HTML des Originals.

## Inhalt

1. [So funktioniert es](#so-funktioniert-es)
2. [Installation](#installation)
3. [Erster Start](#erster-start)
4. [API anbinden](#api-anbinden)
5. [Bedienung](#bedienung)
6. [Autostart](#autostart)
7. [Spracheingabe (optional)](#spracheingabe-optional)
8. [Update, Beenden, Deinstallieren](#update-beenden-deinstallieren)
9. [Probleme lösen](#probleme-lösen)
10. [Test-Checkliste](#test-checkliste)

## So funktioniert es

- Es gibt **keine Installation im Sinne eines Setup-Programms**. Ghostbar ist ein Ordner mit `Ghostbar.exe`, den du irgendwo ablegst und startest.
- Nach dem Start erscheint **nichts**: kein Fenster, kein Taskleisten-Button, kein Tray-Icon, kein Alt+Tab-Eintrag (`WS_EX_TOOLWINDOW`).
- Das Fenster ist von Capture ausgeschlossen (`SetWindowDisplayAffinity` / `WDA_EXCLUDEFROMCAPTURE`, Windows 10 ab Version 2004).
- Bedient wird nur über globale Hotkeys. Im Task-Manager (Details) erscheint `Ghostbar.exe` weiterhin, das lässt sich nicht verhindern.

## Installation

### Schritt 1: Voraussetzungen prüfen

| Was | Wofür | Prüfen / Installieren |
|---|---|---|
| Windows 10 ab Version 2004 (Build 19041) oder Windows 11, 64 Bit | Capture-Schutz | `Win+R`, `winver` |
| **.NET 10 Desktop Runtime (x64)** | Programm starten | `winget install Microsoft.DotNet.DesktopRuntime.10` oder [Download](https://dotnet.microsoft.com/download/dotnet/10.0) (".NET Desktop Runtime", x64) |
| **WebView2 Runtime** | Chat-Oberfläche | Auf Windows 10/11 normalerweise vorinstalliert. Sonst: `winget install Microsoft.EdgeWebView2Runtime` |

Prüfen, ob die .NET-Runtime da ist (PowerShell):

```powershell
dotnet --list-runtimes
```

Es muss eine Zeile `Microsoft.WindowsDesktop.App 10.x.x` erscheinen. Ohne diese Zeile startet `Ghostbar.exe` still und zeigt nichts an.

### Schritt 2: Ghostbar besorgen (eine Variante wählen)

#### Variante A: fertiges Paket (am einfachsten)

1. Auf der Seite [Releases](../../releases) die Datei `Ghostbar-win-x64.zip` herunterladen.
2. Rechtsklick auf die ZIP, **Eigenschaften**, unten **Zulassen** anhaken (falls vorhanden), **OK**.
3. ZIP entpacken, z. B. nach `C:\Tools\Ghostbar\`. Der **ganze Ordner** muss zusammenbleiben (neben `Ghostbar.exe` liegt `WebView2Loader.dll`).

Gibt es noch kein Release, nimm Variante B oder C. Ein Release erzeugst du so (siehe Variante C für das ZIP):

```bash
gh release create v1.0.0 Ghostbar-win-x64.zip --title "Ghostbar 1.0.0" --notes "Erste Windows-Version"
```

#### Variante B: selbst bauen auf Windows

1. [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) installieren (`winget install Microsoft.DotNet.SDK.10`).
2. Repo holen und bauen (PowerShell):

```powershell
git clone https://github.com/nik230395/ghostbar-windows.git
cd ghostbar-windows
./build.ps1
```

Falls PowerShell das Skript blockiert: `powershell -ExecutionPolicy Bypass -File build.ps1`.

3. Ergebnis ist der Ordner `publish\` mit `Ghostbar.exe`. Kopiere ihn komplett an einen festen Ort, z. B. `C:\Tools\Ghostbar\`.

#### Variante C: auf macOS/Linux bauen und auf Windows kopieren

```bash
git clone https://github.com/nik230395/ghostbar-windows.git
cd ghostbar-windows
./build.sh
cd publish && zip -r ../Ghostbar-win-x64.zip . && cd ..
```

`Ghostbar-win-x64.zip` auf den Windows-Rechner bringen (USB, Cloud, Mail) und wie in Variante A entpacken.

### Schritt 3: Windows-Warnung (SmartScreen)

Die App ist nicht signiert. Beim ersten Start kann „Der Computer wurde durch Windows geschützt“ erscheinen. Dann **Weitere Informationen**, **Trotzdem ausführen**. Das passiert nicht, wenn du selbst gebaut hast (Variante B).

## Erster Start

1. `Ghostbar.exe` doppelklicken. **Es passiert sichtbar nichts, das ist richtig.**
2. **Ctrl+Shift+Space** drücken, der Chat öffnet sich.
3. Öffnet sich nichts: siehe [Probleme lösen](#probleme-lösen).
4. Zahnrad oben im Chat öffnen und ein Backend einrichten (nächster Abschnitt).

## API anbinden

Ablauf für jedes Backend: **Ctrl+Shift+Space**, Zahnrad, Backend wählen (die URL füllt sich selbst), Key einfügen, **Test**, **Save**. Die Modellliste lädt danach automatisch, das Modell wählst du im Chat-Header.

| Backend | Key nötig | Hinweis |
|---|---|---|
| Ollama | nein | Lokal, `http://localhost:11434` |
| OpenAI | ja | |
| Anthropic | ja | |
| Google Gemini | ja | Kostenloser Key in AI Studio |
| OpenRouter | ja | |
| NVIDIA NIM | ja | Kostenloser Tarif verfügbar |
| llama.cpp, LM Studio, vLLM | nein | Lokale Server |

Jeder OpenAI-kompatible Server funktioniert: Backend **OpenAI** wählen und die URL ändern.

### Beispiel: Gemini (kostenloser Key)

1. Auf <https://aistudio.google.com/api-keys> anmelden und **Create API key** wählen (Name z. B. `ghostbar`, Standardprojekt). Key kopieren.
2. **Ctrl+Shift+Space**, Zahnrad.
3. Backend **Google Gemini** wählen, Key einfügen, **Test** (muss „✓ Connected“ zeigen), **Save**.
4. Im Chat-Header ein Modell wählen, z. B. eines mit `flash` im Namen.

Gemini läuft über den OpenAI-kompatiblen Endpunkt `https://generativelanguage.googleapis.com/v1beta/openai`.

Der Key liegt mit Windows-DPAPI verschlüsselt in `%APPDATA%\Ghostbar\settings.json` und ist nur für deinen Windows-Benutzer lesbar.

## Bedienung

| Aktion | Standard (in den Settings änderbar) |
|---|---|
| Fenster ein/aus (global) | Ctrl+Shift+Space |
| Mikrofon (im Chat) | Ctrl+Shift+M |
| Screenshot anhängen (im Chat) | Ctrl+Shift+S |
| Nachricht senden | Enter |
| Neue Zeile | Shift+Enter |

- Verschieben: Header (obere Leiste) ziehen. Größe ändern: an den Rändern ziehen. Position und Größe bleiben gespeichert.
- Zahnrad: Settings. Dort steht auch **Quit Ghostbar** zum Beenden.
- × im Chat versteckt nur das Fenster, die App läuft weiter.

## Autostart

Damit Ghostbar nach jedem Anmelden bereitsteht:

1. `Win+R`, `shell:startup`, Enter. Der Autostart-Ordner öffnet sich.
2. Rechtsklick auf `Ghostbar.exe`, **Verknüpfung erstellen**, die Verknüpfung in den Autostart-Ordner verschieben.

Entfernen: Verknüpfung dort löschen.

## Spracheingabe (optional)

Läuft komplett lokal über [whisper.cpp](https://github.com/ggml-org/whisper.cpp).

1. Von den [whisper.cpp Releases](https://github.com/ggml-org/whisper.cpp/releases) die Windows-x64-ZIP laden. Benötigt wird `whisper-cli.exe` (plus die mitgelieferten DLLs).
2. Ein Modell laden, z. B. `ggml-base.bin` (ca. 150 MB) von <https://huggingface.co/ggerganov/whisper.cpp>.
3. Alles in einen Ordner legen: `%LOCALAPPDATA%\Ghostbar\whisper\` (alternativ neben `Ghostbar.exe` oder in den PATH).
4. Im Chat **Ctrl+Shift+M** drücken, sprechen, nochmal drücken. Der Text wird transkribiert und gesendet.

## Update, Beenden, Deinstallieren

- **Beenden:** Settings (Zahnrad), **Quit Ghostbar**. Notfalls Task-Manager, Tab Details, `Ghostbar.exe` beenden.
- **Update:** Ghostbar beenden, Ordner durch die neue Version ersetzen. Einstellungen bleiben erhalten, sie liegen nicht im Programmordner.
- **Deinstallieren:** Ghostbar beenden, Programmordner löschen, Autostart-Verknüpfung löschen. Optional auch `%APPDATA%\Ghostbar` (Einstellungen) und `%LOCALAPPDATA%\Ghostbar` (WebView-Daten, Whisper) löschen.

## Probleme lösen

| Symptom | Ursache / Lösung |
|---|---|
| Hotkey tut nichts | Läuft die App? Task-Manager, Details, `Ghostbar.exe`. Ist sie da, belegt ein anderes Programm das Kürzel. In den Settings ein anderes Kürzel setzen. |
| `Ghostbar.exe` startet nicht, keine Meldung | .NET 10 Desktop Runtime fehlt: `dotnet --list-runtimes` prüfen. |
| Chat bleibt leer oder weiß | WebView2 Runtime fehlt oder ist zu alt: `winget install Microsoft.EdgeWebView2Runtime`. |
| Test zeigt „HTTP 401“ | Key falsch oder für anderes Backend. |
| Test zeigt Verbindungsfehler | Server läuft nicht (Ollama/LM Studio starten) oder URL falsch. |
| Modellliste leer („no models“) | Test zuerst ausführen, dann Save. Bei Ollama muss mindestens ein Modell geladen sein. |
| Mikrofon: „No microphone available“ | Standard-Mikrofon in den Windows-Sound-Einstellungen prüfen. |
| Mikrofon ohne Ergebnis | whisper nicht gefunden, siehe [Spracheingabe](#spracheingabe-optional). |
| Fenster in Aufnahme sichtbar | Windows älter als 10 Version 2004. Mit `winver` prüfen. |

## Fehlersuche: Diagnose und Log

Weil die App unsichtbar ist, gibt es zwei Hilfen, um die Ursache eines Problems einzugrenzen.

**1. Diagnose in den Settings:** Ctrl+Shift+Space, Zahnrad, Button **Diagnose**. Es erscheint eine Liste mit `OK` oder `BAD` pro Zeile:

| Zeile | Bedeutet |
|---|---|
| Windows build | Mindestens 19041 nötig, sonst kein Capture-Schutz |
| .NET runtime | Installierte Laufzeit |
| WebView2 runtime | Version oder „not installed“ |
| Toggle hotkey | Ob das Tastenkürzel angemeldet werden konnte (BAD = ein anderes Programm belegt es) |
| Capture protection | `0x11` = Fenster ist für Aufnahmen unsichtbar |
| Backend / API key | Eingestellte URL, ob ein Key gesetzt ist (nur die Länge, nie der Key) |
| Backend reachable | Antwort des Servers, mit Hinweis auf die Ursache |
| whisper-cli.exe / model / Microphones | Zustand der Spracheingabe |

Den Text kannst du markieren, kopieren und weitergeben.

**2. Logdatei:** `%LOCALAPPDATA%\Ghostbar\ghostbar.log` (Windows-Explorer: Adresszeile, Pfad einfügen). Sie enthält Start, Hotkey, WebView2, Modellabruf, Fehler der API, Screenshot und Mikrofon. API-Keys und Chat-Inhalte werden nicht geloggt. Beispiele:

```
2026-10-21 20:50:01.123 hotkey Ctrl+Shift+Space registered=False   -> Kürzel belegt
2026-10-21 20:50:05.456 webview2 ready version=130.0.2849.80       -> Chat-Oberfläche läuft
2026-10-21 20:50:09.001 stream failed HTTP 401 - API key missing…  -> Key falsch
2026-10-21 20:50:12.777 models fetch failed backend=Ollama …       -> Server nicht erreichbar
```

**Fehlermeldungen im Chat und bei „Test“** nennen die Ursache direkt:

| Meldung | Ursache |
|---|---|
| `HTTP 401/403 - API key missing or rejected` | Key fehlt oder falsch |
| `HTTP 404 - Not found. Check the URL … and the model name` | URL ohne `/v1` oder falscher Modellname |
| `HTTP 429 - Rate limit or quota exceeded` | Limit oder Guthaben aufgebraucht |
| `HTTP 5xx - The server reported an internal error` | Problem beim Anbieter |
| `Connection refused at … Is the server running?` | Ollama/LM Studio nicht gestartet, falscher Port |
| `Host not found` | Tippfehler in der URL oder kein Internet |
| `Invalid URL` | URL unvollständig (`http://` fehlt) |
| `No answer … (timed out)` | Server antwortet 30 Sekunden lang nicht |
| `The backend answered but sent no text` | Antwort ohne Inhalt, meist falscher Modellname |

## Automatische Tests

Ich selbst habe kein Windows-Gerät, deshalb prüfen zwei Testarten die App, ohne dass du etwas installieren musst.

**Unit-Tests (`tests/Ghostbar.Tests`, 44 Tests):** Sie starten einen Fake-Server und prüfen die Kommunikation mit Ollama, OpenAI-kompatiblen Servern und Anthropic: Streaming, Header (`Authorization`, `x-api-key`), Systemprompt, Bild-Anhang, Modellliste, jede Fehlermeldung aus der Tabelle oben, Abbruch und Timeout. Sie laufen auch auf dem Mac/Linux:

```bash
dotnet test tests/Ghostbar.Tests
```

**Windows-Test auf GitHub (`.github/workflows/ci.yml`):** Bei jedem Push startet GitHub einen echten Windows-Rechner und führt aus:

1. Unit-Tests
2. Build
3. Smoke-Test (`scripts/smoke-test.ps1`): startet `Ghostbar.exe`, drückt per Tastatur Ctrl+Shift+Space und prüft, ob der Prozess läuft, nach dem Start kein Fenster sichtbar ist, der Hotkey ankommt, WebView2 startet, die Chat-Seite lädt, die Verbindung Oberfläche zu App funktioniert, das Fenster nicht in Taskleiste/Alt+Tab erscheint (`WS_EX_TOOLWINDOW`), der Capture-Schutz gesetzt ist und der zweite Hotkey das Fenster versteckt.

Ergebnis ansehen: GitHub, Reiter **Actions**, letzten Lauf öffnen. Die Tabelle steht oben in der Zusammenfassung, darunter als Download `ghostbar-ci` (fertiges ZIP, `ghostbar.log`, Screenshot).

Was die Tests **nicht** beweisen: Ob das Fenster in Zoom/Teams/OBS wirklich fehlt (der Test prüft nur, dass Windows das Capture-Flag gesetzt hat, und wertet einen Screenshot nur informativ aus), und wie sich echte API-Server, Mikrofon und whisper verhalten.

## Test-Checkliste

1. `Ghostbar.exe` starten: nichts sichtbar (kein Fenster, kein Taskleisten-/Tray-Symbol).
2. Ctrl+Shift+Space zeigt/versteckt den Chat; nicht in Alt+Tab.
3. Win+Shift+S, OBS, Teams/Zoom-Freigabe: Fenster nicht sichtbar.
4. Header ziehen, Ränder ziehen; Position bleibt nach Neustart.
5. Ollama oder API-Backend streamt Antworten, Modellliste lädt.
6. Screenshot-Hotkey hängt Bild an, Vision-Modell antwortet.
7. Settings, Quit beendet den Prozess.

## Unterschiede zum macOS-Original

- Hotkeys nutzen Ctrl+Shift statt ⌘⌥ (Ctrl+Alt ist unter Windows AltGr).
- Hotkeys brauchen immer eine Taste; Modifier-only-Kürzel gibt es nicht.
- Modellauswahl erfolgt im Chat-Header; sie wird nicht über Neustarts gespeichert.

## Lizenz

MIT, Original © rbc33.

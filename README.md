# Ghostbar for Windows

**English** | [Deutsch](README.de.md)

[![Release](https://img.shields.io/github/v/release/nik230395/ghostbar-windows)](../../releases)
[![CI](https://img.shields.io/github/actions/workflow/status/nik230395/ghostbar-windows/ci.yml?label=CI)](../../actions)
[![License: MIT](https://img.shields.io/github/license/nik230395/ghostbar-windows)](LICENSE)
![Platform: Windows 10 (2004+) / 11](https://img.shields.io/badge/platform-Windows%2010%20%282004%2B%29%20%7C%2011-0078D6)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![UI: WinForms + WebView2](https://img.shields.io/badge/UI-WinForms%20%2B%20WebView2-informational)

Windows port of [rbc33/Ghostbar](https://github.com/rbc33/Ghostbar): an AI chat that is invisible to screen sharing and recordings.
C# / .NET 10 (WinForms) + WebView2. The chat interface is the original's HTML.

## Contents

1. [How it works](#how-it-works)
2. [Installation](#installation)
3. [First start](#first-start)
4. [Connecting an API](#connecting-an-api)
5. [Usage](#usage)
6. [Autostart](#autostart)
7. [Voice input (optional)](#voice-input-optional)
8. [Update, quit, uninstall](#update-quit-uninstall)
9. [Troubleshooting](#troubleshooting)
10. [Test checklist](#test-checklist)

## How it works

- There is **no installer**. Ghostbar is a folder containing `Ghostbar.exe`. Put it anywhere and run it.
- After launch **nothing appears**: no window, no taskbar button, no tray icon, no Alt+Tab entry (`WS_EX_TOOLWINDOW`).
- The window is excluded from capture (`SetWindowDisplayAffinity` / `WDA_EXCLUDEFROMCAPTURE`, Windows 10 version 2004 or later).
- It is controlled only through global hotkeys. `Ghostbar.exe` still shows up in Task Manager (Details tab), and that cannot be prevented.

## Installation

### Step 1: Check the requirements

| What | Why | Check / install |
|---|---|---|
| Windows 10 version 2004 (build 19041) or later, or Windows 11, 64-bit | Capture protection | `Win+R`, `winver` |
| **.NET 10 Desktop Runtime (x64)** | Run the program | `winget install Microsoft.DotNet.DesktopRuntime.10` or [download](https://dotnet.microsoft.com/download/dotnet/10.0) (".NET Desktop Runtime", x64) |
| **WebView2 Runtime** | Chat interface | Usually preinstalled on Windows 10/11. Otherwise: `winget install Microsoft.EdgeWebView2Runtime` |

Check that the .NET runtime is present (PowerShell):

```powershell
dotnet --list-runtimes
```

A line `Microsoft.WindowsDesktop.App 10.x.x` must appear. Without it, `Ghostbar.exe` starts silently and shows nothing.

### Step 2: Get Ghostbar (pick one option)

#### Option A: prebuilt package (easiest)

1. Download `Ghostbar-win-x64.zip` from the [Releases](../../releases) page.
2. Right-click the ZIP, **Properties**, tick **Unblock** at the bottom (if present), **OK**.
3. Extract the ZIP, e.g. to `C:\Tools\Ghostbar\`. Keep the **whole folder** together (`WebView2Loader.dll` sits next to `Ghostbar.exe`).

If there is no release yet, use option B or C. You can create a release like this (see option C for the ZIP):

```bash
gh release create v1.0.0 Ghostbar-win-x64.zip --title "Ghostbar 1.0.0" --notes "First Windows version"
```

#### Option B: build it yourself on Windows

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`winget install Microsoft.DotNet.SDK.10`).
2. Get the repo and build (PowerShell):

```powershell
git clone https://github.com/nik230395/ghostbar-windows.git
cd ghostbar-windows
./build.ps1
```

If PowerShell blocks the script: `powershell -ExecutionPolicy Bypass -File build.ps1`.

3. The result is the `publish\` folder containing `Ghostbar.exe`. Copy the whole folder to a permanent location, e.g. `C:\Tools\Ghostbar\`.

#### Option C: build on macOS/Linux and copy to Windows

```bash
git clone https://github.com/nik230395/ghostbar-windows.git
cd ghostbar-windows
./build.sh
cd publish && zip -r ../Ghostbar-win-x64.zip . && cd ..
```

Move `Ghostbar-win-x64.zip` to the Windows machine (USB, cloud, mail) and extract it as in option A.

### Step 3: Windows warning (SmartScreen)

The app is not signed. On first launch you may see "Windows protected your PC". Click **More info**, then **Run anyway**. This does not happen if you built it yourself (option B).

## First start

1. Double-click `Ghostbar.exe`. **Nothing visible happens, and that is correct.**
2. Press **Ctrl+Shift+Space** and the chat opens.
3. If nothing opens, see [Troubleshooting](#troubleshooting).
4. Open the gear icon at the top of the chat and set up a backend (next section).

## Connecting an API

The steps are the same for every backend: **Ctrl+Shift+Space**, gear icon, choose a backend (the URL fills in by itself), paste the key, **Test**, **Save**. The model list then loads automatically, and you pick the model in the chat header.

| Backend | Key required | Note |
|---|---|---|
| Ollama | no | Local, `http://localhost:11434` |
| OpenAI | yes | |
| Anthropic | yes | |
| Google Gemini | yes | Free key in AI Studio |
| OpenRouter | yes | |
| NVIDIA NIM | yes | Free tier available |
| llama.cpp, LM Studio, vLLM | no | Local servers |

Any OpenAI-compatible server works: choose the **OpenAI** backend and change the URL.

### Example: Gemini (free key)

1. Sign in at <https://aistudio.google.com/api-keys> and choose **Create API key** (name it e.g. `ghostbar`, default project). Copy the key.
2. **Ctrl+Shift+Space**, gear icon.
3. Choose the **Google Gemini** backend, paste the key, **Test** (must show "✓ Connected"), **Save**.
4. Pick a model in the chat header, e.g. one with `flash` in its name.

Gemini runs through the OpenAI-compatible endpoint `https://generativelanguage.googleapis.com/v1beta/openai`.

The key is stored encrypted with Windows DPAPI in `%APPDATA%\Ghostbar\settings.json` and can only be read by your Windows user.

## Usage

| Action | Default (changeable in Settings) |
|---|---|
| Show/hide window (global) | Ctrl+Shift+Space |
| Microphone (in the chat) | Ctrl+Shift+M |
| Attach screenshot (in the chat) | Ctrl+Shift+S |
| Send message | Enter |
| New line | Shift+Enter |

- Move: drag the header (top bar). Resize: drag the edges. Position and size are remembered.
- Gear icon: Settings. **Quit Ghostbar** is there too.
- The × in the chat only hides the window, the app keeps running.

## Autostart

To have Ghostbar ready after every login:

1. `Win+R`, `shell:startup`, Enter. The Startup folder opens.
2. Right-click `Ghostbar.exe`, **Create shortcut**, and move the shortcut into the Startup folder.

To remove it, delete the shortcut there.

## Voice input (optional)

Runs entirely locally through [whisper.cpp](https://github.com/ggml-org/whisper.cpp).

1. Download the Windows x64 ZIP from the [whisper.cpp releases](https://github.com/ggml-org/whisper.cpp/releases). You need `whisper-cli.exe` (plus the bundled DLLs).
2. Download a model, e.g. `ggml-base.bin` (about 150 MB) from <https://huggingface.co/ggerganov/whisper.cpp>.
3. Put everything in one folder: `%LOCALAPPDATA%\Ghostbar\whisper\` (alternatively next to `Ghostbar.exe` or on the PATH).
4. In the chat press **Ctrl+Shift+M**, speak, press it again. The text is transcribed and sent.

## Update, quit, uninstall

- **Quit:** Settings (gear icon), **Quit Ghostbar**. If needed, end `Ghostbar.exe` in Task Manager (Details tab).
- **Update:** Quit Ghostbar and replace the folder with the new version. Your settings are kept, because they are not stored in the program folder.
- **Uninstall:** Quit Ghostbar, delete the program folder, delete the autostart shortcut. Optionally also delete `%APPDATA%\Ghostbar` (settings) and `%LOCALAPPDATA%\Ghostbar` (WebView data, Whisper).

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Hotkey does nothing | Is the app running? Task Manager, Details, `Ghostbar.exe`. If it is there, another program is using the shortcut. Set a different shortcut in Settings. |
| `Ghostbar.exe` does not start, no message | .NET 10 Desktop Runtime is missing: check `dotnet --list-runtimes`. |
| Chat stays empty or white | WebView2 Runtime is missing or too old: `winget install Microsoft.EdgeWebView2Runtime`. |
| Test shows "HTTP 401" | Wrong key, or a key for another backend. |
| Test shows a connection error | The server is not running (start Ollama/LM Studio) or the URL is wrong. |
| Model list empty ("no models") | Run Test first, then Save. With Ollama, at least one model must be available. |
| Microphone: "No microphone available" | Check the default microphone in the Windows sound settings. |
| Microphone gives no result | whisper not found, see [Voice input](#voice-input-optional). |
| Window visible in recordings | Windows is older than 10 version 2004. Check with `winver`. |

## Troubleshooting: diagnostics and log

Because the app is invisible, there are two aids for narrowing down the cause of a problem.

**1. Diagnostics in Settings:** Ctrl+Shift+Space, gear icon, **Diagnose** button. A list appears with `OK` or `BAD` per line:

| Line | Meaning |
|---|---|
| Windows build | At least 19041 is required, otherwise there is no capture protection |
| .NET runtime | Installed runtime |
| WebView2 runtime | Version or "not installed" |
| Toggle hotkey | Whether the shortcut could be registered (BAD = another program is using it) |
| Capture protection | `0x11` = the window is invisible to recordings |
| Backend / API key | Configured URL, and whether a key is set (only its length, never the key) |
| Backend reachable | The server's response, with a hint about the cause |
| whisper-cli.exe / model / Microphones | State of voice input |

You can select, copy and share the text.

**2. Log file:** `%LOCALAPPDATA%\Ghostbar\ghostbar.log` (Windows Explorer: address bar, paste the path). It contains startup, hotkey, WebView2, model fetch, API errors, screenshot and microphone. API keys and chat contents are not logged. Examples:

```
2026-10-21 20:50:01.123 hotkey Ctrl+Shift+Space registered=False   -> shortcut in use
2026-10-21 20:50:05.456 webview2 ready version=130.0.2849.80       -> chat interface running
2026-10-21 20:50:09.001 stream failed HTTP 401 - API key missing…  -> wrong key
2026-10-21 20:50:12.777 models fetch failed backend=Ollama …       -> server not reachable
```

**Error messages in the chat and in "Test"** name the cause directly:

| Message | Cause |
|---|---|
| `HTTP 401/403 - API key missing or rejected` | Key missing or wrong |
| `HTTP 404 - Not found. Check the URL … and the model name` | URL without `/v1`, or wrong model name |
| `HTTP 429 - Rate limit or quota exceeded` | Limit or credit used up |
| `HTTP 5xx - The server reported an internal error` | Problem on the provider's side |
| `Connection refused at … Is the server running?` | Ollama/LM Studio not started, wrong port |
| `Host not found` | Typo in the URL, or no internet |
| `Invalid URL` | Incomplete URL (`http://` missing) |
| `No answer … (timed out)` | The server does not answer for 30 seconds |
| `The backend answered but sent no text` | Empty response, usually a wrong model name |

## Automated tests

I do not have a Windows machine myself, so two kinds of tests check the app without you having to install anything.

**Unit tests (`tests/Ghostbar.Tests`, 44 tests):** They start a fake server and check communication with Ollama, OpenAI-compatible servers and Anthropic: streaming, headers (`Authorization`, `x-api-key`), system prompt, image attachment, model list, every error message from the table above, cancellation and timeout. They also run on Mac/Linux:

```bash
dotnet test tests/Ghostbar.Tests
```

**Windows test on GitHub (`.github/workflows/ci.yml`):** On every push GitHub starts a real Windows machine and runs:

1. Unit tests
2. Build
3. Smoke test (`scripts/smoke-test.ps1`): starts `Ghostbar.exe`, presses Ctrl+Shift+Space on the keyboard and checks that the process is running, that no window is visible after startup, that the hotkey arrives, that WebView2 starts, that the chat page loads, that the connection from interface to app works, that the window does not appear in the taskbar/Alt+Tab (`WS_EX_TOOLWINDOW`), that capture protection is set, and that the second hotkey hides the window.

To view the result: GitHub, **Actions** tab, open the latest run. The table is at the top of the summary, and below it is the download `ghostbar-ci` (finished ZIP, `ghostbar.log`, screenshot).

What the tests do **not** prove: whether the window is really missing in Zoom/Teams/OBS (the test only checks that Windows has set the capture flag, and evaluates a screenshot for information only), and how real API servers, the microphone and whisper behave.

## Test checklist

1. Start `Ghostbar.exe`: nothing visible (no window, no taskbar/tray icon).
2. Ctrl+Shift+Space shows/hides the chat; not in Alt+Tab.
3. Win+Shift+S, OBS, Teams/Zoom sharing: window not visible.
4. Drag the header, drag the edges; position is kept after a restart.
5. Ollama or API backend streams answers, model list loads.
6. Screenshot hotkey attaches an image, a vision model answers.
7. Settings, Quit ends the process.

## Differences from the macOS original

- Hotkeys use Ctrl+Shift instead of ⌘⌥ (Ctrl+Alt is AltGr on Windows).
- Hotkeys always need a key; modifier-only shortcuts do not exist.
- The model is chosen in the chat header and is not remembered across restarts.

## License

MIT, original © rbc33.

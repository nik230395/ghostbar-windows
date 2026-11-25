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
5. [Automatic model selection](#automatic-model-selection)
6. [Project folder: the AI reads and edits files](#project-folder-the-ai-reads-and-edits-files)
7. [Usage](#usage)
8. [Autostart](#autostart)
9. [Voice input (optional)](#voice-input-optional)
10. [Update, quit, uninstall](#update-quit-uninstall)
11. [Troubleshooting](#troubleshooting)
12. [Test checklist](#test-checklist)

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
3. Extract the ZIP, e.g. to `C:\Tools\Ghostbar\`. From v1.1.1 on it contains only `Ghostbar.exe`; everything it needs is inside.

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

3. The result is `publish\Ghostbar.exe`, a single file. Copy it to a permanent location, e.g. `C:\Tools\Ghostbar\`.

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

## Automatic model selection

The model menu at the top left starts with **Auto** (the default). If a model fails before any text arrives, Ghostbar switches to the next model and shows a yellow line in the chat, e.g. `↪ gemini-2.5-flash: rate limit, switching to gemini-2.5-flash-lite`. The answer's label shows which model actually replied.

**Order:** Flash models first (newest first), then Flash-Lite, then Pro, then the rest. Embedding, image, video, speech and live models are skipped. In Settings under **Model order for Auto** you can change the order with ▲/▼ and uncheck models. **Automatic order** resets it. If you pick a specific model in the chat, it is tried first and the others act as backups.

| Error | Behaviour |
|---|---|
| 429 quota/rate limit | Next model. The failed one pauses for 1 minute, or until midnight Pacific time if the daily quota is used up (Google resets it then) |
| 500, 503 | Next model, the failed one pauses for 30 seconds |
| 400, 403 | Next model, the failed one pauses for 10 minutes |
| 404 | Next model, the failed one pauses for 12 hours |
| Empty answer | Next model |
| 401 | **No switch.** The key is invalid, which applies to every model. Check the key in Settings. |
| Server unreachable | No switch, error message |

- Each model is tried at most once per message. If all fail, the chat shows `All N models failed.` plus the last error.
- If the error comes in the middle of an answer, Ghostbar does not switch (that would mix half sentences from two models). Use **↺ retry**.
- Long pauses (1 hour or more, e.g. the daily quota) survive a restart.
- Google counts limits per project and differently per model. Switching helps with per-minute and per-model limits, not with the project-wide spend limit.

## Project folder: the AI reads and edits files

Ghostbar can give the AI a project folder as context and let it write code there, e.g. to extend a starter project.

**Choosing the folder:** In the chat header click **📁 no project**, then **Choose folder…**, or use **Project folder** in Settings. Recently used folders appear in the same menu. **No project** turns it off.

> The Windows "Select folder" dialog **is visible** during screen sharing; only Ghostbar itself is hidden. Pick the folder beforehand or paste the path in Settings.

**What the AI gets:** With every message the folder structure (top levels) and the start of the README. It fetches the rest itself with these tools:

| Tool | What it does | Your approval |
|---|---|---|
| `list_dir` | List folders (skips `.git`, `node_modules`, `bin`, `obj`, `dist` …) | no |
| `read_file` | Read a text file, up to 800 lines per call | no |
| `search` | Search text in files, up to 100 matches | no |
| `edit_file` | Replace one unique piece of text | **yes** |
| `write_file` | Create a file or replace it completely | **yes** |

Each step shows up in the chat as a grey line (`📄 reads src/App.cs`). Every change appears as a card with before/after (red/green) and the buttons **Apply** and **Reject**. Nothing is written until you click **Apply**. If you reject, the AI is told. ⏹ stops and rejects open changes.

**Limits and protection:**
- Only inside the chosen folder. `../`, absolute paths to the outside and links are refused; nothing is written into `.git`.
- No deleting, no commands or programs, no internet.
- At most 15 tool steps per message.
- The tools work with Gemini and other OpenAI-compatible providers (OpenAI, OpenRouter, LM Studio …). With Ollama and Anthropic the AI only gets the folder structure and README as context.
- File contents cost tokens. On the free Gemini tier you hit the limit sooner; automatic model selection partly absorbs that.
- The AI only sees its last answer, not the files it read for the previous message. For a follow-up question it reads them again if needed.

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
| Chat stays empty, white or black | From v1.1.1 on a message shows the cause. Usually the WebView2 Runtime is missing or too old: `winget install Microsoft.EdgeWebView2Runtime`. In v1.1.0 and older, `WebView2Loader.dll` was missing next to the exe; update to v1.1.1. |
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

**Unit tests (`tests/Ghostbar.Tests`, 105 tests):** They start a fake server and check:
- Communication with Ollama, OpenAI-compatible servers and Anthropic: streaming, headers (`Authorization`, `x-api-key`), system prompt, image attachment, model list, every error message from the table above, cancellation and timeout.
- Automatic model selection: switching on 429/500/503/404/400/403 and empty answers, no switch on 401 or on errors mid-answer, pauses, daily quota until midnight Pacific, ordering.
- Tools: tool calls streamed in pieces (including Gemini's `thought_signature`), sending results back, stopping after 15 steps, approving/rejecting changes.
- Protection: `../`, absolute paths, links to the outside, `.git`, binary files, ambiguous text, Windows line endings, UTF-8 BOM.

They also run on Mac/Linux:

```bash
dotnet test tests/Ghostbar.Tests
```

**Windows test on GitHub (`.github/workflows/ci.yml`):** On every push GitHub starts a real Windows machine and runs:

1. Unit tests
2. Build
3. Smoke test (`scripts/smoke-test.ps1`): starts `Ghostbar.exe`, presses Ctrl+Shift+Space on the keyboard and checks that the process is running, that no window is visible after startup, that the hotkey arrives, that WebView2 starts, that the chat page loads, that the connection from interface to app works, that the window does not appear in the taskbar/Alt+Tab (`WS_EX_TOOLWINDOW`), that capture protection is set, and that the second hotkey hides the window.

To view the result: GitHub, **Actions** tab, open the latest run. The table is at the top of the summary, and below it is the download `ghostbar-ci` (finished ZIP, `ghostbar.log`, screenshot).

What the tests do **not** prove: whether the window is really missing in Zoom/Teams/OBS (the test only checks that Windows has set the capture flag, and evaluates a screenshot for information only), and how real API servers, the microphone and whisper behave. In particular, it is not proven that Gemini streams tool calls exactly like the fake server does; Google's documentation does not describe it.

## Test checklist

1. Start `Ghostbar.exe`: nothing visible (no window, no taskbar/tray icon).
2. Ctrl+Shift+Space shows/hides the chat; not in Alt+Tab.
3. Win+Shift+S, OBS, Teams/Zoom sharing: window not visible.
4. Drag the header, drag the edges; position is kept after a restart.
5. Ollama or API backend streams answers, model list loads.
6. Screenshot hotkey attaches an image, a vision model answers.
7. Settings, Quit ends the process.
8. Model on **Auto**: on a 429 a yellow switch line appears and another model answers.
9. Choose a project folder, ask "What does this project do?": grey `reads` lines appear and the answer fits the project.
10. "Add a file NOTES.md with …": a change card appears, **Reject** writes nothing, **Apply** creates the file.

## Differences from the macOS original

- Hotkeys use Ctrl+Shift instead of ⌘⌥ (Ctrl+Alt is AltGr on Windows).
- Hotkeys always need a key; modifier-only shortcuts do not exist.
- The model is chosen in the chat header and is not remembered across restarts.

## License

MIT, original © rbc33.

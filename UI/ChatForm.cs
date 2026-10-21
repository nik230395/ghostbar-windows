using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ghostbar.App;
using Ghostbar.Backend;
using Ghostbar.Config;
using Ghostbar.Native;
using Ghostbar.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Ghostbar.UI;

class ChatForm : StealthForm
{
    private const int ResizeGrip = 6;

    private const string BridgeScript = """
        window.webkit = { messageHandlers: new Proxy({}, {
          get: (_, name) => ({ postMessage: body => window.chrome.webview.postMessage({ name, body }) })
        })};
        document.addEventListener('mousedown', e => {
          if (e.button !== 0 || !e.target.closest('#header')) return;
          if (e.target.closest('button, select, input, a')) return;
          window.chrome.webview.postMessage({ name: 'startDrag' });
        }, true);
        """;

    private readonly GhostbarContext context;
    private readonly WebView2 web = new() { DefaultBackgroundColor = Background, Dock = DockStyle.Fill };
    private readonly AudioRecorder recorder = new();
    private CancellationTokenSource? streamCts;
    private string? storedScreenshot;
    private bool isRecording;

    public ChatForm(GhostbarContext context)
    {
        this.context = context;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(800, 620);
        MinimumSize = new Size(360, 300);
        Padding = new Padding(1);
        LoadBounds();
        Controls.Add(web);
        Load += async (_, _) => await InitWebViewAsync();
        ResizeEnd += (_, _) => SaveBounds();
        FormClosing += (e, a) => { if (a.CloseReason == CloseReason.UserClosing) { a.Cancel = true; Hide(); } };
    }

    private async Task InitWebViewAsync()
    {
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ghostbar", "webview");
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: dataDir);
        await web.EnsureCoreWebView2Async(env);
        var core = web.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(BridgeScript);
        core.WebMessageReceived += OnWebMessage;
        core.NavigateToString(LoadHtml());
    }

    private static string LoadHtml()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("chat.html")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void LoadBounds()
    {
        var saved = Settings.Current.WindowBounds;
        if (saved is { Width: > 100, Height: > 100 } r && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(r)))
            Bounds = r;
        else
        {
            var area = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        }
    }

    private void SaveBounds()
    {
        Settings.Current.WindowBounds = Bounds;
        Settings.Current.Save();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32.WM_NCHITTEST)
        {
            var p = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF)));
            bool left = p.X < ResizeGrip, right = p.X >= Width - ResizeGrip;
            bool top = p.Y < ResizeGrip, bottom = p.Y >= Height - ResizeGrip;
            int hit = (left, right, top, bottom) switch
            {
                (true, _, true, _) => Win32.HTTOPLEFT,
                (_, true, true, _) => Win32.HTTOPRIGHT,
                (true, _, _, true) => Win32.HTBOTTOMLEFT,
                (_, true, _, true) => Win32.HTBOTTOMRIGHT,
                (true, _, _, _) => Win32.HTLEFT,
                (_, true, _, _) => Win32.HTRIGHT,
                (_, _, true, _) => Win32.HTTOP,
                (_, _, _, true) => Win32.HTBOTTOM,
                _ => 0,
            };
            if (hit != 0) { m.Result = hit; return; }
        }
        base.WndProc(ref m);
    }

    private void Script(string js)
    {
        if (IsDisposed) return;
        BeginInvoke(() => web.CoreWebView2?.ExecuteScriptAsync(js));
    }

    private static string Js(string s) => JsonSerializer.Serialize(s);

    public void PushSettings()
    {
        Script($"setSavedModel({Js(Settings.Current.SelectedModel)})");
        PushShortcuts();
        PushScreenshotPrompt();
        FetchModels();
    }

    private void PushShortcuts()
    {
        Script($"setShortcut({Settings.Current.Mic.ToJs()})");
        Script($"setCaptureShortcut({Settings.Current.Capture.ToJs()})");
    }

    private void PushScreenshotPrompt()
    {
        var p = Settings.Current.ScreenshotPrompt;
        Script(p.Length == 0 ? "setScreenshotPrompt(null)" : $"setScreenshotPrompt({Js(p)})");
    }

    private async void FetchModels()
    {
        var cfg = Settings.Current;
        try
        {
            var names = await BackendClient.FetchModelsAsync(cfg);
            var kind = cfg.BackendType == BackendType.Ollama ? "ollama" : "openai";
            Script($"receiveModels({JsonSerializer.Serialize(names)},'{kind}')");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Script("receiveModels([],'" + (cfg.BackendType == BackendType.Ollama ? "ollama" : "openai") + "')");
        }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var msg = JsonNode.Parse(e.WebMessageAsJson);
        var body = msg?["body"];
        switch (msg?["name"]?.GetValue<string>())
        {
            case "sendMessage": SendMessage(body); break;
            case "loadModels": FetchModels(); break;
            case "initModel":
                Script($"setSavedModel({Js(Settings.Current.SelectedModel)})");
                PushShortcuts();
                PushScreenshotPrompt();
                break;
            case "checkWhisper":
                Script($"onWhisperStatus({(Transcriber.FindBinary() != null).ToString().ToLower()},{(Transcriber.FindModel() != null).ToString().ToLower()})");
                break;
            case "captureScreen": CaptureScreen(); break;
            case "clearScreenshot": storedScreenshot = null; break;
            case "startRecording": StartRecording(); break;
            case "stopRecording": StopRecording(); break;
            case "stopStream":
                streamCts?.Cancel();
                streamCts = null;
                Script("endStream()");
                break;
            case "closeWindow": BeginInvoke(Hide); break;
            case "openSettings": BeginInvoke(context.ShowSettings); break;
            case "startDrag":
                Win32.ReleaseCapture();
                Win32.SendMessage(Handle, Win32.WM_NCLBUTTONDOWN, Win32.HTCAPTION, IntPtr.Zero);
                break;
        }
    }

    private void SendMessage(JsonNode? body)
    {
        if (body?["messages"] is not JsonArray messages || body["model"]?.GetValue<string>() is not { } model) return;
        var useScreenshot = body["screenshot"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
        var image = useScreenshot ? storedScreenshot : null;

        streamCts?.Cancel();
        var cts = streamCts = new CancellationTokenSource();
        _ = BackendClient.StreamAsync(messages, model, image,
            token => Script($"appendToken({Js(token)})"),
            isError => Script($"endStream({(isError ? "true" : "false")})"),
            cts.Token);
    }

    private void CaptureScreen()
    {
        Task.Run(() =>
        {
            storedScreenshot = ScreenCapture.CaptureBase64();
            Script(storedScreenshot != null ? "onScreenshotCaptured()" : "onScreenshotError()");
        });
    }

    private void StartRecording()
    {
        if (isRecording) return;
        try
        {
            recorder.Start();
            isRecording = true;
            Script("onRecordingStarted()");
        }
        catch (Exception e) when (e is NAudio.MmException or InvalidOperationException)
        {
            Script($"onMicError({Js("No microphone available.")})");
        }
    }

    private void StopRecording()
    {
        if (!isRecording) return;
        isRecording = false;
        recorder.OnDone = async path =>
        {
            if (path == null) { Script("onTranscription(null)"); return; }
            Script("onTranscribing()");
            var text = await Transcriber.TranscribeAsync(path);
            Script(text == null ? "onTranscription(null)" : $"onTranscription({Js(text)})");
        };
        recorder.Stop();
    }
}

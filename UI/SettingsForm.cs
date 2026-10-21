using Ghostbar.App;
using Ghostbar.Backend;
using Ghostbar.Config;

namespace Ghostbar.UI;

class SettingsForm : StealthForm
{
    private const string DefaultScreenshotPrompt =
        "Look at this screenshot. Find any programming problem, error, or bug and explain how to fix it.";

    private record Preset(string Label, BackendType Type, string Url, string Hint);

    private static readonly Preset[] Presets =
    [
        new("Ollama", BackendType.Ollama, "http://localhost:11434", "Local Ollama instance"),
        new("OpenAI", BackendType.OpenAI, "https://api.openai.com/v1", "Requires API key"),
        new("Anthropic", BackendType.Anthropic, "https://api.anthropic.com/v1", "Requires API key"),
        new("Google Gemini", BackendType.OpenAI, "https://generativelanguage.googleapis.com/v1beta/openai", "Requires API key (Google AI Studio)"),
        new("OpenRouter", BackendType.OpenRouter, "https://openrouter.ai/api/v1", "Requires API key"),
        new("NVIDIA NIM", BackendType.OpenAI, "https://integrate.api.nvidia.com/v1", "Requires API key"),
        new("llama.cpp", BackendType.OpenAI, "http://localhost:8080/v1", "Local llama.cpp server"),
        new("LM Studio", BackendType.OpenAI, "http://localhost:1234/v1", "Local LM Studio server"),
        new("vLLM", BackendType.OpenAI, "http://localhost:8000/v1", "Local vLLM server"),
    ];

    private readonly GhostbarContext context;
    private readonly ListBox backendList = new() { Height = 96, Width = 220 };
    private readonly TextBox urlBox = new() { Width = 420, Font = new Font("Consolas", 9) };
    private readonly Label hint = new() { AutoSize = true, ForeColor = Color.Gray };
    private readonly TextBox keyBox = new() { Width = 420, UseSystemPasswordChar = true };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.Gray };
    private readonly ShortcutBox toggleBox;
    private readonly ShortcutBox micBox;
    private readonly ShortcutBox captureBox;
    private readonly TextBox systemBox = Multiline();
    private readonly TextBox screenshotBox = Multiline();

    public SettingsForm(GhostbarContext context)
    {
        this.context = context;
        var cfg = Settings.Current;

        Text = "Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ForeColor = Color.White;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        toggleBox = new ShortcutBox(cfg.Toggle);
        micBox = new ShortcutBox(cfg.Mic);
        captureBox = new ShortcutBox(cfg.Capture);

        backendList.Items.AddRange(Presets.Select(p => (object)p.Label).ToArray());
        var selected = Array.FindIndex(Presets, p => p.Url == cfg.BackendUrl && p.Type == cfg.BackendType);
        if (selected < 0) selected = Math.Max(0, Array.FindIndex(Presets, p => p.Type == cfg.BackendType));
        backendList.SelectedIndex = selected;
        urlBox.Text = cfg.BackendUrl;
        keyBox.Text = cfg.ApiKey;
        systemBox.Text = cfg.SystemPrompt;
        screenshotBox.Text = cfg.ScreenshotPrompt;
        hint.Text = Presets[selected].Hint;
        backendList.SelectedIndexChanged += (_, _) =>
        {
            var p = Presets[backendList.SelectedIndex];
            urlBox.Text = p.Url;
            hint.Text = p.Hint;
        };

        var testButton = new Button { Text = "Test", AutoSize = true };
        testButton.Click += async (_, _) => await RunTestAsync();
        var defaultButton = new Button { Text = "Default", AutoSize = true };
        defaultButton.Click += (_, _) => screenshotBox.Text = DefaultScreenshotPrompt;

        var save = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var quit = new Button { Text = "Quit Ghostbar", AutoSize = true, ForeColor = Color.Salmon };
        save.Click += (_, _) => Save();
        quit.Click += (_, _) => context.Quit();
        AcceptButton = save;
        CancelButton = cancel;

        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Dock = DockStyle.Fill,
        };
        layout.Controls.AddRange([
            Caption("Backend"), backendList, urlBox, hint,
            Caption("API key"), keyBox,
            Row(testButton, status),
            Caption("Toggle window (global)"), toggleBox,
            Caption("Mic (in chat)"), micBox,
            Caption("Screenshot (in chat)"), captureBox,
            Caption("System prompt"), systemBox,
            Row(Caption("Screenshot prompt"), defaultButton), screenshotBox,
            Row(quit, new Label { Width = 120 }, cancel, save),
        ]);
        Controls.Add(layout);

        foreach (var box in new[] { urlBox, keyBox, systemBox, screenshotBox, toggleBox, micBox, captureBox })
        {
            box.BackColor = Color.FromArgb(30, 32, 38);
            box.ForeColor = Color.White;
            box.BorderStyle = BorderStyle.FixedSingle;
        }
        backendList.BackColor = Color.FromArgb(30, 32, 38);
        backendList.ForeColor = Color.White;
        foreach (var l in layout.Controls.OfType<Label>()) l.ForeColor = l == hint ? Color.Gray : Color.White;
    }

    private static TextBox Multiline() => new()
    {
        Multiline = true,
        Width = 420,
        Height = 52,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 9),
    };

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 10, 3, 2) };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.AddRange(controls);
        return row;
    }

    private async Task RunTestAsync()
    {
        status.ForeColor = Color.Gray;
        status.Text = "Testing…";
        var result = await BackendClient.TestAsync(
            Presets[backendList.SelectedIndex].Type, urlBox.Text.Trim(), keyBox.Text.Trim());
        status.ForeColor = result == "ok" ? Color.LightGreen : Color.Salmon;
        status.Text = result == "ok" ? "✓ Connected" : $"✗ {result}";
    }

    private void Save()
    {
        var preset = Presets[backendList.SelectedIndex];
        var cfg = Settings.Current;
        cfg.BackendType = preset.Type;
        cfg.BackendUrl = urlBox.Text.Trim().Length == 0 ? preset.Url : urlBox.Text.Trim();
        cfg.ApiKey = keyBox.Text.Trim();
        cfg.SystemPrompt = systemBox.Text.Trim();
        cfg.ScreenshotPrompt = screenshotBox.Text.Trim();
        cfg.Toggle = toggleBox.Value;
        cfg.Mic = micBox.Value;
        cfg.Capture = captureBox.Value;
        cfg.Save();
        context.ApplySettings();
    }
}

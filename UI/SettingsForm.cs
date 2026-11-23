using Ghostbar.App;
using Ghostbar.Backend;
using Ghostbar.Config;
using Ghostbar.Services;

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
    private readonly TextBox report = new()
    {
        Multiline = true, ReadOnly = true, Width = 420, Height = 150, ScrollBars = ScrollBars.Both,
        WordWrap = false, Font = new Font("Consolas", 8),
    };
    private readonly TextBox projectBox = new() { Width = 300, Font = new Font("Consolas", 9) };
    private readonly Label projectStatus = new() { AutoSize = true, ForeColor = Color.Gray };
    private readonly CheckedListBox modelList = new() { Width = 300, Height = 220, CheckOnClick = true };
    private bool orderChanged;

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
        projectBox.Text = cfg.ProjectFolder;
        projectStatus.Text = "The AI can read this folder; every change needs your approval.";
        FillModelList(cfg.ModelOrder, cfg.DisabledModels);

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

        var diagnose = new Button { Text = "Diagnose", AutoSize = true };
        diagnose.Click += async (_, _) => await RunDiagnosticsAsync();
        var save = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var quit = new Button { Text = "Quit Ghostbar", AutoSize = true, ForeColor = Color.Salmon };
        save.Click += (_, _) => { if (!Save()) DialogResult = DialogResult.None; };
        quit.Click += (_, _) => context.Quit();
        AcceptButton = save;
        CancelButton = cancel;

        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) => BrowseProject();
        var clearProject = new Button { Text = "None", AutoSize = true };
        clearProject.Click += (_, _) => projectBox.Text = "";
        var up = new Button { Text = "▲", AutoSize = true };
        up.Click += (_, _) => MoveModel(-1);
        var down = new Button { Text = "▼", AutoSize = true };
        down.Click += (_, _) => MoveModel(1);
        var reset = new Button { Text = "Automatic order", AutoSize = true };
        reset.Click += (_, _) => { orderChanged = true; FillModelList([], []); };

        var left = Column(
            Caption("Backend"), backendList, urlBox, hint,
            Caption("API key"), keyBox,
            Row(testButton, status),
            Caption("Toggle window (global)"), toggleBox,
            Caption("Mic (in chat)"), micBox,
            Caption("Screenshot (in chat)"), captureBox,
            Caption("System prompt"), systemBox,
            Row(Caption("Screenshot prompt"), defaultButton), screenshotBox);
        var right = Column(
            Caption("Project folder"), projectBox, Row(browse, clearProject), projectStatus,
            Caption("Model order for Auto (top first, uncheck to skip)"), modelList, Row(up, down, reset),
            Row(diagnose, new Label { Text = "Run checks, copy the result when asking for help", AutoSize = true, ForeColor = Color.Gray }),
            report);
        report.Width = 300;

        var layout = Column(Row(left, right), Row(quit, new Label { Width = 360 }, cancel, save));
        Controls.Add(layout);

        foreach (var box in new[] { urlBox, keyBox, systemBox, screenshotBox, toggleBox, micBox, captureBox, report, projectBox })
        {
            box.BackColor = Color.FromArgb(30, 32, 38);
            box.ForeColor = Color.White;
            box.BorderStyle = BorderStyle.FixedSingle;
        }
        backendList.BackColor = Color.FromArgb(30, 32, 38);
        backendList.ForeColor = Color.White;
        modelList.BackColor = Color.FromArgb(30, 32, 38);
        modelList.ForeColor = Color.White;
        foreach (var l in Descendants(layout).OfType<Label>().Where(l => l.ForeColor == SystemColors.ControlText)) l.ForeColor = Color.White;
    }

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(c => Descendants(c).Prepend(c));

    private static FlowLayoutPanel Column(params Control[] controls)
    {
        var column = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        column.Controls.AddRange(controls);
        return column;
    }

    private void FillModelList(IReadOnlyList<string> userOrder, IReadOnlyCollection<string> disabled)
    {
        modelList.Items.Clear();
        foreach (var model in ModelRouter.Order(context.AvailableModels, userOrder, []))
            modelList.Items.Add(model, !disabled.Contains(model));
        if (modelList.Items.Count == 0) modelList.Items.Add("(no models loaded yet: save a backend and open the chat)", false);
    }

    private bool HasModels => context.AvailableModels.Count > 0;

    private void MoveModel(int direction)
    {
        var index = modelList.SelectedIndex;
        var target = index + direction;
        if (!HasModels || index < 0 || target < 0 || target >= modelList.Items.Count) return;
        var item = modelList.Items[index];
        var isChecked = modelList.GetItemChecked(index);
        modelList.Items.RemoveAt(index);
        modelList.Items.Insert(target, item);
        modelList.SetItemChecked(target, isChecked);
        modelList.SelectedIndex = target;
        orderChanged = true;
    }

    private void BrowseProject()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the project folder",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(projectBox.Text) ? projectBox.Text : "",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) projectBox.Text = dialog.SelectedPath;
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
        var options = new BackendOptions(
            Presets[backendList.SelectedIndex].Type, urlBox.Text.Trim(), keyBox.Text.Trim());
        var result = await BackendClient.TestAsync(options);
        Logger.Write($"settings test backend={options.Type} result={result.Split('\n')[0]}");
        status.ForeColor = result == "ok" ? Color.LightGreen : Color.Salmon;
        status.Text = result == "ok" ? "✓ Connected" : $"✗ {result.Replace('\n', ' ')}";
    }

    private async Task RunDiagnosticsAsync()
    {
        report.Text = "Running checks…";
        report.Text = await Diagnostics.BuildReportAsync(context.ChatHandle, context.HotKeyRegistered);
    }

    private bool Save()
    {
        var folder = projectBox.Text.Trim();
        if (folder.Length > 0 && !Directory.Exists(folder))
        {
            projectStatus.ForeColor = Color.Salmon;
            projectStatus.Text = "Folder not found.";
            return false;
        }

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
        cfg.UseProject(folder);
        if (HasModels)
        {
            var models = modelList.Items.Cast<string>().ToList();
            cfg.DisabledModels = models.Where((_, i) => !modelList.GetItemChecked(i)).ToList();
            if (orderChanged) cfg.ModelOrder = models;
        }
        cfg.Save();
        context.ApplySettings();
        return true;
    }
}

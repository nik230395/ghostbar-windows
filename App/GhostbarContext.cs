using Ghostbar.Config;
using Ghostbar.Services;
using Ghostbar.UI;

namespace Ghostbar.App;

class GhostbarContext : ApplicationContext
{
    private readonly GlobalHotKey hotKey;
    private ChatForm? chat;

    public GhostbarContext()
    {
        Backend.BackendClient.Log = Logger.Write;
        hotKey = new GlobalHotKey(ToggleChat);
        hotKey.Register(Settings.Current.Toggle);
        if (!hotKey.Registered) ReportHotKeyFailure();
    }

    public bool HotKeyRegistered => hotKey.Registered;
    public IntPtr ChatHandle => Chat.Handle;

    private static void ReportHotKeyFailure() =>
        MessageBox.Show($"The shortcut {Settings.Current.Toggle.DisplayString} is used by another program. Ghostbar cannot be opened until you free it or choose another shortcut.", "Ghostbar", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private ChatForm Chat => chat ??= new ChatForm(this);

    private void ToggleChat()
    {
        Logger.Write("toggle hotkey pressed");
        if (Chat.Visible) Chat.Hide();
        else Chat.ShowAndFocus();
    }

    public void ShowSettings()
    {
        using var dialog = new SettingsForm(this);
        dialog.ShowDialog(Chat);
    }

    public void ApplySettings()
    {
        hotKey.Register(Settings.Current.Toggle);
        if (!hotKey.Registered) ReportHotKeyFailure();
        Chat.PushSettings();
    }

    public void Quit()
    {
        hotKey.Dispose();
        chat?.Dispose();
        ExitThread();
    }
}

using Ghostbar.Config;
using Ghostbar.UI;

namespace Ghostbar.App;

class GhostbarContext : ApplicationContext
{
    private readonly GlobalHotKey hotKey;
    private ChatForm? chat;

    public GhostbarContext()
    {
        hotKey = new GlobalHotKey(ToggleChat);
        hotKey.Register(Settings.Current.Toggle);
    }

    private ChatForm Chat => chat ??= new ChatForm(this);

    private void ToggleChat()
    {
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
        Chat.PushSettings();
    }

    public void Quit()
    {
        hotKey.Dispose();
        chat?.Dispose();
        ExitThread();
    }
}

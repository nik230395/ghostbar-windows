using Ghostbar.Native;

namespace Ghostbar.UI;

class StealthForm : Form
{
    public static readonly Color Background = Color.FromArgb(41, 44, 51);

    protected StealthForm()
    {
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Background;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle = (cp.ExStyle | Win32.WS_EX_TOOLWINDOW) & ~Win32.WS_EX_APPWINDOW;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var ok = Win32.SetWindowDisplayAffinity(Handle, Win32.WDA_EXCLUDEFROMCAPTURE);
        Services.Logger.Write($"capture protection {GetType().Name} applied={ok}");
    }

    public void ShowAndFocus()
    {
        Show();
        Activate();
        Win32.SetForegroundWindow(Handle);
        Win32.SetWindowDisplayAffinity(Handle, Win32.WDA_EXCLUDEFROMCAPTURE);
    }
}

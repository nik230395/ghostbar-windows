using Ghostbar.Config;

namespace Ghostbar.UI;

class ShortcutBox : TextBox
{
    private bool recording;

    public KeyChord Value { get; private set; }

    public ShortcutBox(KeyChord initial)
    {
        Value = initial;
        ReadOnly = true;
        Text = initial.DisplayString;
        Cursor = Cursors.Hand;
        ShortcutsEnabled = false;
        Click += (_, _) => { recording = true; Text = "Press keys…"; };
        Leave += (_, _) => { recording = false; Text = Value.DisplayString; };
    }

    protected override bool IsInputKey(Keys keyData) => true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!recording) return;
        e.SuppressKeyPress = true;
        e.Handled = true;
        if (e.KeyCode == Keys.Escape)
        {
            recording = false;
            Text = Value.DisplayString;
            return;
        }
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
        if (!e.Control && !e.Alt && !e.Shift) return;
        Value = new KeyChord(e.Control, e.Alt, e.Shift, e.KeyCode);
        recording = false;
        Text = Value.DisplayString;
    }
}

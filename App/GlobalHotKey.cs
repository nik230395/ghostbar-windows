using Ghostbar.Config;
using Ghostbar.Native;
using Ghostbar.Services;

namespace Ghostbar.App;

class GlobalHotKey : NativeWindow, IDisposable
{
    private const int Id = 1;
    private readonly Action callback;

    public bool Registered { get; private set; }

    public GlobalHotKey(Action callback)
    {
        this.callback = callback;
        CreateHandle(new CreateParams { Parent = Win32.HWND_MESSAGE });
    }

    public bool Register(KeyChord s)
    {
        Registered = false;
        Win32.UnregisterHotKey(Handle, Id);
        uint mods = Win32.MOD_NOREPEAT;
        if (s.Ctrl)  mods |= Win32.MOD_CONTROL;
        if (s.Alt)   mods |= Win32.MOD_ALT;
        if (s.Shift) mods |= Win32.MOD_SHIFT;
        Registered = Win32.RegisterHotKey(Handle, Id, mods, (uint)s.Key);
        Logger.Write($"hotkey {s.DisplayString} registered={Registered}");
        return Registered;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32.WM_HOTKEY) callback();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Win32.UnregisterHotKey(Handle, Id);
        DestroyHandle();
    }
}

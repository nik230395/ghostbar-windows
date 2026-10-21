using Ghostbar.App;

namespace Ghostbar;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "Ghostbar.SingleInstance", out bool isFirst);
        if (!isFirst) return;
        ApplicationConfiguration.Initialize();
        Application.Run(new GhostbarContext());
    }
}

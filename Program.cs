using Ghostbar.App;
using Ghostbar.Services;

namespace Ghostbar;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "Ghostbar.SingleInstance", out bool isFirst);
        if (!isFirst) return;
        Logger.Write($"start v{typeof(Program).Assembly.GetName().Version} os={Environment.OSVersion.Version}");
        Application.ThreadException += (_, e) => Logger.Error("ui thread", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Logger.Write($"FATAL {e.ExceptionObject}");
        ApplicationConfiguration.Initialize();
        Application.Run(new GhostbarContext());
    }
}

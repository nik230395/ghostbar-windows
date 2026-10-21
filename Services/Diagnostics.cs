using System.Runtime.InteropServices;
using System.Text;
using Ghostbar.Backend;
using Ghostbar.Config;
using Ghostbar.Native;
using Microsoft.Web.WebView2.Core;
using NAudio.Wave;

namespace Ghostbar.Services;

static class Diagnostics
{
    public static async Task<string> BuildReportAsync(IntPtr chatHandle, bool hotKeyRegistered)
    {
        var cfg = Settings.Current;
        var sb = new StringBuilder();
        void Line(string name, string value, bool? ok = null) =>
            sb.AppendLine($"{(ok == null ? "   " : ok == true ? "OK " : "BAD")} {name}: {value}");

        var os = Environment.OSVersion.Version;
        Line("Windows build", $"{os} (needs >= 10.0.19041)", os.Build >= 19041);
        Line(".NET runtime", RuntimeInformation.FrameworkDescription);

        try
        {
            Line("WebView2 runtime", CoreWebView2Environment.GetAvailableBrowserVersionString(), true);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Line("WebView2 runtime", "not installed (winget install Microsoft.EdgeWebView2Runtime)", false);
        }

        Line("Toggle hotkey", $"{cfg.Toggle.DisplayString} {(hotKeyRegistered ? "registered" : "NOT registered (used by another program?)")}", hotKeyRegistered);

        if (Win32.GetWindowDisplayAffinity(chatHandle, out var affinity))
            Line("Capture protection", $"affinity=0x{affinity:X} (0x11 = hidden from capture)", affinity == Win32.WDA_EXCLUDEFROMCAPTURE);
        else
            Line("Capture protection", "could not read affinity", false);

        Line("Backend", $"{cfg.BackendType} {cfg.BackendUrl}");
        Line("API key", cfg.ApiKey.Length > 0 ? $"set ({cfg.ApiKey.Length} chars)" : "empty");
        var test = await BackendClient.TestAsync(cfg.ToBackendOptions());
        Line("Backend reachable", test == "ok" ? "yes" : test.Replace('\n', ' '), test == "ok");

        var bin = Transcriber.FindBinary();
        var model = Transcriber.FindModel();
        Line("whisper-cli.exe", bin ?? "not found (optional)");
        Line("whisper model", model ?? "not found (optional)");
        Line("Microphones", WaveInEvent.DeviceCount.ToString());

        Line("Settings file", Settings.FilePath);
        Line("Log file", Logger.FilePath);
        return sb.ToString();
    }
}

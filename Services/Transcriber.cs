using System.Diagnostics;

namespace Ghostbar.Services;

static class Transcriber
{
    private static IEnumerable<string> SearchDirs()
    {
        yield return AppContext.BaseDirectory;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ghostbar", "whisper");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "whisper");
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (dir.Length > 0) yield return dir;
    }

    public static string? FindBinary() =>
        SearchDirs().Select(d => Path.Combine(d, "whisper-cli.exe")).FirstOrDefault(File.Exists);

    public static string? FindModel()
    {
        string[] preferred = ["ggml-base.en.bin", "ggml-base.bin", "ggml-small.bin", "ggml-tiny.bin", "ggml-medium.bin"];
        foreach (var dir in SearchDirs().Where(Directory.Exists))
        {
            var hit = preferred.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists);
            if (hit != null) return hit;
            var any = Directory.EnumerateFiles(dir, "ggml*.bin").FirstOrDefault();
            if (any != null) return any;
        }
        return null;
    }

    public static async Task<string?> TranscribeAsync(string audioPath)
    {
        var bin = FindBinary();
        var model = FindModel();
        if (bin == null || model == null) return null;

        var outPrefix = Path.ChangeExtension(audioPath, null);
        var info = new ProcessStartInfo(bin)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "--model", model, "--language", "auto", "--output-txt", "--no-prints", "--output-file", outPrefix, "--file", audioPath })
            info.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(info);
            if (process == null) return null;
            var stdout = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var txtPath = outPrefix + ".txt";
            var text = File.Exists(txtPath) ? await File.ReadAllTextAsync(txtPath) : await stdout;
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\[.*?\]", "").Trim();
            return text.Length == 0 ? null : text;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        finally
        {
            TryDelete(audioPath);
            TryDelete(outPrefix + ".txt");
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { }
    }
}

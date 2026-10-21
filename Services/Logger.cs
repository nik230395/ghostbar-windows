namespace Ghostbar.Services;

static class Logger
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static readonly string Directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ghostbar");
    public static readonly string FilePath = Path.Combine(Directory, "ghostbar.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void Error(string where, Exception e) => Write($"ERROR {where}: {e.GetType().Name}: {e.Message}");
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ghostbar.Config;

public enum BackendType { Ollama, OpenAI, Anthropic, OpenRouter }

public class Settings
{
    public BackendType BackendType { get; set; } = BackendType.Ollama;
    public string BackendUrl { get; set; } = "http://localhost:11434";
    public string ProtectedApiKey { get; set; } = "";
    public string SystemPrompt { get; set; } = "";
    public string ScreenshotPrompt { get; set; } = "";
    public string SelectedModel { get; set; } = "";
    public Rectangle? WindowBounds { get; set; }
    public KeyChord Toggle { get; set; } = KeyChord.DefaultToggle;
    public KeyChord Mic { get; set; } = KeyChord.DefaultMic;
    public KeyChord Capture { get; set; } = KeyChord.DefaultCapture;

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ghostbar");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static Settings Current { get; private set; } = Load();

    [JsonIgnore]
    public string ApiKey
    {
        get
        {
            if (ProtectedApiKey.Length == 0) return "";
            try
            {
                var plain = ProtectedData.Unprotect(
                    Convert.FromBase64String(ProtectedApiKey), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (CryptographicException) { return ""; }
        }
        set
        {
            ProtectedApiKey = value.Length == 0
                ? ""
                : Convert.ToBase64String(ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
        }
    }

    private static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings(); }
        catch { return new Settings(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

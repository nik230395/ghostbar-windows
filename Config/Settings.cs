using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ghostbar.Backend;

namespace Ghostbar.Config;

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
    public string ProjectFolder { get; set; } = "";
    public List<string> RecentProjects { get; set; } = [];
    public List<string> ModelOrder { get; set; } = [];
    public List<string> DisabledModels { get; set; } = [];
    public Dictionary<string, DateTimeOffset> ModelCooldowns { get; set; } = [];

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ghostbar");
    public static readonly string FilePath = Path.Combine(Dir, "settings.json");

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

    public void UseProject(string folder)
    {
        ProjectFolder = folder;
        if (folder.Length == 0) return;
        RecentProjects.RemoveAll(p => string.Equals(p, folder, StringComparison.OrdinalIgnoreCase));
        RecentProjects.Insert(0, folder);
        if (RecentProjects.Count > 8) RecentProjects.RemoveRange(8, RecentProjects.Count - 8);
    }

    public BackendOptions ToBackendOptions() => new(BackendType, BackendUrl, ApiKey, SystemPrompt);

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

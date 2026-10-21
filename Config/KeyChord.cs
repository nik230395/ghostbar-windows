using System.Text.Json.Serialization;

namespace Ghostbar.Config;

public record KeyChord(bool Ctrl, bool Alt, bool Shift, Keys Key)
{
    public static readonly KeyChord DefaultToggle  = new(true, false, true, Keys.Space);
    public static readonly KeyChord DefaultMic     = new(true, false, true, Keys.M);
    public static readonly KeyChord DefaultCapture = new(true, false, true, Keys.S);

    [JsonIgnore]
    public string DisplayString =>
        (Ctrl ? "Ctrl+" : "") + (Alt ? "Alt+" : "") + (Shift ? "Shift+" : "") + Key;

    public string ToJs() =>
        $"{{\"cmd\":false,\"opt\":{Js(Alt)},\"ctrl\":{Js(Ctrl)},\"shift\":{Js(Shift)},\"key\":\"{JsKey()}\"}}";

    private string JsKey()
    {
        var name = Key.ToString();
        if (name.Length == 1) return name.ToLowerInvariant();
        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) return name[1].ToString();
        return name.ToLowerInvariant();
    }

    private static string Js(bool b) => b ? "true" : "false";
}

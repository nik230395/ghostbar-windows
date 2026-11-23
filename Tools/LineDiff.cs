namespace Ghostbar.Tools;

public record DiffLine(char Kind, string Text);

public static class LineDiff
{
    public static List<DiffLine> Compute(string? oldText, string newText, int context = 3, int maxLines = 300)
    {
        var oldLines = oldText == null ? [] : Split(oldText);
        var newLines = Split(newText);

        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length && oldLines[prefix] == newLines[prefix]) prefix++;
        var suffix = 0;
        while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix &&
               oldLines[^(suffix + 1)] == newLines[^(suffix + 1)]) suffix++;

        var result = new List<DiffLine>();
        var contextStart = Math.Max(0, prefix - context);
        if (contextStart > 0) result.Add(new DiffLine('…', $"{contextStart} unchanged lines"));
        for (var i = contextStart; i < prefix; i++) result.Add(new DiffLine(' ', oldLines[i]));
        for (var i = prefix; i < oldLines.Length - suffix; i++) result.Add(new DiffLine('-', oldLines[i]));
        for (var i = prefix; i < newLines.Length - suffix; i++) result.Add(new DiffLine('+', newLines[i]));
        var contextEnd = Math.Min(newLines.Length, newLines.Length - suffix + context);
        for (var i = newLines.Length - suffix; i < contextEnd; i++) result.Add(new DiffLine(' ', newLines[i]));
        if (contextEnd < newLines.Length) result.Add(new DiffLine('…', $"{newLines.Length - contextEnd} unchanged lines"));

        if (result.Count > maxLines)
        {
            var hidden = result.Count - maxLines;
            result = result.Take(maxLines).ToList();
            result.Add(new DiffLine('…', $"{hidden} more lines not shown"));
        }
        return result;
    }

    private static string[] Split(string text) =>
        text.Length == 0 ? [] : text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
}

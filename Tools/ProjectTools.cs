using System.IO.Enumeration;
using System.Text;
using System.Text.Json.Nodes;
using Ghostbar.Backend;

namespace Ghostbar.Tools;

public record PendingChange(string Path, string? OldContent, string NewContent);

public class ToolException(string message) : Exception(message);

public class ProjectTools : IToolExecutor
{
    public const int MaxReadLines = 800;
    public const int MaxListEntries = 400;
    public const int MaxSearchHits = 100;
    private const long MaxReadBytes = 2 * 1024 * 1024;
    private const long MaxSearchFileBytes = 1024 * 1024;
    private const int MaxWriteChars = 1024 * 1024;

    private static readonly HashSet<string> SkippedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", ".vs", ".idea", ".venv", "venv", "__pycache__",
        "dist", "build", "target", ".next", "packages",
    };

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly Func<PendingChange, CancellationToken, Task<bool>> confirm;

    public string Root { get; }
    public Action<string> Activity { get; set; } = _ => { };

    public ProjectTools(string root, Func<PendingChange, CancellationToken, Task<bool>> confirm)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(Root)) throw new ToolException($"Project folder not found: {Root}");
        this.confirm = confirm;
    }

    public JsonArray Definitions => (JsonArray)JsonNode.Parse(DefinitionsJson)!;

    public string SystemPrompt(bool toolsEnabled)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are helping with the project in the folder \"{Path.GetFileName(Root)}\".");
        if (toolsEnabled)
        {
            sb.AppendLine("Use the tools list_dir, read_file and search to look at the project before answering or changing code.");
            sb.AppendLine("Use edit_file for small changes and write_file for new files or full rewrites. Read a file before editing it.");
            sb.AppendLine("All paths are relative to the project root. The user approves every change, so explain briefly what you change and why.");
        }
        sb.AppendLine();
        sb.AppendLine("Project structure (top levels):");
        sb.AppendLine(ListDir(".", 2, 150));
        var readme = new[] { "README.md", "readme.md", "README.txt", "README" }
            .Select(n => Path.Combine(Root, n)).FirstOrDefault(File.Exists);
        if (readme != null)
        {
            var text = File.ReadAllText(readme);
            sb.AppendLine();
            sb.AppendLine($"{Path.GetFileName(readme)} (start):");
            sb.AppendLine(text.Length > 3000 ? text[..3000] + "\n…" : text);
        }
        return sb.ToString();
    }

    public async Task<string> ExecuteAsync(string name, string arguments, CancellationToken ct)
    {
        JsonNode? args;
        try { args = JsonNode.Parse(arguments.Length == 0 ? "{}" : arguments); }
        catch (System.Text.Json.JsonException) { return "Error: the tool arguments are not valid JSON."; }

        try
        {
            return name switch
            {
                "list_dir" => Run($"📂 lists {Arg(args, "path") ?? "."}", () => ListDir(Arg(args, "path") ?? ".", IntArg(args, "depth") ?? 2, MaxListEntries)),
                "read_file" => Run($"📄 reads {Required(args, "path")}", () => ReadFile(Required(args, "path"), IntArg(args, "start_line"), IntArg(args, "end_line"))),
                "search" => Run($"🔎 searches \"{Required(args, "query")}\"", () => Search(Required(args, "query"), Arg(args, "path") ?? ".", Arg(args, "glob"))),
                "edit_file" => await EditFileAsync(Required(args, "path"), Required(args, "old_text"), Arg(args, "new_text") ?? "", ct),
                "write_file" => await WriteFileAsync(Required(args, "path"), Arg(args, "content") ?? "", ct),
                _ => $"Error: unknown tool '{name}'.",
            };
        }
        catch (ToolException e) { return "Error: " + e.Message; }
        catch (IOException e) { return "Error: " + e.Message; }
        catch (UnauthorizedAccessException) { return "Error: access denied."; }
    }

    private string Run(string activity, Func<string> action)
    {
        Activity(activity);
        return action();
    }

    private static string? Arg(JsonNode? args, string key) => SseLine.Str(SseLine.At(args, key));

    private static string Required(JsonNode? args, string key) =>
        Arg(args, key) ?? throw new ToolException($"missing argument '{key}'.");

    private static int? IntArg(JsonNode? args, string key) =>
        SseLine.At(args, key) is JsonValue v && v.TryGetValue<int>(out var n) ? n : null;

    public string Resolve(string path, bool forWrite = false)
    {
        if (string.IsNullOrWhiteSpace(path)) path = ".";
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Root, path)));
        if (!IsInside(full)) throw new ToolException($"'{path}' is outside the project folder.");

        var segments = Path.GetRelativePath(Root, full).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (forWrite && segments.Any(s => s.Equals(".git", StringComparison.OrdinalIgnoreCase)))
            throw new ToolException("writing inside .git is not allowed.");

        for (var p = full; !string.Equals(p, Root, PathComparison); p = Path.GetDirectoryName(p)!)
        {
            var link = File.Exists(p) ? new FileInfo(p).LinkTarget : Directory.Exists(p) ? new DirectoryInfo(p).LinkTarget : null;
            if (link != null) throw new ToolException($"'{path}' goes through a link, which is not allowed.");
        }
        return full;
    }

    private bool IsInside(string full) =>
        string.Equals(full, Root, PathComparison) ||
        full.StartsWith(Root + Path.DirectorySeparatorChar, PathComparison);

    private string Relative(string full) => Path.GetRelativePath(Root, full).Replace('\\', '/');

    public string ListDir(string path, int depth, int maxEntries)
    {
        var start = Resolve(path);
        if (!Directory.Exists(start)) throw new ToolException($"'{path}' is not a folder.");
        depth = Math.Clamp(depth, 1, 4);
        var lines = new List<string>();
        var truncated = false;

        void Walk(string dir, int level)
        {
            var entries = new DirectoryInfo(dir).EnumerateFileSystemInfos()
                .Where(e => e.LinkTarget == null)
                .OrderBy(e => e is FileInfo)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (lines.Count >= maxEntries) { truncated = true; return; }
                var indent = new string(' ', level * 2);
                if (entry is DirectoryInfo sub)
                {
                    if (SkippedDirs.Contains(sub.Name)) continue;
                    lines.Add($"{indent}{sub.Name}/");
                    if (level + 1 < depth) Walk(sub.FullName, level + 1);
                }
                else if (entry is FileInfo file)
                {
                    lines.Add($"{indent}{file.Name} ({FormatSize(file.Length)})");
                }
            }
        }

        Walk(start, 0);
        if (lines.Count == 0) return $"{Relative(start)}/ is empty.";
        if (truncated) lines.Add($"… more entries not shown. List a subfolder to see them.");
        return string.Join('\n', lines);
    }

    private static string FormatSize(long bytes) =>
        bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024} KB" : $"{bytes / (1024 * 1024)} MB";

    public string ReadFile(string path, int? startLine, int? endLine)
    {
        var full = Resolve(path);
        if (!File.Exists(full)) throw new ToolException($"'{path}' does not exist.");
        if (new FileInfo(full).Length > MaxReadBytes) throw new ToolException($"'{path}' is larger than 2 MB.");
        if (IsBinary(full)) throw new ToolException($"'{path}' is a binary file.");

        var all = File.ReadAllLines(full);
        var start = Math.Max(1, startLine ?? 1);
        if (all.Length == 0) return $"{Relative(full)} is empty.";
        if (start > all.Length) throw new ToolException($"'{path}' has only {all.Length} lines.");
        var end = Math.Min(all.Length, Math.Min(endLine ?? int.MaxValue, start + MaxReadLines - 1));
        var header = $"{Relative(full)} (lines {start}-{end} of {all.Length})";
        var body = string.Join('\n', all[(start - 1)..end]);
        var cutShort = end < all.Length && (endLine == null || end < endLine);
        var more = cutShort ? $"\n… {all.Length - end} more lines. Call read_file with start_line={end + 1} to continue." : "";
        return $"{header}\n{body}{more}";
    }

    private static bool IsBinary(string full)
    {
        using var stream = File.OpenRead(full);
        var buffer = new byte[8192];
        var read = stream.Read(buffer, 0, buffer.Length);
        return Array.IndexOf(buffer, (byte)0, 0, read) >= 0;
    }

    public string Search(string query, string path, string? glob)
    {
        if (query.Length == 0) throw new ToolException("query is empty.");
        var start = Resolve(path);
        if (!Directory.Exists(start)) throw new ToolException($"'{path}' is not a folder.");
        var hits = new List<string>();

        foreach (var file in EnumerateFiles(start))
        {
            if (glob != null && !FileSystemName.MatchesSimpleExpression(glob, Path.GetFileName(file))) continue;
            var info = new FileInfo(file);
            if (info.Length > MaxSearchFileBytes || IsBinary(file)) continue;
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                if (!line.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                var text = line.Trim();
                hits.Add($"{Relative(file)}:{lineNumber}: {(text.Length > 200 ? text[..200] + "…" : text)}");
                if (hits.Count >= MaxSearchHits)
                    return string.Join('\n', hits) + $"\n… stopped after {MaxSearchHits} matches. Narrow the search.";
            }
        }
        return hits.Count == 0 ? $"No matches for \"{query}\"." : string.Join('\n', hits);
    }

    private static IEnumerable<string> EnumerateFiles(string dir)
    {
        var info = new DirectoryInfo(dir);
        foreach (var file in info.EnumerateFiles().Where(f => f.LinkTarget == null).OrderBy(f => f.Name, StringComparer.Ordinal))
            yield return file.FullName;
        foreach (var sub in info.EnumerateDirectories().Where(d => d.LinkTarget == null && !SkippedDirs.Contains(d.Name)).OrderBy(d => d.Name, StringComparer.Ordinal))
            foreach (var file in EnumerateFiles(sub.FullName))
                yield return file;
    }

    public async Task<string> EditFileAsync(string path, string oldText, string newText, CancellationToken ct)
    {
        var full = Resolve(path, forWrite: true);
        if (!File.Exists(full)) throw new ToolException($"'{path}' does not exist. Use write_file to create it.");
        if (oldText.Length == 0) throw new ToolException("old_text is empty.");
        var (content, bom) = ReadText(full);

        var count = Occurrences(content, oldText);
        if (count == 0 && content.Contains("\r\n"))
        {
            oldText = ToCrLf(oldText);
            newText = ToCrLf(newText);
            count = Occurrences(content, oldText);
        }
        if (count == 0) throw new ToolException($"old_text was not found in '{path}'. Read the file again and copy the exact text.");
        if (count > 1) throw new ToolException($"old_text appears {count} times in '{path}'. Include more surrounding lines so it is unique.");

        var index = content.IndexOf(oldText, StringComparison.Ordinal);
        var updated = content[..index] + newText + content[(index + oldText.Length)..];
        Activity($"✏️ wants to edit {Relative(full)}");
        if (!await confirm(new PendingChange(Relative(full), content, updated), ct)) return Rejected(path);
        await File.WriteAllTextAsync(full, updated, new UTF8Encoding(bom), ct);
        return $"Edited {Relative(full)}.";
    }

    public async Task<string> WriteFileAsync(string path, string content, CancellationToken ct)
    {
        if (content.Length > MaxWriteChars) throw new ToolException("content is larger than 1 MB.");
        var full = Resolve(path, forWrite: true);
        if (Directory.Exists(full)) throw new ToolException($"'{path}' is a folder.");
        var exists = File.Exists(full);
        if (exists && IsBinary(full)) throw new ToolException($"'{path}' is a binary file and cannot be overwritten.");
        var (old, bom) = exists ? ReadText(full) : (null, false);

        Activity($"✏️ wants to {(exists ? "overwrite" : "create")} {Relative(full)}");
        if (!await confirm(new PendingChange(Relative(full), old, content), ct)) return Rejected(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content, new UTF8Encoding(bom), ct);
        var lines = content.Length == 0 ? 0 : content.Split('\n').Length;
        return $"{(exists ? "Overwrote" : "Created")} {Relative(full)} ({lines} lines).";
    }

    private static (string Text, bool Bom) ReadText(string full)
    {
        var bytes = File.ReadAllBytes(full);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return (Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)), bom);
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string ToCrLf(string text) => text.Replace("\r\n", "\n").Replace("\n", "\r\n");

    private static string Rejected(string path) =>
        $"The user rejected the change to '{path}'. Do not retry the same change; ask what they want instead.";

    private const string DefinitionsJson = """
        [
          {"type":"function","function":{"name":"list_dir","description":"List files and folders in the project. Skips .git, node_modules, bin, obj and similar folders.","parameters":{"type":"object","properties":{"path":{"type":"string","description":"Folder relative to the project root. Default: ."},"depth":{"type":"integer","description":"How many levels to show, 1-4. Default: 2."}}}}},
          {"type":"function","function":{"name":"read_file","description":"Read a text file. Returns at most 800 lines per call; use start_line to read further.","parameters":{"type":"object","properties":{"path":{"type":"string","description":"File relative to the project root."},"start_line":{"type":"integer","description":"First line, 1-based."},"end_line":{"type":"integer","description":"Last line, inclusive."}},"required":["path"]}}},
          {"type":"function","function":{"name":"search","description":"Case-insensitive text search in project files. Returns path:line: text, at most 100 matches.","parameters":{"type":"object","properties":{"query":{"type":"string","description":"Text to find."},"path":{"type":"string","description":"Folder to search in. Default: ."},"glob":{"type":"string","description":"File name filter such as *.cs or *.md."}},"required":["query"]}}},
          {"type":"function","function":{"name":"edit_file","description":"Replace one exact piece of text in an existing file. old_text must appear exactly once; include a few surrounding lines. The user must approve the change.","parameters":{"type":"object","properties":{"path":{"type":"string"},"old_text":{"type":"string","description":"Exact text currently in the file."},"new_text":{"type":"string","description":"Replacement text."}},"required":["path","old_text","new_text"]}}},
          {"type":"function","function":{"name":"write_file","description":"Create a new file or replace a whole file. Folders are created as needed. The user must approve the change.","parameters":{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string","description":"Complete file content."}},"required":["path","content"]}}}
        ]
        """;
}

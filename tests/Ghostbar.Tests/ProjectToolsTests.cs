using Ghostbar.Tools;

namespace Ghostbar.Tests;

public class ProjectToolsTests : IDisposable
{
    private readonly string root;
    private readonly string outside;
    private readonly List<PendingChange> asked = [];
    private bool approve = true;
    private readonly ProjectTools tools;

    public ProjectToolsTests()
    {
        var parent = Directory.CreateTempSubdirectory("ghostbar-tools").FullName;
        root = Directory.CreateDirectory(Path.Combine(parent, "project")).FullName;
        outside = Directory.CreateDirectory(Path.Combine(parent, "outside")).FullName;
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "top secret");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "node_modules", "lib"));
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        File.WriteAllText(Path.Combine(root, "src", "App.cs"), "class App\n{\n    // TODO greet\n}\n");
        File.WriteAllText(Path.Combine(root, "README.md"), "# Demo\nA demo project.");
        File.WriteAllText(Path.Combine(root, "node_modules", "lib", "x.js"), "TODO hidden");
        File.WriteAllBytes(Path.Combine(root, "logo.png"), [0x89, 0x50, 0x00, 0x01]);
        tools = new ProjectTools(root, (change, _) => { asked.Add(change); return Task.FromResult(approve); });
    }

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(root)!, true);

    private Task<string> Call(string name, string args) => tools.ExecuteAsync(name, args, CancellationToken.None);

    [Fact]
    public async Task ListDir_ShowsProject_SkipsDependencyAndGitFolders()
    {
        var result = await Call("list_dir", "{}");
        Assert.Contains("src/", result);
        Assert.Contains("  App.cs", result);
        Assert.Contains("README.md", result);
        Assert.DoesNotContain("node_modules", result);
        Assert.DoesNotContain(".git", result);
    }

    [Fact]
    public async Task ReadFile_ReturnsLinesWithHeader_AndSupportsRanges()
    {
        Assert.StartsWith("src/App.cs (lines 1-4 of 4)\nclass App", await Call("read_file", """{"path":"src/App.cs"}"""));
        Assert.Equal("src/App.cs (lines 3-3 of 4)\n    // TODO greet", await Call("read_file", """{"path":"src/App.cs","start_line":3,"end_line":3}"""));
    }

    [Fact]
    public async Task ReadFile_LongFile_IsTruncatedWithHint()
    {
        File.WriteAllLines(Path.Combine(root, "long.txt"), Enumerable.Range(1, 1000).Select(i => $"line {i}"));
        var result = await Call("read_file", """{"path":"long.txt"}""");
        Assert.Contains("(lines 1-800 of 1000)", result);
        Assert.Contains("start_line=801", result);
    }

    [Theory]
    [InlineData("""{"path":"logo.png"}""", "binary")]
    [InlineData("""{"path":"missing.txt"}""", "does not exist")]
    [InlineData("""{}""", "missing argument 'path'")]
    [InlineData("""not json""", "not valid JSON")]
    public async Task ReadFile_Errors_AreExplained(string args, string expected)
    {
        var result = await Call("read_file", args);
        Assert.StartsWith("Error:", result);
        Assert.Contains(expected, result);
    }

    [Fact]
    public async Task Search_FindsMatches_SkipsNodeModules_AndFiltersByGlob()
    {
        Assert.Equal("src/App.cs:3: // TODO greet", await Call("search", """{"query":"todo"}"""));
        Assert.StartsWith("No matches", await Call("search", """{"query":"todo","glob":"*.md"}"""));
    }

    [Theory]
    [InlineData("../outside/secret.txt")]
    [InlineData("src/../../outside/secret.txt")]
    public async Task PathsOutsideProject_AreRefused(string path)
    {
        var result = await Call("read_file", $$"""{"path":"{{path}}"}""");
        Assert.Contains("outside the project folder", result);
    }

    [Fact]
    public async Task AbsolutePathOutsideProject_IsRefused()
    {
        var path = Path.Combine(outside, "secret.txt").Replace("\\", "\\\\");
        Assert.Contains("outside the project folder", await Call("read_file", $$"""{"path":"{{path}}"}"""));
    }

    [Fact]
    public async Task LinkPointingOutside_IsRefused()
    {
        Directory.CreateSymbolicLink(Path.Combine(root, "escape"), outside);
        Assert.Contains("link", await Call("read_file", """{"path":"escape/secret.txt"}"""));
        Assert.DoesNotContain("escape", await Call("list_dir", "{}"));
        Assert.StartsWith("No matches", await Call("search", """{"query":"top secret"}"""));
    }

    [Fact]
    public async Task WritingIntoGit_IsRefused()
    {
        Assert.Contains(".git", await Call("write_file", """{"path":".git/config","content":"x"}"""));
        Assert.Empty(asked);
    }

    [Fact]
    public async Task EditFile_ReplacesUniqueText_AfterApproval()
    {
        var result = await Call("edit_file", """{"path":"src/App.cs","old_text":"// TODO greet","new_text":"// hello"}""");

        Assert.Equal("Edited src/App.cs.", result);
        Assert.Equal("class App\n{\n    // hello\n}\n", File.ReadAllText(Path.Combine(root, "src", "App.cs")));
        Assert.Equal("src/App.cs", asked.Single().Path);
    }

    [Fact]
    public async Task EditFile_Rejected_LeavesFileUnchanged()
    {
        approve = false;
        var result = await Call("edit_file", """{"path":"src/App.cs","old_text":"// TODO greet","new_text":"// hello"}""");

        Assert.Contains("rejected", result);
        Assert.Contains("TODO greet", File.ReadAllText(Path.Combine(root, "src", "App.cs")));
    }

    [Fact]
    public async Task EditFile_NotFoundOrAmbiguous_IsExplained_WithoutAsking()
    {
        File.WriteAllText(Path.Combine(root, "dup.txt"), "x\nx\n");
        Assert.Contains("was not found", await Call("edit_file", """{"path":"src/App.cs","old_text":"nope","new_text":"y"}"""));
        Assert.Contains("appears 2 times", await Call("edit_file", """{"path":"dup.txt","old_text":"x","new_text":"y"}"""));
        Assert.Empty(asked);
    }

    [Fact]
    public async Task EditFile_WindowsLineEndings_MatchUnixOldText()
    {
        var file = Path.Combine(root, "crlf.txt");
        File.WriteAllText(file, "a\r\nb\r\nc\r\n");
        await Call("edit_file", """{"path":"crlf.txt","old_text":"a\nb","new_text":"a\nB"}""");
        Assert.Equal("a\r\nB\r\nc\r\n", File.ReadAllText(file));
    }

    [Fact]
    public async Task EditFile_KeepsUtf8Bom()
    {
        var file = Path.Combine(root, "bom.txt");
        File.WriteAllText(file, "hello", new System.Text.UTF8Encoding(true));
        await Call("edit_file", """{"path":"bom.txt","old_text":"hello","new_text":"hi"}""");
        Assert.Equal([0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i'], File.ReadAllBytes(file));
    }

    [Fact]
    public async Task WriteFile_CreatesFoldersAfterApproval()
    {
        var result = await Call("write_file", """{"path":"docs/guide/intro.md","content":"# Intro\n"}""");

        Assert.Equal("Created docs/guide/intro.md (2 lines).", result);
        Assert.Null(asked.Single().OldContent);
        Assert.Equal("# Intro\n", File.ReadAllText(Path.Combine(root, "docs", "guide", "intro.md")));
    }

    [Fact]
    public async Task WriteFile_Rejected_CreatesNothing()
    {
        approve = false;
        await Call("write_file", """{"path":"new/file.txt","content":"x"}""");
        Assert.False(Directory.Exists(Path.Combine(root, "new")));
    }

    [Fact]
    public async Task WriteFile_ExistingFile_PassesOldContentForDiff()
    {
        await Call("write_file", """{"path":"README.md","content":"# New"}""");
        Assert.Equal("# Demo\nA demo project.", asked.Single().OldContent);
    }

    [Fact]
    public async Task UnknownTool_IsExplained() =>
        Assert.Contains("unknown tool", await Call("delete_everything", "{}"));

    [Fact]
    public void SystemPrompt_ContainsStructureAndReadme()
    {
        var prompt = tools.SystemPrompt(toolsEnabled: true);
        Assert.Contains("\"project\"", prompt);
        Assert.Contains("src/", prompt);
        Assert.Contains("A demo project.", prompt);
        Assert.Contains("edit_file", prompt);
        Assert.DoesNotContain("edit_file", tools.SystemPrompt(toolsEnabled: false));
    }

    [Fact]
    public void Diff_ShowsOnlyChangedRegionWithContext()
    {
        var old = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"l{i}"));
        var updated = old.Replace("l10", "changed");
        var diff = LineDiff.Compute(old, updated);

        Assert.Equal(new DiffLine('…', "6 unchanged lines"), diff[0]);
        Assert.Contains(new DiffLine('-', "l10"), diff);
        Assert.Contains(new DiffLine('+', "changed"), diff);
        Assert.Equal(new DiffLine('…', "7 unchanged lines"), diff[^1]);
        Assert.Equal(10, diff.Count);
    }

    [Fact]
    public void Diff_NewFile_IsAllAdded() =>
        Assert.All(LineDiff.Compute(null, "a\nb"), d => Assert.Equal('+', d.Kind));
}

using System.Text;
using System.Text.RegularExpressions;

namespace Lemon.Core.Wheel;

/// <summary>What a command runs on: each dragged file, each dragged folder, or each dragged web link.</summary>
public enum RunsOn
{
    Files,
    Folders,
    Links,
}

/// <summary>A line of PowerShell the user runs from the Commands ring on dragged files, folders or links.</summary>
public sealed class WheelCommand
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Name { get; set; } = "";

    /// <summary>Segoe Fluent Icons glyph.</summary>
    public string Glyph { get; set; } = Glyphs[0];

    public RunsOn RunsOn { get; set; } = RunsOn.Folders;

    /// <summary>For files: extensions such as "pdf, png"; empty means any file.</summary>
    public string Types { get; set; } = "";

    public string Line { get; set; } = "";

    /// <summary>Command prompt, code, gear, folder, globe, link, camera, bookmark, download, star, lightning, send.</summary>
    public static readonly string[] Glyphs =
        ["", "", "", "", "", "", "", "", "", "", "", ""];

    /// <summary>Whether a dragged file is one of <see cref="Types"/>.</summary>
    public bool Takes(string path)
    {
        var types = Types.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries).Select(t => t.TrimStart('.')).ToList();
        return types.Count == 0 || types.Contains(Path.GetExtension(path).TrimStart('.'), StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>A placeholder in a command line, shown as a chip in the editor.</summary>
public enum CommandToken
{
    File,
    Folder,
    Files,
    Link,
}

public static class CommandLine
{
    public static string Text(this CommandToken token) => "{" + token.ToString().ToLowerInvariant() + "}";

    public static string Label(this CommandToken token) => token == CommandToken.Files ? "Files in Folder" : token.ToString();

    /// <summary>The placeholders that fit what a command runs on, for the editor's Insert buttons.</summary>
    public static IReadOnlyList<CommandToken> Offered(RunsOn on) => on switch
    {
        RunsOn.Files => [CommandToken.File],
        RunsOn.Folders => [CommandToken.Folder, CommandToken.Files],
        _ => [CommandToken.Link],
    };

    /// <summary>The Mac's first version's {path} and {url} still work.</summary>
    private static CommandToken? Named(string text) =>
        text == "{path}" ? CommandToken.Folder
        : text == "{url}" ? CommandToken.Link
        : Enum.GetValues<CommandToken>().Cast<CommandToken?>().FirstOrDefault(t => t!.Value.Text() == text);

    /// <summary>A command line split into plain text and placeholders (as typed, so {url} too), in order. An unknown {…} stays text.</summary>
    public static IReadOnlyList<(string Text, CommandToken? Token)> Tokenize(string line)
    {
        var pieces = new List<(string, CommandToken?)>();
        var plain = new StringBuilder();
        var i = 0;
        while (i < line.Length)
        {
            var close = line[i] == '{' ? line.IndexOf('}', i) : -1;
            if (close > 0 && Named(line[i..(close + 1)]) is { } token)
            {
                if (plain.Length > 0)
                    pieces.Add((plain.ToString(), null));
                plain.Clear();
                pieces.Add((line[i..(close + 1)], token));
                i = close + 1;
            }
            else
                plain.Append(line[i++]);
        }
        if (plain.Length > 0)
            pieces.Add((plain.ToString(), null));
        return pieces;
    }

    /// <summary>
    /// <paramref name="line"/> with each placeholder replaced by <paramref name="value"/> (a path or link) and {files} by
    /// <paramref name="files"/>, all single-quoted for PowerShell so spaces, quotes and <c>$</c> arrive unchanged. One pass:
    /// a name with "{folder}" in it isn't replaced again.
    /// </summary>
    public static string Build(string line, string value, IEnumerable<string>? files = null) =>
        string.Concat(Tokenize(line).Select(piece => piece.Token switch
        {
            null => piece.Text,
            // A PowerShell list: a cmdlet such as Compress-Archive takes it as one array, a program as separate arguments.
            CommandToken.Files => string.Join(", ", (files ?? []).Select(Quote)),
            _ => Quote(value),
        }));

    /// <summary>PowerShell also ends a single-quoted string at a curly quote, as in "Mom’s photo.jpg", so those double too.</summary>
    private static string Quote(string s) => "'" + Regex.Replace(s, "['‘’‚‛]", "$0$0") + "'";
}

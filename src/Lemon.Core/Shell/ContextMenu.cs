using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Lemon.Core.Shell;

/// <summary>
/// Adds a "Convert with Lemon" submenu to Explorer for every supported file type, per user (no admin).
/// On Windows 11 classic verbs live under "Show more options".
/// </summary>
public static partial class ContextMenu
{
    private const string VerbName = "Lemon";

    /// <summary>Quick-convert entries offered in the submenu, per kind of source file.</summary>
    private static readonly Dictionary<MediaKind, string[]> QuickTargets = new()
    {
        [MediaKind.Image] = ["png", "jpg", "webp", "pdf"],
        [MediaKind.Audio] = ["mp3", "m4a", "wav"],
        [MediaKind.Video] = ["mp4", "mp3", "gif"],
        [MediaKind.Document] = ["pdf", "docx", "html"],
    };

    public static bool IsRegistered(RegistryKey? classesRoot = null)
    {
        using var root = classesRoot is null ? OpenClassesRoot() : null;
        using var verb = (classesRoot ?? root)!.OpenSubKey(VerbPath("png"));
        return verb is not null;
    }

    /// <summary>
    /// Registers the menu for each extension. <paramref name="canConvert"/> filters out quick targets
    /// that the current machine can't produce (e.g. PDF without LibreOffice); it's also asked about
    /// <see cref="Preset.SameFormat"/> to decide whether to offer "Optimize".
    /// </summary>
    public static void Register(string exePath, Func<string, string, bool> canConvert, RegistryKey? classesRoot = null)
    {
        using var root = classesRoot is null ? OpenClassesRoot() : null;
        var classes = (classesRoot ?? root)!;

        foreach (var ext in FormatCatalog.Extensions)
        {
            // Start clean so targets that are no longer possible (or got renumbered) don't linger.
            classes.DeleteSubKeyTree(VerbPath(ext), throwOnMissingSubKey: false);

            // No menu for files this machine can't convert at all (e.g. PDF, or .docx without Pandoc/LibreOffice).
            if (!FormatCatalog.Extensions.Append(Preset.SameFormat).Any(to => canConvert(ext, to)))
                continue;

            var kind = FormatCatalog.KindOf(ext)!.Value;
            using var verb = classes.CreateSubKey(VerbPath(ext));
            verb.SetValue("MUIVerb", "Convert with Lemon");
            verb.SetValue("Icon", $"\"{exePath}\",0");
            verb.SetValue("SubCommands", "");

            AddCommand(verb, "01open", "Open in Lemon…", $"\"{exePath}\" \"%1\"");
            var order = 2;
            if (canConvert(ext, Preset.SameFormat))
                AddCommand(verb, $"{order++:00}optimize", "Optimize (smaller file)", $"\"{exePath}\" {LaunchRequest.OptimizeArgument} \"%1\"");
            foreach (var target in QuickTargets[kind].Where(t => t != ext && canConvert(ext, t)))
                AddCommand(verb, $"{order++:00}to{target}", $"To {target.ToUpperInvariant()}", $"\"{exePath}\" --to {target} \"%1\"");
        }

        NotifyShell();
    }

    public static void Unregister(RegistryKey? classesRoot = null)
    {
        using var root = classesRoot is null ? OpenClassesRoot() : null;
        var classes = (classesRoot ?? root)!;

        foreach (var ext in FormatCatalog.Extensions)
            classes.DeleteSubKeyTree(VerbPath(ext), throwOnMissingSubKey: false);

        NotifyShell();
    }

    private static string VerbPath(string extension) => $@"SystemFileAssociations\.{extension}\shell\{VerbName}";

    private static void AddCommand(RegistryKey verb, string keyName, string label, string command)
    {
        using var item = verb.CreateSubKey($@"shell\{keyName}");
        item.SetValue("MUIVerb", label);
        using var cmd = item.CreateSubKey("command");
        cmd.SetValue("", command);
    }

    private static RegistryKey OpenClassesRoot() => Registry.CurrentUser.CreateSubKey(@"Software\Classes");

    private static void NotifyShell() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, 0, 0);

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}

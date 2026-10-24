namespace Lemon.Core.Wheel;

/// <summary>How the conversion wheel looks and which rings it has, as the user set them up in Settings › Wheel.</summary>
public sealed class WheelSettings
{
    /// <summary>A built-in theme's name (see <see cref="WheelTheme.BuiltIn"/>), or "Custom" once anything is changed.</summary>
    public string Theme { get; set; } = WheelTheme.BuiltIn[0].Name;

    /// <summary>Highlight color as "#RRGGBB"; null follows the Windows accent color.</summary>
    public string? Accent { get; set; }

    /// <summary>How strongly the disc takes on the accent color, 0-0.5.</summary>
    public double Tint { get; set; } = 0.12;

    /// <summary>Emoji in the middle of the wheel; "🍋" is Lemon's own lemon.</summary>
    public string Center { get; set; } = "🍋";

    /// <summary>Label color as "#RRGGBB"; null follows light / dark mode.</summary>
    public string? TextColor { get; set; }

    /// <summary>Of the disc and the mode bar, 0.3-1; lower shows more of what's behind the wheel.</summary>
    public double Opacity { get; set; } = 1;

    /// <summary>How the glass blurs what's behind the wheel: 0 none (a plain pane), 1 light, 2 standard, 3 heavy (frosted).</summary>
    public int Blur { get; set; } = 2;

    /// <summary>The Convert ring per kind of file, by preset id, clockwise from the top; missing means the built-in choice.</summary>
    public Dictionary<MediaKind, List<string>> ConvertRing { get; set; } = [];

    /// <summary>Built-in rings switched off, by <see cref="WheelRing.Id"/>.</summary>
    public HashSet<string> HiddenRings { get; set; } = [];

    public List<CustomRing> Rings { get; set; } = [];

    /// <summary>Every drag opens on the hub, where the rings themselves are the slices.</summary>
    public bool OpenOnHub { get; set; }

    /// <summary>Inside a ring, go back to the hub from a chip under the ring rather than from the middle.</summary>
    public bool BackChip { get; set; }

    /// <summary>Without the hub, the ring each kind of file opens on, by <see cref="WheelRing.Id"/>; missing means the first.</summary>
    public Dictionary<MediaKind, string> OpensOn { get; set; } = [];

    public const int MaxCustomRings = 5;

    /// <summary>Removes a preset from the Convert ring and every custom ring, e.g. after it's deleted.</summary>
    public void Forget(string presetId)
    {
        foreach (var ring in ConvertRing.Values)
            ring.Remove(presetId);
        foreach (var ring in Rings)
            ring.PresetIds.Remove(presetId);
    }
}

/// <summary>A ring the user puts together from any presets: formats, Optimize, Remove background, their own.</summary>
public sealed class CustomRing
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Name { get; set; } = "";

    /// <summary>Segoe Fluent Icons glyph.</summary>
    public string Glyph { get; set; } = Glyphs[0];

    /// <summary>Clockwise from the top; ones a file can't use are skipped.</summary>
    public List<string> PresetIds { get; set; } = [];

    /// <summary>Star, lightning, heart, flag, bookmark, magic wand, brush, camera, video, music, document, briefcase.</summary>
    public static readonly string[] Glyphs =
        ["", "", "", "", "", "", "", "", "", "", "", ""];
}

/// <summary>A color scheme for the wheel.</summary>
/// <param name="Accent">"#RRGGBB", or null for the Windows accent color.</param>
public sealed record WheelTheme(string Name, string? Accent, string Center = "🍋")
{
    public static readonly IReadOnlyList<WheelTheme> BuiltIn =
    [
        new("Windows accent", null),
        new("Tangerine", "#FF8C00"),
        new("Lime", "#8CD91A", "🍏"),
        new("Berry", "#ED3880", "🍓"),
        new("Ocean", "#2699FF", "🫐"),
        new("Graphite", "#8C8F99"),
    ];

    public void ApplyTo(WheelSettings settings)
    {
        settings.Theme = Name;
        settings.Accent = Accent;
        settings.Center = Center;
        settings.TextColor = null;
    }
}

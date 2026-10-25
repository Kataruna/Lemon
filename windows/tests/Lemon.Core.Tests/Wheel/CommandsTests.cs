namespace Lemon.Core.Tests;

public class CommandsTests
{
    [Fact]
    public void PlaceholdersAreQuotedForPowerShellInOnePass()
    {
        Assert.Equal("code 'C:\\My ''$x'' {folder}'", CommandLine.Build("code {folder}", "C:\\My '$x' {folder}"));
        Assert.Equal("echo 'Mom’’s.jpg'", CommandLine.Build("echo {file}", "Mom’s.jpg"));
        Assert.Equal("zip 'a b.png', 'c.png' -d 'D'", CommandLine.Build("zip {files} -d {folder}", "D", ["a b.png", "c.png"]));
    }

    [Fact]
    public void UnknownBracesStayTextAndOldPlaceholdersStillWork()
    {
        Assert.Equal("$x = @{a=1}; open 'p' 'p'", CommandLine.Build("$x = @{a=1}; open {path} {url}", "p"));
        Assert.Equal([("{x} ", null), ("{url}", CommandToken.Link)], CommandLine.Tokenize("{x} {url}"));
    }

    [Fact]
    public void FileTypesMatchIgnoringCaseAndDots()
    {
        Assert.True(new WheelCommand { Types = "" }.Takes(@"C:\a.zip"));
        Assert.True(new WheelCommand { Types = "pdf, .png" }.Takes(@"C:\A.PNG"));
        Assert.False(new WheelCommand { Types = "pdf png" }.Takes(@"C:\a.jpg"));
    }

    [Fact]
    public void CommandsRingComesLastAndOnlyWithCommands()
    {
        var settings = new WheelSettings();
        IReadOnlyList<Preset> forPng = [.. BuiltInPresets.All.Where(p => p.TargetKind == MediaKind.Image)];
        var command = new WheelCommand { Name = "Open in Code", Line = "code {folder}" };

        Assert.DoesNotContain(ConversionWheel.Plan([(MediaKind.Image, forPng)], MediaKind.Image, settings).Rings, r => r.Id == "commands");

        var plan = ConversionWheel.Plan([(MediaKind.Image, forPng)], MediaKind.Image, settings, [command]);
        Assert.Equal("commands", plan.Rings[^1].Id);
        Assert.Equal(0, plan.Start);
        Assert.Equal(command.Id, plan.Choices[^1].Single().CommandId);
        Assert.Equal(("Open in Code", null), ConversionWheel.Label(plan.Choices[^1][0]));

        var folders = ConversionWheel.Plan([(MediaKind.Image, forPng)], MediaKind.Image, settings, [command], onCommands: true);
        Assert.Equal(folders.Rings.Count - 1, folders.Start);
        Assert.False(folders.OpensOnHub);

        // Nothing convertible (a link, or a file only a command takes): just the Commands ring.
        Assert.Equal(["commands"], ConversionWheel.Plan([], MediaKind.Image, settings, [command]).Rings.Select(r => r.Id));
    }

    [Fact]
    public void IconsAreOneGlyphEachAndDistinct()
    {
        Assert.All(WheelCommand.Glyphs, g => Assert.Equal(1, g.Length));
        Assert.Equal(WheelCommand.Glyphs.Length, WheelCommand.Glyphs.Distinct().Count());
        Assert.Equal(1, WheelRing.Commands.Glyph.Length);
    }
}

namespace Lemon.Core.Tests;

public class PresetStoreTests
{
    [Fact]
    public void PersistsUserPresetsAndProtectsBuiltIns()
    {
        using var temp = new TempFolder();
        var path = temp.File("presets.json");
        var store = new PresetStore(path);

        var copy = store.Duplicate(BuiltInPresets.All[0]);
        store.Upsert(copy with { Name = "Mine", Quality = 42 });

        var reloaded = new PresetStore(path);
        var mine = Assert.Single(reloaded.All, p => !p.IsBuiltIn);
        Assert.Equal("Mine", mine.Name);
        Assert.Equal(42, mine.Quality);

        Assert.Throws<InvalidOperationException>(() => reloaded.Upsert(BuiltInPresets.All[0]));
        reloaded.Delete(mine.Id);
        Assert.DoesNotContain(new PresetStore(path).All, p => !p.IsBuiltIn);
    }

    [Fact]
    public void CorruptFileFallsBackToBuiltIns()
    {
        using var temp = new TempFolder();
        var path = temp.File("presets.json");
        File.WriteAllText(path, "{ not json");
        Assert.Equal(BuiltInPresets.All.Count, new PresetStore(path).All.Count);
    }

    [Fact]
    public void BuiltInIdsAreUnique() =>
        Assert.Equal(BuiltInPresets.All.Count, BuiltInPresets.All.Select(p => p.Id).Distinct().Count());
}

namespace Lemon.Core.Presets;

/// <summary>Built-in presets plus the user's own presets, which are persisted to presets.json.</summary>
public sealed class PresetStore
{
    private readonly string _path;
    private readonly List<Preset> _userPresets;

    public PresetStore(string? path = null)
    {
        _path = path ?? Path.Combine(JsonFileStore.DefaultDirectory, "presets.json");
        _userPresets = JsonFileStore.Load<List<Preset>>(_path)?.Select(p => p with { IsBuiltIn = false }).ToList() ?? [];
    }

    public event EventHandler? Changed;

    public IReadOnlyList<Preset> All => [.. BuiltInPresets.All, .. _userPresets];

    public Preset? Find(string id) => All.FirstOrDefault(p => p.Id == id);

    public void Upsert(Preset preset)
    {
        if (preset.IsBuiltIn)
            throw new InvalidOperationException("Built-in presets can't be modified. Duplicate it instead.");

        var index = _userPresets.FindIndex(p => p.Id == preset.Id);
        if (index >= 0)
            _userPresets[index] = preset;
        else
            _userPresets.Add(preset);
        Persist();
    }

    public void Delete(string id)
    {
        if (_userPresets.RemoveAll(p => p.Id == id) > 0)
            Persist();
    }

    public Preset Duplicate(Preset source)
    {
        var copy = source with { Id = Guid.NewGuid().ToString("n"), Name = source.Name + " copy", IsBuiltIn = false };
        Upsert(copy);
        return copy;
    }

    private void Persist()
    {
        JsonFileStore.Save(_path, _userPresets);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

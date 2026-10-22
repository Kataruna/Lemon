namespace Lemon.Core.Tests;

public sealed class TempFolder : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("lemon-tests-").FullName;

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

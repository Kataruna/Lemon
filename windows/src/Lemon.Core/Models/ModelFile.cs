using System.Security.Cryptography;

namespace Lemon.Core.Models;

/// <summary>
/// An AI model pinned to a URL and a SHA-256. Too big to ship in the app, so it's downloaded on request into
/// %LOCALAPPDATA%\Lemon\models.
/// </summary>
public abstract record ModelFile(string DisplayName, string Purpose, string FileName, string Url, string Sha256, long Bytes)
{
    public static string Folder { get; } = System.IO.Path.Combine(JsonFileStore.DefaultDirectory, "models");

    public string Path => System.IO.Path.Combine(Folder, FileName);

    public bool IsInstalled => File.Exists(Path);

    /// <summary>Downloads to a .part file, checks the hash and only then moves it into place.</summary>
    public async Task DownloadAsync(HttpClient http, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Folder);
        var partial = Path + ".part";
        try
        {
            using var response = await http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                    total += read;
                    progress?.Report(Math.Min(1, (double)total / Bytes));
                }
                if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"The downloaded {DisplayName} model is corrupt (hash mismatch). Try again.");
            }
            File.Move(partial, Path, overwrite: true);
        }
        finally
        {
            try { File.Delete(partial); } catch (IOException) { }
        }
    }
}

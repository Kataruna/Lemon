using System.Security.Cryptography;

namespace Lemon.Core.Models;

/// <summary>Which BiRefNet export a Remove background preset uses.</summary>
public enum BackgroundModel
{
    Lite,
    Full,
}

/// <summary>
/// The BiRefNet ONNX models (MIT licence, onnx-community on Hugging Face), pinned to a commit and a SHA-256.
/// Too big to ship in the app, so they're downloaded on request into %LOCALAPPDATA%\Lemon\models.
/// The installer (installer\Lemon.iss) downloads the same files; keep the two in sync.
/// </summary>
public sealed record BackgroundModelInfo(BackgroundModel Model, string DisplayName, string FileName, string Url, string Sha256, long Bytes)
{
    public static string Folder { get; } = System.IO.Path.Combine(JsonFileStore.DefaultDirectory, "models");

    public static IReadOnlyList<BackgroundModelInfo> All { get; } =
    [
        new(BackgroundModel.Lite, "BiRefNet Lite", "birefnet-lite.onnx",
            "https://huggingface.co/onnx-community/BiRefNet_lite-ONNX/resolve/de15b22ba131738a16dff04aab8bdf8dc32e3ac1/onnx/model.onnx",
            "5600024376f572a557870a5eb0afb1e5961636bef4e1e22132025467d0f03333", 224_005_088),
        new(BackgroundModel.Full, "BiRefNet", "birefnet.onnx",
            "https://huggingface.co/onnx-community/BiRefNet-ONNX/resolve/534d3c82d3bb8b2f0867db6dfbc3a525b8e42f67/onnx/model.onnx",
            "58f621f00f5d756097615970a88a791584600dcf7c45b18a0a6267535a1ebd3c", 972_666_916),
    ];

    public static BackgroundModelInfo Get(BackgroundModel model) => All.First(m => m.Model == model);

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

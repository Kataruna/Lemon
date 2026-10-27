namespace Lemon.Core.Models;

/// <summary>Which BiRefNet export a Remove background preset uses.</summary>
public enum BackgroundModel
{
    Lite,
    Full,
}

/// <summary>
/// The BiRefNet ONNX models (MIT licence, onnx-community on Hugging Face), pinned to a commit and a SHA-256.
/// The installer (installer\Lemon.iss) downloads the same files; keep the two in sync.
/// </summary>
public sealed record BackgroundModelInfo(BackgroundModel Model, string DisplayName, string Purpose, string FileName, string Url, string Sha256, long Bytes)
    : ModelFile(DisplayName, Purpose, FileName, Url, Sha256, Bytes)
{
    public static IReadOnlyList<BackgroundModelInfo> All { get; } =
    [
        new(BackgroundModel.Lite, "BiRefNet Lite", "Remove background. Fast, good for most pictures (recommended).", "birefnet-lite.onnx",
            "https://huggingface.co/onnx-community/BiRefNet_lite-ONNX/resolve/de15b22ba131738a16dff04aab8bdf8dc32e3ac1/onnx/model.onnx",
            "5600024376f572a557870a5eb0afb1e5961636bef4e1e22132025467d0f03333", 224_005_088),
        new(BackgroundModel.Full, "BiRefNet", "Remove background. Best edges on hair, fur and fine detail. Slower and needs a lot more memory.", "birefnet.onnx",
            "https://huggingface.co/onnx-community/BiRefNet-ONNX/resolve/534d3c82d3bb8b2f0867db6dfbc3a525b8e42f67/onnx/model.onnx",
            "58f621f00f5d756097615970a88a791584600dcf7c45b18a0a6267535a1ebd3c", 972_666_916),
    ];

    public static BackgroundModelInfo Get(BackgroundModel model) => All.First(m => m.Model == model);
}

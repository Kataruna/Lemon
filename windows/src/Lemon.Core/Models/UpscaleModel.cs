namespace Lemon.Core.Models;

/// <summary>Which Real-ESRGAN network an Upscale preset uses.</summary>
public enum UpscaleModel
{
    Lite,
    Full,
}

/// <summary>
/// The official Real-ESRGAN weights (BSD-3-Clause, xinntao/Real-ESRGAN), exported to ONNX by
/// packaging\export-upscale-models.py and published on Lemon's models-v1 release.
/// </summary>
public sealed record UpscaleModelInfo(UpscaleModel Model, string DisplayName, string Purpose, string FileName, string Url, string Sha256, long Bytes)
    : ModelFile(DisplayName, Purpose, FileName, Url, Sha256, Bytes)
{
    private const string Release = "https://github.com/Kataruna/Lemon/releases/download/models-v1/";

    public static IReadOnlyList<UpscaleModelInfo> All { get; } =
    [
        new(UpscaleModel.Lite, "Real-ESRGAN General", "Upscale. Fast, good for most pictures (recommended).", "realesrgan-general-x4v3.onnx",
            Release + "realesrgan-general-x4v3.onnx", "47710b200e8ea23e90cc4193bd9e6f3302f163db94865673c843dd12e2344b8a", 4_866_412),
        new(UpscaleModel.Full, "Real-ESRGAN x4plus", "Upscale. Sharper detail on photos. Much slower.", "realesrgan-x4plus.onnx",
            Release + "realesrgan-x4plus.onnx", "863cc55ef1f43a851418253eb475318634993fbe8c2c3aadec3737d727296e68", 67_051_634),
    ];

    public static UpscaleModelInfo Get(UpscaleModel model) => All.First(m => m.Model == model);
}

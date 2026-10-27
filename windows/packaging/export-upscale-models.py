"""Exports the official Real-ESRGAN weights (BSD-3-Clause, xinntao/Real-ESRGAN) to ONNX for Upscale.

Writes dist/Windows/models/realesrgan-general-x4v3.onnx and realesrgan-x4plus.onnx, which are published as
assets of the models-v1 GitHub release. Lemon.Core/Models/UpscaleModel.cs pins their URLs and SHA-256s, so a
re-export (other bytes) needs a new release and new hashes there.

Needs: pip install torch onnx onnxruntime (CPU torch is enough).
Run:   python windows/packaging/export-upscale-models.py
"""

import hashlib
import pathlib
import urllib.request

import numpy as np
import onnxruntime
import torch
from torch import nn
from torch.nn import functional as F

OUT = pathlib.Path(__file__).resolve().parents[2] / "dist" / "Windows" / "models"

# The networks, copied from upstream (forward passes unchanged, scale 4 only):
# BasicSR 8d56e3a basicsr/archs/rrdbnet_arch.py and Real-ESRGAN a4abfb2 realesrgan/archs/srvgg_arch.py.


class ResidualDenseBlock(nn.Module):
    def __init__(self, num_feat=64, num_grow_ch=32):
        super().__init__()
        self.conv1 = nn.Conv2d(num_feat, num_grow_ch, 3, 1, 1)
        self.conv2 = nn.Conv2d(num_feat + num_grow_ch, num_grow_ch, 3, 1, 1)
        self.conv3 = nn.Conv2d(num_feat + 2 * num_grow_ch, num_grow_ch, 3, 1, 1)
        self.conv4 = nn.Conv2d(num_feat + 3 * num_grow_ch, num_grow_ch, 3, 1, 1)
        self.conv5 = nn.Conv2d(num_feat + 4 * num_grow_ch, num_feat, 3, 1, 1)
        self.lrelu = nn.LeakyReLU(negative_slope=0.2, inplace=True)

    def forward(self, x):
        x1 = self.lrelu(self.conv1(x))
        x2 = self.lrelu(self.conv2(torch.cat((x, x1), 1)))
        x3 = self.lrelu(self.conv3(torch.cat((x, x1, x2), 1)))
        x4 = self.lrelu(self.conv4(torch.cat((x, x1, x2, x3), 1)))
        x5 = self.conv5(torch.cat((x, x1, x2, x3, x4), 1))
        return x5 * 0.2 + x


class RRDB(nn.Module):
    def __init__(self, num_feat, num_grow_ch=32):
        super().__init__()
        self.rdb1 = ResidualDenseBlock(num_feat, num_grow_ch)
        self.rdb2 = ResidualDenseBlock(num_feat, num_grow_ch)
        self.rdb3 = ResidualDenseBlock(num_feat, num_grow_ch)

    def forward(self, x):
        return self.rdb3(self.rdb2(self.rdb1(x))) * 0.2 + x


class RRDBNet(nn.Module):
    def __init__(self, num_in_ch=3, num_out_ch=3, num_feat=64, num_block=23, num_grow_ch=32):
        super().__init__()
        self.conv_first = nn.Conv2d(num_in_ch, num_feat, 3, 1, 1)
        self.body = nn.Sequential(*[RRDB(num_feat, num_grow_ch) for _ in range(num_block)])
        self.conv_body = nn.Conv2d(num_feat, num_feat, 3, 1, 1)
        self.conv_up1 = nn.Conv2d(num_feat, num_feat, 3, 1, 1)
        self.conv_up2 = nn.Conv2d(num_feat, num_feat, 3, 1, 1)
        self.conv_hr = nn.Conv2d(num_feat, num_feat, 3, 1, 1)
        self.conv_last = nn.Conv2d(num_feat, num_out_ch, 3, 1, 1)
        self.lrelu = nn.LeakyReLU(negative_slope=0.2, inplace=True)

    def forward(self, x):
        feat = self.conv_first(x)
        feat = feat + self.conv_body(self.body(feat))
        feat = self.lrelu(self.conv_up1(F.interpolate(feat, scale_factor=2, mode="nearest")))
        feat = self.lrelu(self.conv_up2(F.interpolate(feat, scale_factor=2, mode="nearest")))
        return self.conv_last(self.lrelu(self.conv_hr(feat)))


class SRVGGNetCompact(nn.Module):
    def __init__(self, num_in_ch=3, num_out_ch=3, num_feat=64, num_conv=32, upscale=4):
        super().__init__()
        self.upscale = upscale
        self.body = nn.ModuleList([nn.Conv2d(num_in_ch, num_feat, 3, 1, 1), nn.PReLU(num_parameters=num_feat)])
        for _ in range(num_conv):
            self.body.append(nn.Conv2d(num_feat, num_feat, 3, 1, 1))
            self.body.append(nn.PReLU(num_parameters=num_feat))
        self.body.append(nn.Conv2d(num_feat, num_out_ch * upscale * upscale, 3, 1, 1))
        self.upsampler = nn.PixelShuffle(upscale)

    def forward(self, x):
        out = x
        for layer in self.body:
            out = layer(out)
        return self.upsampler(out) + F.interpolate(x, scale_factor=self.upscale, mode="nearest")


# (onnx name, network, upstream checkpoint, its SHA-256)
MODELS = [
    ("realesrgan-general-x4v3.onnx", SRVGGNetCompact,
     "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesr-general-x4v3.pth",
     "8dc7edb9ac80ccdc30c3a5dca6616509367f05fbc184ad95b731f05bece96292"),
    ("realesrgan-x4plus.onnx", RRDBNet,
     "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.1.0/RealESRGAN_x4plus.pth",
     "4fa0d38905f75ac06eb49a7951b426670021be3018265fd191d2125df9d682f1"),
]


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def export(name, network, url, expected):
    checkpoint = OUT / url.rsplit("/", 1)[1]
    if not checkpoint.exists():
        urllib.request.urlretrieve(url, checkpoint)
    if sha256(checkpoint) != expected:
        raise SystemExit(f"{checkpoint.name}: SHA-256 is {sha256(checkpoint)}, expected {expected}")

    state = torch.load(checkpoint, map_location="cpu", weights_only=True)
    model = network()
    model.load_state_dict(state.get("params_ema", state.get("params")), strict=True)
    model.eval()

    target = OUT / name
    torch.onnx.export(model, torch.rand(1, 3, 64, 64), target, opset_version=17, dynamo=False,
                      input_names=["input"], output_names=["output"],
                      dynamic_axes={"input": {2: "height", 3: "width"}, "output": {2: "height", 3: "width"}})

    # Two sizes, so a size baked into the graph would show up.
    session = onnxruntime.InferenceSession(str(target))
    for h, w in [(48, 80), (96, 64)]:
        x = torch.rand(1, 3, h, w)
        with torch.no_grad():
            want = model(x).numpy()
        got = session.run(None, {"input": x.numpy()})[0]
        assert got.shape == (1, 3, h * 4, w * 4), got.shape
        assert np.abs(got - want).max() < 1e-3, np.abs(got - want).max()
    print(f"{name}  {target.stat().st_size} bytes  sha256 {sha256(target)}")


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for model in MODELS:
        export(*model)

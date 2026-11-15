# /// script
# requires-python = "==3.12.*"
# dependencies = [
#     "ultralytics>=8.4,<8.5",
#     "onnx>=1.23",
#     "onnxslim>=0.1.60",
#     "torch",
# ]
#
# [tool.uv]
# exclude-newer = "2026-11-15T00:00:00Z"
#
# # torch from the CPU-only index: avoids a multi-GB CUDA download (no GPU needed to export).
# [tool.uv.sources]
# torch = [
#     { index = "pytorch-cpu", marker = "sys_platform == 'linux' or sys_platform == 'win32'" },
# ]
#
# [[tool.uv.index]]
# name = "pytorch-cpu"
# url = "https://download.pytorch.org/whl/cpu"
# explicit = true
# ///
"""Reproducible export of YOLO11n (COCO) to ONNX for QualityVision.

Usage (from the repository root):  uv run tools/export-model/export.py
Result: models/yolo11n.onnx (opset 17, static input 1x3x640x640).
"""

from __future__ import annotations

import hashlib
import shutil
import sys
from pathlib import Path

import onnx
from ultralytics import YOLO

OPSET = 17
IMGSZ = 640
WEIGHTS = "yolo11n.pt"

SCRIPT_DIR = Path(__file__).resolve().parent
TARGET = SCRIPT_DIR.parent.parent / "models" / "yolo11n.onnx"


def dims(value_info: onnx.ValueInfoProto) -> list[int | str]:
    return [d.dim_value if d.HasField("dim_value") else d.dim_param
            for d in value_info.type.tensor_type.shape.dim]


def main() -> int:
    # 1. Load (and download if missing) the weights, cached next to this script.
    model = YOLO(str(SCRIPT_DIR / WEIGHTS))

    # 2. Export: static shapes, simplified graph.
    exported = Path(model.export(format="onnx", opset=OPSET, dynamic=False,
                                 imgsz=IMGSZ, simplify=True))

    # 3. Verify what the C# side relies on.
    proto = onnx.load(str(exported))
    onnx.checker.check_model(proto)
    opset = next(o.version for o in proto.opset_import if o.domain in ("", "ai.onnx"))
    inp, out = proto.graph.input[0], proto.graph.output[0]
    in_shape, out_shape = dims(inp), dims(out)

    errors = []
    if opset != OPSET:
        errors.append(f"opset {opset} != {OPSET}")
    if in_shape != [1, 3, IMGSZ, IMGSZ]:
        errors.append(f"input shape {in_shape} != [1, 3, {IMGSZ}, {IMGSZ}]")
    if out_shape != [1, 84, 8400]:
        errors.append(f"output shape {out_shape} != [1, 84, 8400]")
    if errors:
        print("VERIFICATION FAILED: " + "; ".join(errors), file=sys.stderr)
        return 1

    # 4. Publish into models/.
    TARGET.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(exported, TARGET)
    exported.unlink()  # drop the intermediate .onnx next to the .pt
    sha256 = hashlib.sha256(TARGET.read_bytes()).hexdigest()

    print("--- Export summary ---")
    print(f"file   : {TARGET} ({TARGET.stat().st_size / 1e6:.1f} MB)")
    print(f"sha256 : {sha256}")
    print(f"opset  : {opset}")
    print(f"input  : {inp.name} {in_shape}")
    print(f"output : {out.name} {out_shape}")
    print(f"classes: {len(model.names)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

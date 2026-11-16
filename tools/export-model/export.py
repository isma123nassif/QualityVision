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
"""Reproducible export of detection models to ONNX for QualityVision.

Usage (from the repository root):
  uv run tools/export-model/export.py [--size n|s|m]    # YOLO11 (COCO), same as --mode coco
  uv run tools/export-model/export.py --mode world      # YOLO-World v2 large, classes from world-classes.txt

coco  -> models/yolo11{size}.onnx (opset 17, static 1x3x640x640, output 1x84x8400). Default size: n.
world -> models/yoloworld-qv.onnx (opset 17, static 1x3x640x640, output 1x(4+N)x8400, N = lines of
         world-classes.txt; order matters: it defines the class ids). --size is ignored in this mode.
"""

from __future__ import annotations

import argparse
import ast
import hashlib
import shutil
import sys
from pathlib import Path

import onnx
from ultralytics import YOLO, YOLOWorld

OPSET = 17
IMGSZ = 640
WORLD_WEIGHTS = "yolov8l-worldv2.pt"

SCRIPT_DIR = Path(__file__).resolve().parent
MODELS_DIR = SCRIPT_DIR.parent.parent / "models"
WORLD_CLASSES = SCRIPT_DIR / "world-classes.txt"


def dims(value_info: onnx.ValueInfoProto) -> list[int | str]:
    return [d.dim_value if d.HasField("dim_value") else d.dim_param
            for d in value_info.type.tensor_type.shape.dim]


def read_classes(path: Path) -> list[str]:
    names = [line.strip() for line in path.read_text(encoding="utf-8").splitlines()]
    names = [n for n in names if n and not n.startswith("#")]
    if len(set(names)) != len(names):
        raise SystemExit(f"{path.name}: duplicate class names")
    return names


def main() -> int:
    parser = argparse.ArgumentParser(description="Export detection models to ONNX.")
    parser.add_argument("--mode", choices=["coco", "world"], default="coco",
                        help="coco: YOLO11 COCO (default); world: YOLO-World open-vocabulary")
    parser.add_argument("--size", choices=["n", "s", "m"], default="n",
                        help="YOLO11 variant for --mode coco (default: n)")
    args = parser.parse_args()

    expected_names: list[str] | None = None
    if args.mode == "world":
        expected_names = read_classes(WORLD_CLASSES)
        n_classes = len(expected_names)
        target = MODELS_DIR / "yoloworld-qv.onnx"
        # Load (download if missing) the weights and bake the custom vocabulary into the model.
        model = YOLOWorld(str(SCRIPT_DIR / WORLD_WEIGHTS))
        model.set_classes(expected_names)
    else:
        n_classes = 80
        target = MODELS_DIR / f"yolo11{args.size}.onnx"
        model = YOLO(str(SCRIPT_DIR / f"yolo11{args.size}.pt"))

    # Export: static shapes, simplified graph.
    exported = Path(model.export(format="onnx", opset=OPSET, dynamic=False,
                                 imgsz=IMGSZ, simplify=True))

    # Verify what the C# side relies on.
    proto = onnx.load(str(exported))
    onnx.checker.check_model(proto)
    opset = next(o.version for o in proto.opset_import if o.domain in ("", "ai.onnx"))
    inp, out = proto.graph.input[0], proto.graph.output[0]
    in_shape, out_shape = dims(inp), dims(out)
    expected_out = [1, 4 + n_classes, 8400]

    errors = []
    if opset != OPSET:
        errors.append(f"opset {opset} != {OPSET}")
    if in_shape != [1, 3, IMGSZ, IMGSZ]:
        errors.append(f"input shape {in_shape} != [1, 3, {IMGSZ}, {IMGSZ}]")
    if out_shape != expected_out:
        errors.append(f"output shape {out_shape} != {expected_out}")
    if expected_names is not None:
        meta = {p.key: p.value for p in proto.metadata_props}
        try:
            names = ast.literal_eval(meta.get("names", ""))
            got = [names[i] for i in range(len(names))]
        except (ValueError, SyntaxError, KeyError):
            got = None
        if got != expected_names:
            errors.append(f"names metadata {got} != {expected_names}")
    if errors:
        print("VERIFICATION FAILED: " + "; ".join(errors), file=sys.stderr)
        return 1

    # Publish into models/.
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(exported, target)
    exported.unlink()  # drop the intermediate .onnx next to the .pt
    sha256 = hashlib.sha256(target.read_bytes()).hexdigest()

    print("--- Export summary ---")
    print(f"file   : {target} ({target.stat().st_size / 1e6:.1f} MB)")
    print(f"sha256 : {sha256}")
    print(f"opset  : {opset}")
    print(f"input  : {inp.name} {in_shape}")
    print(f"output : {out.name} {out_shape}")
    print(f"classes: {n_classes}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

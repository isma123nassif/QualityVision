# Models

## yolo11n.onnx

- **Origin:** Ultralytics YOLO11n (COCO, 80 classes), https://github.com/ultralytics/ultralytics
- **Licence:** AGPL-3.0 (code and weights). See ADR-003, section Licensing (incl. §13 network use).
- **Export:** `uv run tools/export-model/export.py` (ultralytics 8.4.x)
- **Format:** ONNX opset 17, input `images` 1x3x640x640, output `output0` 1x84x8400
- **Size:** 10,741,396 bytes (~10.7 MB)
- **SHA256:** `7ab7b60b4e28c307e53f2a85920b9cc3fa82f23511e8fd5793ad1d44eba7f50b`  (compute: `sha256sum models/yolo11n.onnx`)

# Architecture Decision Records

Decisions are recorded in [MADR](https://adr.github.io/madr/) style. New ADRs get the next number;
accepted ADRs are not edited — they are superseded by a new ADR. Exception: clarifications made
before the affected code exists are recorded in the ADR's own "Amendments" section.

| ADR | Title | Status | Date |
|---|---|---|---|
| ADR-001 | Layered architecture and inspection pipeline | Accepted (amended) | 2026-11-15 |
| ADR-002 | Rules engine boundary and debounce unit | Accepted | 2026-11-15 |
| ADR-003 | Object detection model and inference runtime | Accepted (partly superseded by ADR-004) | 2026-11-15 |
| ADR-004 | GPU acceleration (CUDA) with automatic CPU fallback | Accepted | 2026-11-15 |
| ADR-005 | Open-vocabulary model (YOLO-World v2) for the marker pen | Accepted | 2026-11-16 |

Summaries of the decisions:

- **ADR-001** — Web → Infrastructure → Core layering, enforced by architecture tests; LAN-only, no authentication by design.
- **ADR-002** — The rules engine lives in Core and is pure; debounce is counted in frames (flicker is a per-frame detector phenomenon), not seconds.
- **ADR-003** — YOLO11 exported to ONNX and run through YoloDotNet instead of a hand-written inference stack.
- **ADR-004** — Use CUDA when available and fall back automatically to CPU.
- **ADR-005** — YOLO-World v2 (open vocabulary) detects the marker pen, which COCO-trained YOLO11 cannot.

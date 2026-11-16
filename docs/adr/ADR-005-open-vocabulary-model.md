# ADR-005: Open-vocabulary model (YOLO-World v2) for the marker pen

- **Status:** Accepted
- **Date:** 2026-11-16
- **Deciders:** architect, orchestrator, project owner
- **Refines:** [ADR-003](ADR-003-object-detection-model.md), [ADR-004](ADR-004-gpu-acceleration.md) (GPU model choice)

## Context

The object the demo must inspect is a **marker pen**. It is **not a COCO class**, so YOLO11 (COCO, 80 classes)
cannot name it, no matter which size or threshold is used. The demo is shown in an interview today, so the
solution must work on the real desk with the real object and fit in the time left.

## Options considered

1. **Lower the confidence threshold on YOLO11.** Useless: the class does not exist in the model; we would only
   get more false positives of other classes.
2. **Fine-tune a custom YOLO11 on our own photos.** Best long-term option (highest accuracy, fixed and
   explainable class set), but it needs a labelled dataset, training and validation. Not feasible in the time left.
3. **Open-vocabulary detector (YOLO-World v2 / YOLOE).** Classes are given as text. Tested on the real desk:
   - YOLO-World v2 L (`yolov8l-worldv2.pt`): marker pen 0.86–0.97, cup 0.94, bottle 0.91, cell phone 0.46–0.71.
   - YOLOE-11-L: found nothing on a product photo.

## Decision

Option 3 with **YOLO-World v2 L**:

1. **Fixed text classes baked in at export** (`set_classes([...])`, then ONNX export). The result is a plain YOLO
   ONNX (input `1x3x640x640`, output `1x(4+14)x8400`, class names in metadata, task `detect`), which YoloDotNet
   runs unchanged. **No C# change.**
2. **Class list (order matters):** marker pen, bottle, cup, cell phone, banana, scissors, person, pencil, cable,
   reed diffuser, notebook, laptop, keyboard, computer mouse. Source: `tools/export-model/world-classes.txt`.
3. **Distractor classes mapped to Ignore** in `rules.json` (open-vocabulary negative prompting). At conf 0.25
   there were false "marker pen" hits at 0.25–0.36; giving look-alikes (pencil, cable, reed diffuser, …) their own
   class absorbs them, and the threshold is raised to **conf 0.45**.
4. **CPU fallback unchanged:** YOLO11n (COCO) stays for the CPU path and the existing tests; YOLO11m remains the
   COCO GPU model. The world model is `models/yoloworld-qv.onnx` (177.2 MB).

## Consequences

- **Latency:** ~10 ms on CUDA vs 6.5 ms for yolo11m. Still far below the frame interval.
- **CPU fallback does not detect the marker pen** (COCO classes only). Acceptable for the demo; documented.
- **Changing the class list needs a re-export (seconds), not retraining.** The C# code stays as it is; only
  `rules.json` is adapted.
- **Distractor list is part of the tuning:** a new look-alike causing false positives is fixed by adding it to the
  list and mapping it to Ignore.
- **Licence:** YOLO-World is GPL-3.0 (Tencent AILab) and Ultralytics is AGPL-3.0: same commercial caveat as
  ADR-003 (a commercial product needs a commercial licence or an alternative model).
- **Model not in git:** 177 MB exceeds GitHub's 100 MB limit. The file is gitignored and reproducible from the
  export script (`tools/export-model/export.py`, world mode); SHA256 recorded in `models/README.md`.
  Unlike ADR-003, Python/`uv` is therefore needed once to obtain this model.
- **Professional path:** in a real customer project we would collect and label images of the real parts and
  fine-tune a YOLO model (option 2). Open vocabulary is the pragmatic bridge for a demo, not the end state.

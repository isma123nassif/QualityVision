# Exportación del modelo (YOLO11 n/s/m → ONNX)

## Por qué Python solo aquí

La aplicación es 100 % C#/.NET: en tiempo de ejecución se cargan `models/yolo11m.onnx` (GPU, CUDA) o
`models/yolo11n.onnx` (CPU, respaldo) con YoloDotNet (ONNX Runtime; véase
[ADR-004](../../docs/adr/ADR-004-gpu-acceleration.md)). Python **únicamente** hace falta una vez para convertir los pesos
PyTorch (`yolo11n.pt`) a ONNX. El resultado se versiona en `models/`, así que quien clone el
repositorio no necesita Python para compilar, ejecutar ni probar la aplicación.

## Cómo ejecutarlo

Requisito: [uv](https://docs.astral.sh/uv/). Desde la raíz del repositorio:

```bash
uv run tools/export-model/export.py              # yolo11n (por defecto)
uv run tools/export-model/export.py --size m     # yolo11m, modelo de GPU
```

`--size n|s|m` (por defecto `n`) elige la variante: exporta `yolo11{size}.pt` a
`models/yolo11{size}.onnx`. Los pesos se descargan junto al script si faltan.

El script declara sus dependencias con metadatos en línea (PEP 723): Python 3.12,
`ultralytics` 8.4.x, `onnx` ≥ 1.23, `onnxslim` y `torch`. `uv` crea un entorno aislado y no toca el
sistema.

### torch desde el índice CPU

El `torch` de PyPI en Linux arrastra varios GB de librerías CUDA que aquí no sirven para nada
(se exporta en CPU). Por eso el bloque PEP 723 fija `torch` al índice
`https://download.pytorch.org/whl/cpu` mediante `[tool.uv.sources]` y `[[tool.uv.index]]`
(índice `explicit`: solo `torch` sale de ahí). Si tu versión de `uv` ignora esa sección,
ejecútalo manualmente:

```bash
uv run --index https://download.pytorch.org/whl/cpu tools/export-model/export.py
```

## Qué hace

1. Carga `yolo11{size}.pt` (lo descarga junto al script si no existe).
2. Exporta con `opset=17, dynamic=False, imgsz=640, simplify=True`.
3. Verifica con `onnx` el opset, la entrada y la salida; si algo no coincide, termina con error.
4. Copia el resultado a `models/yolo11{size}.onnx`, borra el `.onnx` intermedio junto al `.pt` e imprime un
   resumen con el SHA-256 del fichero.

Las dependencias quedan acotadas en el tiempo con `exclude-newer = "2026-11-15T00:00:00Z"`
(`[tool.uv]`) para que la exportación sea reproducible.

## Qué verificar

Valores obtenidos con ultralytics 8.4.214, onnx 1.23.2, torch 2.15 y Python 3.12:

| Propiedad | Valor esperado |
|---|---|
| Tamaño | n ≈ 10,7 MB · m ≈ 80,7 MB |
| Opset | 17 |
| Entrada | `images` `[1, 3, 640, 640]` (estática) |
| Salida | `output0` `[1, 84, 8400]` (4 coordenadas + 80 clases COCO, 8400 anclas) |

Comprobación funcional: el test de integración con `bus.jpg` debe detectar `bus` y `person`
con confianza ≥ 0,5.

## Licencia

Los pesos y el código de Ultralytics YOLO11 son **AGPL-3.0**. Vale para esta demo; para un
producto comercial hay que resolver la licencia (véase [ADR-003](../../docs/adr/ADR-003-object-detection-model.md)).
`yolo11n.pt` y `.venv/` no deben commitearse.

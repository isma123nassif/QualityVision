# samples/ — vídeo del Plan B

Si la cámara falla durante la demo, la app puede reproducir en bucle un vídeo grabado previamente (`samples/demo.mp4`).

## Grabar

Con la cámara real funcionando, arranca la app con la grabación activada:

```bash
Camera__RecordTo=samples/demo.mp4 ASPNETCORE_ENVIRONMENT=Production dotnet run --project src/QualityVision.Web --no-launch-profile
```

- Se graba cada fotograma crudo (sin anotaciones) a 15 fps, en mp4 (códec mp4v), con el tamaño del primer fotograma.
- Se detiene a los `Camera__RecordMaxSeconds` segundos (por defecto 120) o al cerrar la app; el log muestra `Recorded N frames to <ruta>`.
- Una ruta relativa se resuelve contra el *content root*: con `dotnet run` es `src/QualityVision.Web/`, así que usa una ruta absoluta
  (p. ej. `Camera__RecordTo=$PWD/samples/demo.mp4`) para que acabe en esta carpeta.
- Si la grabación falla, se registra un error una sola vez y se desactiva; la captura nunca se interrumpe.

## Reproducir

```bash
./run-demo.sh --video
```

`run-demo.sh` ejecuta desde `artifacts/demo`, por eso pasa la ruta **absoluta** `<repo>/samples/demo.mp4` en
`Camera__VideoFile` y fija `Camera__Source=VideoFile`. Falla con un mensaje claro si el fichero no existe.

Ojo: no grabes con `Camera__RecordTo` apuntando al mismo `samples/demo.mp4` que se reproduce con `--video`; se sobrescribiría el fichero en uso.
Un `Camera__VideoFile` relativo tampoco se resuelve desde `artifacts/demo` (usa una ruta absoluta o `--video`).

## Git

`samples/*.mp4` solo se versiona si pesa menos de 50 MB; si no, no lo subas.

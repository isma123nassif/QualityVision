# QualityVision — Plan maestro

> Demo de control de calidad industrial por visión artificial (C#/.NET 10).
> Entrevista: **lunes 16-nov-2026**. Hoy: sábado 14-nov-2026 → ~2 días de trabajo.
> Briefing literal: [`00-briefing/PROMPT_ORIGINAL.md`](00-briefing/PROMPT_ORIGINAL.md)

---

## 1. Cómo trabajamos (software factory)

| Rol | Quién | Hace | No hace |
|---|---|---|---|
| **Orquestador** | Claude (sesión principal) | Planifica, trocea, delega, integra, ejecuta quality gates, git | Escribir código de producto |
| `architect` | subagente | Estructura, contratos entre capas, ADRs (`docs/adr/`) | Implementar |
| `backend-dev` | subagente | Pipeline, DI, BackgroundServices, EF Core, endpoints | UI |
| `vision-engineer` | subagente | Cámara/vídeo (OpenCvSharp), ONNX, YoloDotNet, rendimiento, dispose | UI |
| `frontend-dev` | subagente | Blazor, dashboard, SignalR cliente, UX en alemán | Lógica de dominio |
| `qa-engineer` | subagente | Tests xUnit (RulesEngine, debounce, zona), verificación | Código de producto |
| `code-reviewer` | subagente | Revisa SOLID, async, fugas, seguridad → informe | **Modificar código** |
| `tech-writer` | subagente | README + Mermaid, `docs/aprendizaje/MX.md`, kit de entrevista | Código |

**Motor de los subagentes:** la cuota de OpenCode Go está agotada (vuelve en ~9 días, después de la entrevista). Los agentes se ejecutan como **Claude en Konsole visible** (escritorio *Desarrollo*), con el modelo según complejidad: Haiku para tareas mecánicas, Sonnet para la mayoría, Opus para arquitectura/visión/review. Cada encargo incluye la definición del rol de `.claude/agents/<rol>.md`.

**Definición de "milestone cerrado" (Definition of Done):**
1. `dotnet build -warnaserror` → 0 warnings, 0 errores
2. `dotnet test` → verde
3. `dotnet format --verify-no-changes` → limpio
4. Informe del `code-reviewer` sin hallazgos bloqueantes (los menores, anotados)
5. Verificación funcional real (navegador / curl / captura)
6. `docs/aprendizaje/MX.md` escrito en español
7. Commit Conventional Commits (`feat(m2): ...`)
8. **PARADA → espero tu OK**

---

## 2. Mejoras de arquitectura que propongo

Sobre tu diseño (que mantengo), estas mejoras — todas baratas y defendibles en entrevista:

1. **La BD no frena la inferencia.** Segundo `Channel<InspectionEvent>` → `EventPersistenceService` (BackgroundService) que escribe en SQLite. Si SQLite tarda, la detección sigue a sus FPS.
2. **Codificar el JPEG una sola vez.** `DetectionService` publica el último frame anotado (ya en JPEG) en un `FrameBroadcaster` singleton; el endpoint `/stream` lo sirve a N clientes sin re-codificar. Coste O(1) por cliente.
3. **`IFrameSource` como abstracción** con dos implementaciones: `OpenCvFrameSource` (índice de webcam, URL MJPEG/RTSP como DroidCam, o archivo de vídeo en bucle) y `PhoneFrameSource` (el navegador del iPhone hace `POST /api/camera/frame`). Plan B = cambiar una línea de config (`Camera:Source`). Patrón Strategy.
4. **`TimeProvider` (.NET 8+)** inyectado en Core → debounce y estadísticas por minuto testeables sin `Thread.Sleep`.
5. **Reglas en archivo aparte (`rules.json`)** cargado con `reloadOnChange`. El editor web escribe solo ese archivo → `IOptionsMonitor` lo recarga. Nunca reescribimos `appsettings.json`.
6. **Core sin dependencias de verdad:** Core define `Detection` (clase, confianza, caja) y `InspectionZone` con sus propios tipos; Infrastructure traduce desde YoloDotNet. El dominio no sabe que existe YOLO (Clean/Hexagonal light).
7. **Debounce por clase y honesto:** YOLO no da identidad a los objetos, así que el contador es "por clase presente en la zona" con máquina de estados *Absent → Present* tras N frames ausente. Limitación conocida (dos tazas a la vez cuentan 1) → se documenta en un ADR como trade-off consciente; mejora futura: tracker (SORT/ByteTrack).
8. **Modelo `.onnx` versionado en el repo** (~10 MB) + script de exportación reproducible. El lunes no puede depender de internet ni de Python.

Lo que **no** haré (sobreingeniería): MediatR, CQRS, Docker, autenticación, GPU, microservicios, tracker propio.

---

## 3. Pasos y subpasos por milestone

### M0 · Entorno ✅ comprobado (pendiente tu permiso para instalar)
- [x] 0.1 `dotnet --list-sdks` → **solo 8.0.133. Falta .NET 10.**
- [x] 0.2 Webcam → **no hay ningún `/dev/video*`. La cámara no está conectada/detectada.** `v4l2-ctl` sí está instalado; tu usuario ya está en el grupo `video`.
- [x] 0.3 Python → 3.14.10 (demasiado nuevo para `ultralytics`/`onnx`; usaré Python 3.12 gestionado por `uv`). **`uv` no está instalado.**
- [x] 0.4 Extras: git, gh, ffmpeg ✅ · CPU 12 hilos, 30 GB RAM, 70 GB libres en /home ✅
- [ ] 0.5 **Instalar (requiere sudo, pide tu permiso):** `sudo pacman -S dotnet-sdk aspnet-runtime uv` (SDK 10.0.14, convive con el 8)
- [x] 0.6 Cámara = **iPhone con DroidCam** en `http://192.168.18.93:4747/video` → MJPEG 1280x720, ~30 FPS medidos con ffmpeg desde el PC ✅ (versión gratis: marca de agua pequeña "using droidcam.app" arriba a la izquierda). Ver `investigacion/demo-por-meet.md`
- [ ] 0.7 Re-verificar: `dotnet --list-sdks` muestra 10.x, `uv --version`
- [ ] 0.8 Escribir `docs/aprendizaje/M0.md`

### M1 · Scaffolding, agentes y ADR-001
- 1.1 `git init`, `.gitignore` (dotnet + `*.db` + `.venv/`), rama `main`
- 1.2 `global.json` (fija SDK 10), `Directory.Build.props` (net10.0, Nullable, TreatWarningsAsErrors, ImplicitUsings, AnalysisLevel latest), `Directory.Packages.props` (CPM), `.editorconfig`
- 1.3 Solución `QualityVision.slnx` con 4 proyectos y referencias: Web → Infrastructure → Core; Tests → Core (+Infrastructure)
- 1.4 Proyecto Web = plantilla *Blazor Web App, Interactive Server* (.NET 10)
- 1.5 `.claude/agents/*.md` × 7 (frontmatter `name`, `description`, `tools`; reviewer sin Edit/Write)
- 1.6 `CLAUDE.md` del proyecto (convenciones, DoD, comandos)
- 1.7 `docs/adr/ADR-001-architecture.md` (capas, pipeline, mejoras de §2) — *architect*
- 1.8 Test "humo" vacío en xUnit para que el pipeline de gates funcione
- 1.9 Gates + review + `docs/aprendizaje/M1.md` + commit `chore(m1): scaffold solution` → **PARADA**

### M2 · Captura y MJPEG en el navegador
- 2.1 Core: `Frame` (wrapper con `IDisposable`), `IFrameSource`, opciones `CameraOptions` (`Source = Phone|Url|Webcam|VideoFile`) — *backend-dev*
- 2.2 Infra: `OpenCvFrameSource` con OpenCvSharp: URL (backend FFmpeg, DroidCam), índice de webcam (V4L2) y archivo en bucle respetando FPS — *vision-engineer*
- 2.2b Web: `PhoneFrameSource` + `POST /api/camera/frame` (límite de tamaño, solo JPEG) + página de captura `/kamera` portada del spike + endpoint HTTPS de Kestrel (`Kestrel:Endpoints`, HTTP 5080 y HTTPS 5443 con certificado fuera del repo) — *backend-dev* + *frontend-dev*
- 2.3 `CameraService : BackgroundService` → `Channel<Frame>` acotado (capacidad 2, `DropOldest`, dispose del frame descartado vía `itemDropped`) — *backend-dev*
- 2.4 `FrameBroadcaster` + endpoint `GET /stream` (`multipart/x-mixed-replace`), con `CancellationToken` del cliente — *backend-dev*
- 2.5 Página Blazor mínima con `<img src="/stream">` y la Prüfzone pintada; traducir al alemán los textos de la plantilla (reconexión, 404, error) — *frontend-dev*
- 2.6 Reconexión si la cámara se cae (reintento con backoff, log)
- 2.7 Verificación: vídeo en el navegador, memoria estable 2 min (`dotnet-counters`)
- 2.8 Gates + review + M2.md + commit → **PARADA**

### M3 · Modelo YOLO11n y detección
- 3.1 `tools/export-model/` con `uv`: Python 3.12, `ultralytics`, export `yolo11n.pt` → ONNX opset 17, `dynamic=False`, imgsz 640 — *vision-engineer*
- 3.2 Verificar el ONNX (shape de entrada `1x3x640x640`, opset) y guardarlo en `models/yolo11n.onnx`
- 3.3 Infra: `YoloObjectDetector : IObjectDetector` (singleton, YoloDotNet CPU), mapeo a `Detection` del Core — *vision-engineer*
- 3.4 `DetectionService : BackgroundService` lee del Channel, detecta, mide ms de inferencia, dibuja cajas, publica JPEG; `using` estricto de `Mat`/`SKBitmap` — *vision-engineer*
- 3.5 Benchmark: FPS y ms/inferencia en tu CPU; umbral de confianza en config
- 3.6 Verificación con botella/taza/móvil delante de la cámara
- 3.7 Gates + review (foco: fugas nativas) + M3.md + commit → **PARADA**

### M4 · RulesEngine, zona, debounce, recarga en caliente
- 4.1 Core: `Verdict` (OK/NOK/DANGER/UNKNOWN/IGNORE), `RuleSet`, `RulesEngine.Evaluate(Detection)` con fallback UNKNOWN — *backend-dev*
- 4.2 Core: `InspectionZone` (rectángulo normalizado 0..1, centro de la caja dentro de la zona)
- 4.3 Core: `ZoneEntryTracker` (debounce por clase, N frames ausente, `TimeProvider`)
- 4.4 `rules.json` + `IOptionsMonitor<RulesOptions>` + validación (`ValidateOnStart`, DataAnnotations)
- 4.5 Colores por veredicto en las cajas y en la zona
- 4.6 Tests xUnit (paralelo a 4.1-4.3, *qa-engineer*): reglas, desconocidos, mayúsculas, ignorados, zona en bordes, debounce (entrada, parpadeo de 1 frame, salida/reentrada, N configurable), recarga
- 4.7 Gates + review + M4.md + commit → **PARADA**

### M5 · Dashboard en tiempo real y editor de reglas
- 5.1 `InspectionHub` (SignalR) + `DashboardNotifier` que emite eventos y métricas (throttle ~4 Hz) — *backend-dev*
- 5.2 Servicio de estadísticas: contadores OK/NOK/DANGER, Ausschussquote %, FPS, ms inferencia, serie por minuto
- 5.3 UI alemana (*frontend-dev*, en paralelo con 5.1-5.2 contra un contrato acordado):
  - KPIs (Gut / Ausschuss / Gefahr / Ausschussquote / FPS / Inferenz ms)
  - Gráfica por minuto (Chart.js vía JS interop, sin librerías pesadas)
  - Ereignisprotokoll en vivo (últimos 50)
  - Alarma visual + sonora para NOK (`cup`)
- 5.4 Editor de reglas (`/regeln`): tabla clase → veredicto, guardar → `rules.json` → recarga en caliente visible al momento
- 5.5 Verificación E2E en navegador (Playwright, captura de pantalla)
- 5.6 Gates + review + M5.md + commit → **PARADA**

### M6 · Persistencia, CSV y Plan B (vídeo)
- 6.1 EF Core + SQLite: `InspectionEventEntity`, `QualityVisionDbContext`, `EnsureCreated` (o migración inicial) — *backend-dev*
- 6.2 `EventPersistenceService` desde el segundo Channel (mejora §2.1)
- 6.3 `GET /api/events/export.csv` con streaming (`IAsyncEnumerable`), cabeceras y escape CSV correctos
- 6.4 Botón "CSV exportieren" + historial al arrancar desde BD
- 6.5 Fallback automático al vídeo en bucle si se pierde la fuente en vivo + indicador en el dashboard — *vision-engineer* (paralelo a 6.1-6.3)
- 6.6 Grabar `samples/demo.mp4` (botella, taza, tijeras, móvil) con ffmpeg y tu webcam
- 6.7 Cambio de fuente por config (`Camera:Source = Phone|Url|Webcam|VideoFile`), probado en todos los modos disponibles
- 6.8 Tests: persistencia (SQLite en memoria), formato CSV
- 6.9 Gates + review + M6.md + commit → **PARADA**

### M7 · Pulido
- 7.1 Logging estructurado (Serilog consola con plantillas; scopes por frame donde aporte)
- 7.2 Health checks `/health`: cámara viva (último frame < 2 s), modelo cargado, BD accesible
- 7.3 Manejo de errores: cámara desconectada → banner "Kamera getrennt" en la UI, sin crash
- 7.4 README (*tech-writer*): qué es, captura, arquitectura Mermaid (componentes + secuencia del pipeline), cómo ejecutar, decisiones, limitaciones
- 7.5 Script `run-demo.sh` (modo móvil / modo vídeo) con `ASPNETCORE_ENVIRONMENT=Production` (nada de páginas de excepción ante el entrevistador)
- 7.6 Ensayo general: 10 min seguidos, memoria y CPU estables
- 7.7 Gates + review final completa + M7.md + commit → **PARADA**

### M8 · Kit de entrevista (*tech-writer*, revisado por mí)
- 8.1 `docs/entrevista/PITCH_DE.md` — 2 min en alemán
- 8.2 `docs/entrevista/DEMO_DE.md` — guion paso a paso (incluye qué hacer si falla la cámara → Plan B)
- 8.3 `docs/entrevista/FRAGEN_DE.md` — 15 preguntas DE + respuesta corta DE + traducción ES
- 8.4 `docs/entrevista/WORKFLOW_DE.md` — orquestador, subagentes, quality gates, en alemán sencillo
- 8.5 Commit `docs(m8): interview kit` + tag `v1.0-demo` → **FIN**

---

## 4. Paralelismo previsto

```
M1: architect(ADR) ║ backend-dev(scaffold) → tech-writer(M1.md)
M2: vision(WebcamSource) ║ backend(Channel+MJPEG) → frontend(página)
M3: vision(export ONNX) → vision(detector) → reviewer
M4: backend(RulesEngine/Tracker) ║ qa(tests contra la interfaz) → reviewer
M5: backend(Hub+stats) ║ frontend(dashboard contra contrato) → reviewer
M6: backend(EF+CSV) ║ vision(VideoFileSource) → qa → reviewer
```

## 5. Riesgos

| Riesgo | Mitigación |
|---|---|
| Webcam no detectada (hoy pasa) | Plan B (vídeo) adelantado si hace falta; grabar `demo.mp4` en cuanto funcione |
| YoloDotNet / OpenCvSharp aún sin soporte oficial .NET 10 | Comprobar en M1 con un build mínimo; fallback: paquetes net8.0 funcionan en net10 |
| CPU lenta → pocos FPS | YOLO11n a 640 en 12 hilos ≈ 15-30 FPS esperado; si no, inferir 1 de cada 2 frames |
| Fugas de memoria nativa | `using` estricto, `itemDropped` en el Channel, prueba de 10 min con `dotnet-counters` |
| Falta de tiempo | M7/M8 tienen prioridad sobre extras; cada milestone deja algo demostrable |

## 6. Registro de decisiones
| Fecha | Decisión | Motivo |
|---|---|---|
| 14-nov | Agentes = Claude en Konsole (no OpenCode) | Cuota OpenCode agotada hasta ~23-nov |
| 14-nov | Cámara de objetos = iPhone (DroidCam, URL MJPEG); demo corre en el PC Linux y el portátil Win11 comparte la pestaña en Meet | Webcam del portátil ocupada por Meet |
| 15-nov | M1: cerrados los hallazgos de `docs/reviews/M1-review.md` antes de M2 | Revisión con veredicto APPROVE + 5 MAJOR |
| 15-nov | Añadido `SkiaSharp.NativeAssets.Linux.NoDependencies` 3.119.1 (misma versión que exige YoloDotNet 4.2.0) | Sin él, YoloDotNet falla en Linux en tiempo de ejecución; verificado con un proyecto de prueba: OpenCV 4.13 + FFmpeg, SkiaSharp y ONNX Runtime CPU cargan |
| 15-nov | `RulesEngine` puro en Core (`Evaluate(Detection, RuleSet)`); `IOptionsMonitor` solo en el adaptador de Infra/Web | Core no puede depender de `Microsoft.Extensions.Options` |
| 15-nov | Debounce medido en frames de ausencia (N configurable) | Simple y testeable; dependencia de los FPS documentada |
| 14-nov | Python 3.12 vía `uv` solo para exportar | 3.14 del sistema sin wheels de `ultralytics`/`onnx` fiables |

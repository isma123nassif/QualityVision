# Briefing original del proyecto (14-nov-2026)

> Texto literal del encargo. Es la fuente de verdad del alcance; cualquier cambio de alcance se anota en `docs/PLAN.md` → "Registro de decisiones".

Actúa como ORQUESTADOR de una "software factory" con IA. Vamos a construir QualityVision: una demo de control de calidad industrial por visión artificial en C#/.NET 10 que tengo que presentar el lunes en una entrevista técnica para un puesto de Fullstack C# en una consultora alemana con clientes industriales. Debe ser visual, robusta y de calidad profesional. Además YO tengo que entender cada decisión y poder explicarla.

## Producto
- Webcam en directo en el navegador con una "Prüfzone" (zona de inspección) en el centro.
- Detección de objetos con YOLO11n (ONNX, opset 17, no dinámico) usando YoloDotNet en CPU.
- Reglas configurables: bottle=OK (verde), cell phone=OK (verde), cup=NOK (rojo + alarma), scissors=DANGER (naranja), person=ignorar, resto=UNKNOWN (gris). En appsettings.json con IOptionsMonitor (recarga en caliente) y editables desde la web.
- Contar sin duplicar: un objeto cuenta solo cuando ENTRA en la zona tras N frames ausente (debounce).
- Dashboard: contadores OK/NOK, Ausschussquote %, FPS, ms de inferencia, gráfica por minuto, registro de eventos en vivo.
- Persistencia de eventos con EF Core + SQLite y exportación a CSV.
- Plan B obligatorio: modo demo con archivo de vídeo en lugar de la webcam (configurable).
- UI en alemán (es para entrevistadores alemanes).

## Arquitectura (propón mejoras si las ves)
- Solución con: QualityVision.Core (dominio + RulesEngine, C# puro, sin dependencias), QualityVision.Infrastructure (cámara con OpenCvSharp4 + OpenCvSharp4.official.runtime.linux-x64, detección con YoloDotNet, EF Core), QualityVision.Web (Blazor Server, SignalR hub, endpoint MJPEG /stream), QualityVision.Tests (xUnit).
- Pipeline: CameraService (BackgroundService) -> Channel<Frame> acotado con DropOldest -> DetectionService -> RulesEngine -> SignalR + BD.
- Modelo YOLO como Singleton. Dispose estricto de Mat y SKBitmap en cada frame.
- Directory.Build.props (Nullable enable, TreatWarningsAsErrors), Directory.Packages.props (gestión central de paquetes), .editorconfig.
- Código y comentarios en inglés.

## Software factory: roles
Crea subagentes en .claude/agents/ (un .md por agente con frontmatter name, description y tools):
1. architect: diseño, estructura y ADRs en docs/adr/.
2. backend-dev: servicios .NET, pipeline, DI, EF Core.
3. vision-engineer: cámara, modelo ONNX, detección, rendimiento.
4. frontend-dev: Blazor, dashboard, SignalR, UX en alemán.
5. qa-engineer: tests xUnit (sobre todo RulesEngine y debounce) y verificación.
6. code-reviewer: revisa cada milestone (SOLID, async, fugas de memoria, seguridad) y no modifica código, solo reporta.
7. tech-writer: README con diagrama Mermaid y documentación para mí.
Tú, el orquestador, planificas, delegas, lanzas en paralelo las tareas independientes, integras los resultados y no das un milestone por cerrado sin build + tests + review en verde.

## Milestones (uno a uno; al terminar cada uno PARA y espera mi OK)
M0. Comprobar entorno: dotnet --list-sdks, webcam en /dev/video0 (v4l2-ctl), Python para exportar el modelo (usa un venv o uv). Pídeme permiso antes de cualquier sudo o instalación del sistema.
M1. Scaffolding de la solución, CLAUDE.md del proyecto, agentes y ADR-001 con la arquitectura.
M2. Captura de webcam y vídeo MJPEG visible en el navegador.
M3. Exportar el modelo y detectar objetos con recuadros dibujados.
M4. RulesEngine + zona + debounce + recarga en caliente + tests.
M5. Dashboard completo en tiempo real y editor de reglas.
M6. Persistencia SQLite + CSV + modo vídeo (Plan B).
M7. Pulido: logging estructurado, health check, README, guion de demo.
M8. Kit de entrevista (ver abajo).

## Reglas de trabajo
- Empieza en modo plan: antes de escribir código preséntame el plan completo y espera mi OK.
- Commit por milestone con Conventional Commits.
- Quality gates en cada milestone: dotnet build sin warnings, dotnet test en verde, dotnet format.
- Nada de sobreingeniería: es una demo de un fin de semana y tiene que funcionar el lunes.

## Para que yo lo entienda (MUY importante)
- Al cerrar cada milestone escribe docs/aprendizaje/MX.md EN ESPAÑOL: qué se hizo, por qué, conceptos de C# usados (con un ejemplo del propio código) y 3 preguntas que me podrían hacer con su respuesta.
- M8: crea docs/entrevista/ con: PITCH_DE.md (presentación de 2 minutos en alemán), DEMO_DE.md (guion de demo paso a paso en alemán), FRAGEN_DE.md (15 preguntas técnicas probables en alemán con respuesta corta en alemán y traducción al español) y WORKFLOW_DE.md (cómo trabajé con IA: orquestador, subagentes, quality gates, en alemán sencillo).

Empieza con M0. Guárdalo, hazlo todo muy profesional, trabaja con agentes y arquitectura de software, primero divide la tarea en pasos y subpasos, después comenzaremos.

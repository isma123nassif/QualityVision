# 15 wahrscheinliche Interviewfragen – mit Musterantwort

> Jede Frage: **Antwort auf Deutsch** (zum Sprechen, ca. 30–60 s) und darunter die **spanische Übersetzung** (zum Verstehen).
> Tipp: Erst den Kernsatz sagen, dann ein Beispiel aus dem eigenen Code. Nicht auswendig lernen, sondern die Struktur.

---

## 1. Können Sie die Architektur kurz erklären?

**Antwort:**
Die Lösung hat drei Projekte plus Tests: Core, Infrastructure und Web.
Die Abhängigkeiten zeigen nur nach innen: Web → Infrastructure → Core.
Core ist die Fachlogik: `RulesEngine`, `InspectionZone`, `ZoneEntryTracker`. Core hat null NuGet-Pakete.
Core definiert Ports, zum Beispiel `IFrameSource` und `IObjectDetector`.
Infrastructure implementiert diese Ports mit OpenCV, YoloDotNet und EF Core.
Web ist der Composition Root: DI, Hosted Services, Blazor und Endpoints.
Ein Architekturtest prüft die Regel. Wenn Core ein fremdes Paket referenziert, wird der Test rot.
Ich nenne das „hexagonal light“: Ports und Adapter, aber ohne Overhead wie MediatR oder CQRS.

> 🇪🇸 La solución tiene tres proyectos más los tests: Core, Infrastructure y Web. Las dependencias solo apuntan hacia dentro: Web → Infrastructure → Core. Core es la lógica de negocio: `RulesEngine`, `InspectionZone`, `ZoneEntryTracker`, y no tiene ningún paquete NuGet. Core define puertos, por ejemplo `IFrameSource` e `IObjectDetector`. Infrastructure implementa esos puertos con OpenCV, YoloDotNet y EF Core. Web es la raíz de composición: DI, hosted services, Blazor y endpoints. Un test de arquitectura comprueba la regla: si Core referencia un paquete ajeno, el test falla. Lo llamo "hexagonal light": puertos y adaptadores, pero sin sobrecarga como MediatR o CQRS.

---

## 2. Warum `Channel<T>`? Und warum `DropOldest`?

**Antwort:**
Kamera und Erkennung laufen in zwei `BackgroundService`s mit unterschiedlicher Geschwindigkeit.
Ein `Channel<T>` entkoppelt sie: Producer-Consumer, thread-sicher und async.
Der Channel ist **begrenzt**, Kapazität zwei, mit `BoundedChannelFullMode.DropOldest`.
Ist die Erkennung langsamer als die Kamera, wird das älteste Bild verworfen.
So arbeitet der Detektor immer auf einem aktuellen Bild. Latenz und Speicher wachsen nie.
Wichtig: Das verworfene Bild hat einen gepoolten Puffer. Deshalb gebe ich einen `itemDropped`-Callback mit, der `Dispose` aufruft.
Ohne den Callback hätte ich ein Speicherleck.
Für ein Überwachungssystem ist ein verlorenes Bild in Ordnung. Für eine 100-%-Prüfung bräuchte man Backpressure statt Verwerfen.

> 🇪🇸 La cámara y la detección corren en dos `BackgroundService` a distinta velocidad. Un `Channel<T>` los desacopla: productor-consumidor, thread-safe y asíncrono. El canal está **acotado**, capacidad dos, con `DropOldest`. Si la detección es más lenta que la cámara, se descarta el frame más antiguo. Así el detector siempre trabaja con un frame reciente y ni la latencia ni la memoria crecen. Importante: el frame descartado tiene un buffer del pool, por eso paso un callback `itemDropped` que llama a `Dispose`. Sin él habría una fuga de memoria. Para un sistema de monitorización perder un frame es aceptable; para una inspección al 100 % haría falta backpressure en lugar de descartar.

---

## 3. `IOptions`, `IOptionsSnapshot` oder `IOptionsMonitor` – was nutzen Sie und warum?

**Antwort:**
`IOptions<T>` liest die Konfiguration einmal beim Start. `IOptionsSnapshot<T>` ist scoped, also einmal pro Request.
`IOptionsMonitor<T>` ist ein Singleton und meldet Änderungen über `OnChange`. Das brauche ich für Hot Reload in Hintergrunddiensten.
Die Regeln liegen in einer eigenen `rules.json` mit `reloadOnChange`.
Aber: `IOptionsMonitor` kommt aus `Microsoft.Extensions.Options`, und Core darf das nicht kennen.
Deshalb gibt es einen Adapter in Infrastructure, den `OptionsRuleSetProvider`.
Er baut bei jeder Änderung ein unveränderliches `RuleSet` und tauscht es atomar mit `Volatile.Write` aus.
Ist die neue Datei ungültig, behält er die letzten gültigen Regeln und loggt eine Warnung.
Die Erkennung liest pro Bild genau einen Schnappschuss. Alle Objekte im selben Bild werden mit denselben Regeln bewertet.

> 🇪🇸 `IOptions<T>` lee la configuración una vez al arrancar. `IOptionsSnapshot<T>` es scoped, una vez por petición. `IOptionsMonitor<T>` es singleton y notifica cambios con `OnChange`; es lo que necesito para recarga en caliente en servicios en segundo plano. Las reglas están en un `rules.json` propio con `reloadOnChange`. Pero `IOptionsMonitor` vive en `Microsoft.Extensions.Options` y Core no puede conocerlo. Por eso hay un adaptador en Infrastructure, `OptionsRuleSetProvider`. En cada cambio construye un `RuleSet` inmutable y lo sustituye de forma atómica con `Volatile.Write`. Si el fichero nuevo no es válido, mantiene las últimas reglas válidas y registra un warning. La detección lee exactamente una instantánea por frame: todos los objetos del mismo frame se evalúan con las mismas reglas.

---

## 4. Wie vermeiden Sie Speicherlecks? Stichwort `IDisposable` und `ArrayPool`.

**Antwort:**
Bilder sind groß. Ein Frame mit 1280×720 in BGRA hat etwa 3,7 MB.
`Mat` von OpenCV und `SKBitmap` von SkiaSharp halten nativen Speicher. Den räumt der Garbage Collector nicht zuverlässig auf.
Deshalb gilt die Regel: Jedes `Mat`, `SKBitmap` und `Frame` lebt in einem `using`.
Wenn das Eigentum wechselt, ist das im Code ausdrücklich kommentiert.
Für die Pixeldaten miete ich Puffer aus `ArrayPool<byte>.Shared`. Das spart Allokationen und Druck auf den Large Object Heap.
Das `Frame` gibt den Puffer in seinem `Dispose` an den Pool zurück.
Auch der Channel-Callback für verworfene Bilder ruft `Dispose` auf.
Das Code-Review hat jeden Meilenstein gezielt auf Speicherlecks geprüft.
Vor einem echten Einsatz würde ich zusätzlich einen Dauerlauf mit `dotnet-counters` machen.

> 🇪🇸 Las imágenes son grandes: un frame de 1280×720 en BGRA ocupa unos 3,7 MB. `Mat` de OpenCV y `SKBitmap` de SkiaSharp guardan memoria nativa, que el recolector de basura no libera de forma fiable. Por eso la regla es: cada `Mat`, `SKBitmap` y `Frame` vive en un `using`. Cuando cambia la propiedad, está comentado explícitamente en el código. Para los píxeles alquilo buffers de `ArrayPool<byte>.Shared`; ahorra asignaciones y presión sobre el Large Object Heap. El `Frame` devuelve el buffer al pool en su `Dispose`. El callback del canal para frames descartados también llama a `Dispose`. La revisión de código comprobó cada milestone buscando fugas de memoria. Antes de un uso real haría además una ejecución larga con `dotnet-counters`.

---

## 5. Wie gehen Sie mit `async` und `CancellationToken` um?

**Antwort:**
Die Regel ist „async all the way“. Kein `.Result`, kein `.Wait()`. Das verhindert Deadlocks und blockierte Threads.
Jede asynchrone Methode nimmt einen `CancellationToken` und gibt ihn weiter.
Die Hintergrunddienste nutzen den `stoppingToken` von `BackgroundService`. Beim Herunterfahren beenden sie sich sauber.
Der MJPEG-Endpunkt `/stream` nutzt `HttpContext.RequestAborted`. Schließt der Browser den Tab, endet die Schleife sofort.
In den Blazor-Komponenten habe ich eine eigene `CancellationTokenSource`, die in `DisposeAsync` abgebrochen wird.
Für Wartezeiten nutze ich `PeriodicTimer` und `Task.Delay` mit `TimeProvider`. So kann ich Zeit in Tests mit `FakeTimeProvider` simulieren.
In Bibliothekscode nutze ich `ConfigureAwait(false)`.

> 🇪🇸 La regla es "async de principio a fin": nada de `.Result` ni `.Wait()`, así se evitan deadlocks e hilos bloqueados. Cada método asíncrono recibe un `CancellationToken` y lo propaga. Los servicios en segundo plano usan el `stoppingToken` de `BackgroundService` y terminan limpiamente al apagar. El endpoint MJPEG `/stream` usa `HttpContext.RequestAborted`: si el navegador cierra la pestaña, el bucle termina al instante. En los componentes Blazor tengo un `CancellationTokenSource` propio que se cancela en `DisposeAsync`. Para esperas uso `PeriodicTimer` y `Task.Delay` con `TimeProvider`, así puedo simular el tiempo en los tests con `FakeTimeProvider`. En código de librería uso `ConfigureAwait(false)`.

---

## 6. Warum Blazor Server und nicht Blazor WebAssembly?

**Antwort:**
Bei Blazor Server läuft der Komponentencode auf dem Server. Der Browser bekommt nur UI-Diffs über SignalR.
Für ein Live-Dashboard ist das ideal: Die Daten sind schon im Server-Prozess.
Ich abonniere ein Event, und bei neuen Prüfungen rufe ich `InvokeAsync(StateHasChanged)` auf.
Ich brauche keine eigene REST-API, keinen eigenen SignalR-Hub und keine doppelte Serialisierung.
Updates bündele ich auf etwa 4 Hz, damit die Verbindung nicht überlastet wird.
WebAssembly wäre besser bei vielen Nutzern, offline oder bei schlechter Verbindung. Dann braucht man aber eine API-Schicht und einen größeren Download.
Nachteil von Server: Jeder Nutzer hält eine Verbindung und Zustand auf dem Server. Für eine Prüfstation mit wenigen Bildschirmen ist das kein Problem.
Das Video selbst geht nicht über SignalR, sondern als MJPEG über ein normales `<img>`.

> 🇪🇸 Con Blazor Server el código de los componentes se ejecuta en el servidor; el navegador solo recibe diffs de UI por SignalR. Para un dashboard en vivo es ideal: los datos ya están en el proceso del servidor. Me suscribo a un evento y con cada inspección nueva llamo a `InvokeAsync(StateHasChanged)`. No necesito API REST propia, ni hub SignalR propio, ni serialización duplicada. Agrupo las actualizaciones a unos 4 Hz para no saturar la conexión. WebAssembly sería mejor con muchos usuarios, offline o con mala conexión, pero exige una capa API y una descarga mayor. La desventaja de Server: cada usuario mantiene una conexión y estado en el servidor; para una estación de inspección con pocas pantallas no es problema. El vídeo no va por SignalR, sino como MJPEG en un `<img>` normal.

---

## 7. Wie nutzen Sie EF Core?

**Antwort:**
EF Core mit SQLite speichert jede Prüfung als Zeile.
Wichtig ist: Die Datenbank ist **nicht** im heißen Pfad. Die Erkennung schreibt Ereignisse in einen zweiten Channel.
Ein eigener `EventPersistenceService` liest daraus und speichert in Batches von bis zu 50 Ereignissen.
Weil das ein Singleton-Hintergrunddienst ist, nutze ich `IDbContextFactory`. Ein `DbContext` ist nicht thread-sicher und soll kurz leben.
Der CSV-Export streamt mit `AsAsyncEnumerable`, also ohne alles in den Speicher zu laden.
SQLite läuft im WAL-Modus. So blockiert ein offener Lese-Cursor vom Export nicht das Schreiben.
Das Schema erstelle ich mit `EnsureCreated`. Das ist ein bewusster Kompromiss für die Demo. In Produktion würde ich Migrationen nutzen.

> 🇪🇸 EF Core con SQLite guarda cada inspección como una fila. Lo importante: la base de datos **no** está en el camino crítico. La detección escribe eventos en un segundo canal. Un `EventPersistenceService` propio los lee y guarda en lotes de hasta 50. Como es un servicio singleton en segundo plano, uso `IDbContextFactory`: un `DbContext` no es thread-safe y debe vivir poco. El export CSV hace streaming con `AsAsyncEnumerable`, sin cargarlo todo en memoria. SQLite está en modo WAL, así un cursor de lectura abierto por el export no bloquea las escrituras. El esquema lo creo con `EnsureCreated`; es un compromiso consciente para la demo. En producción usaría migraciones.

---

## 8. Wie haben Sie getestet?

**Antwort:**
Es gibt 417 xUnit-Tests. Der Schwerpunkt liegt auf der Fachlogik.
`RulesEngine` ist eine reine Funktion. Gleiche Eingabe, gleiches Ergebnis. Das testet man ohne Mocks.
Die Entprellung im `ZoneEntryTracker` teste ich mit Bildsequenzen: Eintritt, Flackern von einem Bild, Austritt und Wiedereintritt.
Wo Zeit wichtig ist, nutze ich `FakeTimeProvider`. Kein `Thread.Sleep` in den Tests.
Für EF Core nutze ich SQLite in-memory. Der CSV-Export hat eigene Tests, auch für die Injection.
Dazu kommen Architekturtests, die die Schichtenregel prüfen.
Und ein Guard-Test prüft, dass im Build-Output die GPU-Version von ONNX Runtime liegt. Das war ein echter Fehler, den wir gefunden haben.
Zusätzlich gibt es pro Meilenstein einen echten Funktionstest im Browser.

> 🇪🇸 Hay 417 tests xUnit, centrados en la lógica de negocio. `RulesEngine` es una función pura: misma entrada, mismo resultado; se prueba sin mocks. El debounce del `ZoneEntryTracker` lo pruebo con secuencias de frames: entrada, parpadeo de un frame, salida y reentrada. Donde importa el tiempo uso `FakeTimeProvider`, sin `Thread.Sleep`. Para EF Core uso SQLite en memoria. El export CSV tiene sus propios tests, también de inyección. Además hay tests de arquitectura que comprueban la regla de capas, y un test de guarda que verifica que en la salida del build está la versión GPU de ONNX Runtime; fue un error real que encontramos. Y en cada milestone, una prueba funcional real en el navegador.

---

## 9. Wie funktioniert die GPU-Beschleunigung mit ONNX?

**Antwort:**
Das Modell ist ein ONNX-Export, Opset 17, mit fester Eingabe 640×640.
Ich nutze YoloDotNet auf ONNX Runtime, mit dem CUDA Execution Provider.
Gemessen: Ein yolo11m braucht auf der CPU 198 ms, auf der GPU etwa 6,5 ms. Das jetzige YOLO-World-Modell braucht etwa 10 ms.
Die Gerätewahl ist `Auto`: Erst CUDA versuchen. Bei einem Fehler Warnung loggen und auf ein kleines Modell auf der CPU wechseln.
Das aktive Gerät steht im Log, im Health Check und als Badge im Dashboard.
Eine Falle war das Packaging: CPU- und GPU-Paket liefern beide `libonnxruntime.so`. Die CPU-Version hat die GPU-Version still überschrieben.
`ExcludeAssets` wirkt nicht über `ProjectReference`. Deshalb muss jedes ausführbare Projekt es wiederholen. Ein Test sichert das jetzt ab.
Das Modell ist ein Singleton, weil das Laden teuer ist.

> 🇪🇸 El modelo es una exportación ONNX, opset 17, con entrada fija de 640×640. Uso YoloDotNet sobre ONNX Runtime con el proveedor de ejecución CUDA. Medido: yolo11m tarda 198 ms en CPU y unos 6,5 ms en GPU; el modelo YOLO-World actual, unos 10 ms. La selección de dispositivo es `Auto`: primero intenta CUDA; si falla, registra un warning y pasa a un modelo pequeño en CPU. El dispositivo activo aparece en el log, en el health check y como badge en el dashboard. Una trampa fue el empaquetado: los paquetes CPU y GPU traen ambos `libonnxruntime.so`, y la versión CPU sobrescribía en silencio a la GPU. `ExcludeAssets` no se propaga por `ProjectReference`, así que cada proyecto ejecutable debe repetirlo; ahora un test lo asegura. El modelo es singleton porque cargarlo es caro.

---

## 10. Was ist mit der Lizenz? YOLO ist doch AGPL.

**Antwort:**
Richtig. Ultralytics YOLO, Code und Gewichte, steht unter AGPL-3.0. YOLO-World ist GPL-3.0.
Bei AGPL reicht es nicht, das Produkt nicht zu verkaufen. Schon die Nutzung über ein Netzwerk kann Pflichten auslösen, zum Beispiel den Quellcode anzubieten.
Für eine interne Demo ist das in Ordnung. Ich habe es im ADR und in den Modell-Notizen dokumentiert.
Für ein kommerzielles Kundenprodukt gibt es zwei Wege: eine Enterprise-Lizenz von Ultralytics kaufen oder ein Modell mit Apache- oder MIT-Lizenz nutzen, zum Beispiel YOLOX oder das originale RT-DETR.
YoloDotNet und ONNX Runtime selbst sind MIT. Das habe ich im NuGet-Paket geprüft.
Architektonisch ist der Wechsel klein: Das Modell steckt hinter `IObjectDetector`. Nur eine Klasse in Infrastructure kennt das YOLO-Format.

> 🇪🇸 Correcto. Ultralytics YOLO, código y pesos, es AGPL-3.0; YOLO-World es GPL-3.0. Con AGPL no basta con no vender el producto: el mero uso a través de una red puede generar obligaciones, por ejemplo ofrecer el código fuente. Para una demo interna está bien, y lo documenté en el ADR y en las notas del modelo. Para un producto comercial de cliente hay dos caminos: comprar una licencia Enterprise de Ultralytics o usar un modelo Apache o MIT, por ejemplo YOLOX o el RT-DETR original. YoloDotNet y ONNX Runtime son MIT; lo comprobé en el paquete NuGet. Arquitectónicamente el cambio es pequeño: el modelo está detrás de `IObjectDetector` y solo una clase de Infrastructure conoce el formato YOLO.

---

## 11. Wie sicher ist die Demo? Es gibt ja keine Anmeldung.

**Antwort:**
Ehrlich: Es ist eine LAN-Demo ohne Authentifizierung. Das ist eine bewusste Entscheidung und im ADR dokumentiert.
Die Risiken habe ich trotzdem begrenzt.
Netzwerk: nur im Heimnetz, Firewall erlaubt die Ports nur für Laptop und iPhone. Nach der Demo wird die App gestoppt.
Die App läuft in *Production*, also keine Stacktraces für Besucher.
Der Bild-Upload vom iPhone hat ein Größenlimit, akzeptiert nur JPEG und prüft die Magic Bytes.
Der Regel-Editor schreibt nur eine feste Datei, validiert die Eingaben und schreibt atomar.
Und der CSV-Export ist gegen Formel-Injection geschützt: Felder, die mit `=`, `+`, `-` oder `@` beginnen, bekommen ein Hochkomma davor. Das hat das Review gefunden.
Für Produktion würde ich HTTPS überall und Anmeldung ergänzen, zum Beispiel mit Entra ID und Rollen: Werker darf sehen, nur Qualitätsleiter darf Regeln ändern.

> 🇪🇸 Sinceramente: es una demo de LAN sin autenticación; es una decisión consciente y está documentada en el ADR. Aun así limité los riesgos. Red: solo en la red de casa, el firewall permite los puertos solo al portátil y al iPhone, y la app se para tras la demo. La app corre en *Production*, así que no se muestran stack traces. La subida de imágenes del iPhone tiene límite de tamaño, solo acepta JPEG y comprueba los magic bytes. El editor de reglas solo escribe un fichero fijo, valida las entradas y escribe de forma atómica. Y el export CSV está protegido contra inyección de fórmulas: los campos que empiezan por `=`, `+`, `-` o `@` llevan un apóstrofo delante; lo encontró la revisión. En producción añadiría HTTPS en todo y autenticación, por ejemplo con Entra ID y roles: el operario puede ver, solo el responsable de calidad puede cambiar reglas.

---

## 12. Wie zählen Sie Objekte, ohne doppelt zu zählen?

**Antwort:**
YOLO gibt keine Identität für Objekte. Es sagt nur: In diesem Bild ist eine Tasse.
Deshalb hat der `ZoneEntryTracker` pro Klasse einen Zustand: abwesend oder anwesend.
Wechselt eine Klasse von abwesend zu anwesend in der Zone, gibt es genau ein Ereignis.
Abwesend wird sie erst, wenn sie N Bilder hintereinander fehlt. Standard ist N gleich fünf, konfigurierbar.
Ein kurzes Flackern des Detektors zählt also nicht doppelt.
Die Einheit sind bewusst Bilder, nicht Sekunden. So bleibt der Tracker eine reine Funktion, ohne Uhr, einfach zu testen.
Bekannte Grenze: Zwei Tassen gleichzeitig zählen als eine. Das ist dokumentiert.
Die Lösung wäre ein Multi-Object-Tracker wie ByteTrack. Dann wäre der Schlüssel die Track-ID statt der Klasse. Der Rest der Pipeline bleibt gleich.

> 🇪🇸 YOLO no da identidad a los objetos; solo dice "en este frame hay una taza". Por eso el `ZoneEntryTracker` tiene un estado por clase: ausente o presente. Cuando una clase pasa de ausente a presente en la zona, se emite exactamente un evento. Solo vuelve a ausente cuando falta N frames seguidos; por defecto N = 5, configurable. Así un parpadeo del detector no cuenta doble. La unidad son frames a propósito, no segundos: el tracker sigue siendo una función pura, sin reloj, fácil de testear. Límite conocido: dos tazas a la vez cuentan como una; está documentado. La solución sería un tracker multiobjeto como ByteTrack: la clave sería el ID de track en lugar de la clase, y el resto del pipeline no cambia.

---

## 13. Wie würden Sie das auf 10 Kameras skalieren?

**Antwort:**
Zuerst messen. Dann gibt es mehrere Stufen.
Erste Stufe: eine Pipeline pro Kamera, jeweils mit eigenem Channel und eigenem Tracker. Die Kamera-ID kommt in jedes Ereignis und in die Datenbank.
Die GPU teilen sich alle. Bei 10 ms pro Bild schafft eine GPU rechnerisch etwa 100 Bilder pro Sekunde. Bei 10 Kameras mit 10 FPS passt das gerade so.
Besser ist Batching: Bilder mehrerer Kameras in einem Batch inferieren. Das braucht ein Modell mit dynamischer Batch-Größe.
Optional TensorRT für noch weniger Latenz.
Zweite Stufe: Erkennung nahe an der Linie, also ein Edge-Rechner pro Linie. Die Ereignisse gehen zentral über MQTT oder OPC UA.
SQLite würde ich dann durch PostgreSQL oder SQL Server ersetzen. Das Dashboard bekommt eine Auswahl der Kamera.
Die Architektur hilft: Die Kamera ist schon ein Interface, und der Core weiß nichts von der Anzahl.

> 🇪🇸 Primero medir; luego hay varias etapas. Primera: un pipeline por cámara, cada uno con su canal y su tracker; el ID de cámara va en cada evento y en la base de datos. Todos comparten la GPU: a 10 ms por frame, una GPU da en teoría unos 100 frames por segundo; con 10 cámaras a 10 FPS va justo. Mejor es el batching: inferir frames de varias cámaras en un solo lote, lo que requiere un modelo con tamaño de lote dinámico. Opcionalmente TensorRT para menos latencia. Segunda etapa: detección cerca de la línea, un equipo edge por línea, y los eventos van a un sistema central por MQTT u OPC UA. SQLite lo cambiaría por PostgreSQL o SQL Server, y el dashboard tendría un selector de cámara. La arquitectura ayuda: la cámara ya es una interfaz y el Core no sabe cuántas hay.

---

## 14. Wie würden Sie das an eine SPS oder ein MES anbinden, z. B. über OPC UA?

**Antwort:**
Das ist nicht umgesetzt, aber die Architektur ist dafür vorbereitet.
Ich würde einen neuen Adapter in Infrastructure bauen, als weiteren Konsumenten der Prüfereignisse – genau wie die Datenbank, über einen eigenen Channel.
So bremst eine langsame Verbindung die Erkennung nicht.
Für die SPS gibt es zwei Varianten: Die App ist OPC-UA-Client und schreibt ein Signal in einen Knoten der SPS, zum Beispiel „Teil n.i.O., auswerfen“.
Oder die App ist OPC-UA-Server und stellt Variablen bereit: letzter Befund, Zähler, Status. Die SPS oder das MES liest sie.
Wichtig ist das Timing: Für einen Auswerfer braucht man die Position des Teils, also meist einen Trigger von der SPS, zum Beispiel eine Lichtschranke.
Ans MES würde ich Ergebnisse eher über REST oder MQTT melden, mit Seriennummer oder Auftragsnummer.
In .NET gibt es dafür den offiziellen OPC-UA-Stack der OPC Foundation. Die Lizenz würde ich vorher prüfen.

> 🇪🇸 No está implementado, pero la arquitectura está preparada. Haría un adaptador nuevo en Infrastructure, como otro consumidor de los eventos de inspección, igual que la base de datos, por su propio canal; así una conexión lenta no frena la detección. Para el PLC hay dos variantes: la app es cliente OPC UA y escribe una señal en un nodo del PLC, por ejemplo "pieza NOK, expulsar". O la app es servidor OPC UA y expone variables: último resultado, contadores, estado; el PLC o el MES las leen. Lo importante es el timing: para un expulsor hace falta la posición de la pieza, normalmente un disparo del PLC, por ejemplo una barrera de luz. Al MES reportaría los resultados más bien por REST o MQTT, con número de serie o de orden. En .NET existe el stack OPC UA oficial de la OPC Foundation; revisaría su licencia antes.

---

## 15. Wie haben Sie mit KI-Agenten gearbeitet? Ist das dann Ihr Code?

**Antwort:**
Ich habe mit Claude Code gearbeitet, als Orchestrator mit spezialisierten Agenten: Architekt, Backend, Vision, Frontend, QA, Reviewer und Tech-Writer.
Pro Meilenstein gab es einen Vertrag mit genauen Signaturen. Unabhängige Agenten haben parallel daran gearbeitet.
Die Entscheidungen lagen bei mir: Umfang, Freigabe jedes Meilensteins, GPU, iPhone als Kamera, Erkennung des Markers.
Und nichts wurde einfach geglaubt. Jeder Meilenstein musste durch Quality Gates: null Warnungen, Tests grün, Format sauber, Review ohne Blocker, Live-Test.
Das Review hat echte Fehler gefunden, zum Beispiel die CSV-Injection.
Aber auch die KI irrt: Ein Reviewer behauptete, YoloDotNet sei GPL. Ich habe die Lizenz im Paket geprüft – es ist MIT.
Mein Fazit: Die KI schreibt schnell. Verantwortlich bin ich. Deshalb habe ich jede Entscheidung dokumentiert und kann sie erklären.

> 🇪🇸 Trabajé con Claude Code como orquestador con agentes especializados: arquitecto, backend, visión, frontend, QA, revisor y tech-writer. En cada milestone había un contrato con firmas exactas, y los agentes independientes trabajaban en paralelo sobre él. Las decisiones eran mías: alcance, aprobación de cada milestone, GPU, iPhone como cámara, detección del rotulador. Y nada se creía sin más: cada milestone pasaba quality gates: cero warnings, tests en verde, formato limpio, revisión sin bloqueantes y prueba en vivo. La revisión encontró errores reales, por ejemplo la inyección CSV. Pero la IA también se equivoca: un revisor afirmó que YoloDotNet era GPL; comprobé la licencia en el paquete y es MIT. Mi conclusión: la IA escribe rápido, el responsable soy yo. Por eso documenté cada decisión y puedo explicarla.

---

## Bonus: Fragen, die ich selbst stellen kann

- „Mit welchen Kameras und Steuerungen arbeiten Ihre Kunden typischerweise – Siemens, Beckhoff, Cognex?“
- „Läuft die Bildverarbeitung bei Ihren Kunden eher am Edge oder zentral?“
- „Wie sieht bei Ihnen der Umgang mit KI-Werkzeugen in der Entwicklung aus?“
- „Wie ist ein typisches Projektteam aufgebaut, und wie viel Kundenkontakt hat ein Entwickler?“

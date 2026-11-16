# Pitch – QualityVision

> Zwei Versionen: **Kurz (60–90 s)** für „Erzählen Sie kurz, was Sie mitgebracht haben“.
> **Lang (ca. 3 min)**, wenn Zeit für Details ist.
> Ruhig sprechen. Kurze Sätze. Nach jedem Absatz kurz Pause machen.

---

## Kurzversion (60–90 Sekunden)

Ich habe QualityVision mitgebracht. Das ist eine Demo für optische Qualitätsprüfung.
Sie läuft komplett in C# und .NET 10.

Eine Kamera filmt eine Prüfzone. Ein YOLO-Modell erkennt die Objekte, auf der GPU in etwa zehn Millisekunden.
Eine Regel-Engine entscheidet dann: i.O., n.i.O. oder Gefahr.
Jedes Teil wird genau einmal gezählt, wenn es in die Zone kommt.

Das Dashboard ist auf Deutsch und mit Blazor Server gebaut.
Es zeigt Zähler, Ausschussquote, FPS und ein Ereignisprotokoll in Echtzeit.
Bei n.i.O. gibt es einen Alarm.
Die Regeln kann man im Browser ändern. Sie gelten sofort, ohne Neustart.
Alle Prüfungen landen in SQLite und kann man als CSV exportieren.

Technisch sind mir drei Dinge wichtig:
Erstens eine saubere Schichtenarchitektur. Der Domänenkern hat keine einzige Abhängigkeit.
Zweitens Robustheit: begrenzte Channels, sauberes Dispose, GPU mit automatischem CPU-Fallback.
Drittens Qualität: 417 Tests, null Warnungen, und für jede Entscheidung gibt es ein ADR.

Gebaut habe ich das an einem Wochenende, mit KI-Agenten als Team.
Die Entscheidungen und die Verantwortung lagen bei mir.
Ich zeige es Ihnen gern live.

*(ca. 190 Wörter ≈ 80 Sekunden)*

---

## Langversion (ca. 3 Minuten)

### 1. Das Problem (ca. 25 s)

In der Fertigung prüfen oft noch Menschen die Teile mit dem Auge.
Das ist teuer, ermüdend und schwer nachzuweisen.
Ich wollte zeigen, wie man so eine Prüfstation mit .NET baut.
Nicht als Python-Prototyp, sondern als eine einzige, wartbare C#-Anwendung.

### 2. Was die Demo kann (ca. 45 s)

Eine Kamera – in meinem Fall ein iPhone – liefert das Livebild.
In der Mitte ist die Prüfzone.
Ein YOLO-World-Modell erkennt die Objekte, auf der GPU mit CUDA, in etwa zehn Millisekunden.
Eine Flasche ist i.O., grün. Eine Tasse ist n.i.O., rot, mit Alarm. Eine Schere ist Gefahr, orange.

Gezählt wird nur, wenn ein Objekt in die Zone *eintritt*.
Ein kurzes Flackern des Detektors zählt nicht doppelt.

Das Dashboard zeigt Zähler, Ausschussquote, Prüfungen pro Minute und ein Ereignisprotokoll.
Die Regeln ändere ich im Browser, und sie gelten beim nächsten Bild.
Alles wird in SQLite gespeichert und lässt sich als CSV für Excel exportieren.

### 3. Die Architektur (ca. 50 s)

Die Lösung hat drei Schichten: Core, Infrastructure und Web.
Core ist die Fachlogik: Regel-Engine, Prüfzone, Zählung.
Core hat **null** NuGet-Pakete. Ein Architekturtest erzwingt das.
Kamera, ONNX-Modell und Datenbank sind Adapter in Infrastructure.

Die Pipeline läuft so:
Ein `BackgroundService` liest die Kamera und schreibt in einen begrenzten Channel.
Der Channel hat Kapazität zwei und verwirft das älteste Bild.
So bleibt die Latenz klein, und der Speicher wächst nie.
Danach kommen Erkennung, Regeln und Zählung.
Das Bild wird einmal als JPEG kodiert und an alle Browser verteilt.
Die Datenbank hängt an einem zweiten Channel. Sie bremst die Erkennung also nie.

### 4. Qualität und Robustheit (ca. 35 s)

Jeder Meilenstein hatte feste Quality Gates:
Build ohne Warnungen, alle Tests grün, Formatierung sauber, ein Code-Review ohne Blocker.
Am Ende sind es 417 Tests und fünf Architekturentscheidungen als ADR.

Für den Ernstfall gibt es Rückfallebenen:
Fehlt die GPU, läuft die Erkennung automatisch auf der CPU weiter.
Fällt die Kamera aus, verbindet sich die App neu.
Und als Plan B kann ich die Demo mit einem aufgenommenen Video starten.

### 5. Wie ich gearbeitet habe (ca. 25 s)

Ich habe mit KI-Agenten gearbeitet, wie mit einem kleinen Team:
Architekt, Backend, Vision, Frontend, QA, Reviewer.
Ich habe den Umfang festgelegt und jeden Meilenstein freigegeben.
Nichts wurde einfach geglaubt. Alles wurde gebaut, getestet und live geprüft.
Das Review hat echte Fehler gefunden, zum Beispiel eine CSV-Injection. Die wurde vor dem Commit behoben.

### Abschluss (ca. 10 s)

Ich zeige Ihnen das jetzt gern live.
Und danach erkläre ich gern jede Entscheidung im Detail.

*(ca. 430 Wörter ≈ 3 Minuten)*

---

## Merksätze (falls der Faden reißt)

- „Der Kern kennt weder YOLO noch die Datenbank.“
- „Begrenzter Channel: lieber ein Bild verlieren als Latenz aufbauen.“
- „Die Datenbank bremst die Erkennung nie.“
- „Regeln ändern ohne Neustart – über `IOptionsMonitor`.“
- „GPU, wenn vorhanden. Sonst CPU. Die Demo läuft immer.“
- „Die KI hat geschrieben. Ich habe entschieden und geprüft.“

## Zahlen zum Merken

| Was | Wert |
|---|---|
| Inferenz GPU (YOLO-World v2 L, CUDA) | ca. 10 ms |
| yolo11m: CPU vs. GPU | 198 ms vs. ca. 6,5 ms |
| Tests | 417 |
| Produktcode / Testcode | ca. 5.600 / ca. 4.500 Zeilen |
| ADRs / Reviews / Commits (M1–M6) | 5 / 7 / 8 |
| Channel-Kapazität | 2, `DropOldest` |
| Entprellung | 5 Bilder Abwesenheit (konfigurierbar) |

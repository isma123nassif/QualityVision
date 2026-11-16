# Demo-Drehbuch – QualityVision

> Gesamtdauer: **ca. 10–12 Minuten** (ohne Fragen).
> Format: **Tun** = was ich mache. **Sagen** = was ich dabei sage.
> Die App läuft auf dem Linux-PC. Der Windows-Laptop teilt in Google Meet den Chrome-**Tab** mit `http://<PC-IP>:5080`.

---

## Vorbereitung (vor dem Gespräch, nicht vor Publikum)

**30–15 Minuten vorher:**

- [ ] `samples/demo.mp4` existiert (Plan B). Falls nicht, jetzt aufnehmen – siehe `samples/README.md`:
      `Camera__RecordTo=$PWD/samples/demo.mp4 ASPNETCORE_ENVIRONMENT=Production dotnet run --project src/QualityVision.Web --no-launch-profile`
      Flasche, Tasse, Schere und Marker nacheinander durch die Zone bewegen. **Stand heute fehlt die Datei noch.**
- [ ] `./run-demo.sh` starten (erster CUDA-Start dauert ca. 5 s länger). Ausgabe zeigt Dashboard-, Phone- und Health-URL.
- [ ] Im Log prüfen: Gerät **GPU**, Modell **yoloworld-qv**.
- [ ] iPhone: `https://<PC-IP>:5443/kamera` öffnen, Kamera starten. iPhone fest aufstellen, Zone im Bild.
- [ ] Laptop: Chrome-Tab mit `http://<PC-IP>:5080`. Ein zweiter Tab mit `/regeln` vorbereiten.
- [ ] In Meet „Tab teilen“ wählen und **„Tab-Audio teilen“ aktivieren** (sonst hört niemand den Alarm).
- [ ] Objekte bereitlegen: Flasche, Tasse, Schere, Marker. Alles außerhalb der Zone.
- [ ] `rules.json` im Ausgangszustand: `cup` = n.i.O. (Pfad steht in der Ausgabe von `run-demo.sh`: `artifacts/demo/rules.json`).
- [ ] Zweites Terminal offen, im Repo-Ordner (für Plan B).
- [ ] Optional: Zähler vom Vortag sind egal; sie sind pro Tag.

---

## Ablauf mit Zeiten

| # | Schritt | Zeit | Kumuliert |
|---|---|---|---|
| 0 | Start erklären (`./run-demo.sh`) | 0:45 | 0:45 |
| 1 | Dashboard-Rundgang | 1:30 | 2:15 |
| 2 | Flasche → i.O. | 0:45 | 3:00 |
| 3 | „Ton an“, dann Tasse → n.i.O. + Alarm | 1:00 | 4:00 |
| 4 | Schere → GEFAHR | 0:45 | 4:45 |
| 5 | Marker → YOLO-World | 1:00 | 5:45 |
| 6 | `/regeln`: Tasse live auf i.O. | 1:45 | 7:30 |
| 7 | CSV-Export | 1:00 | 8:30 |
| 8 | GPU-Badge und `/health` | 1:00 | 9:30 |
| 9 | Plan B erwähnen (oder ausführen) | 1:00 | 10:30 |

Wenn die Zeit knapp ist: Schritt 5 und 9 kürzen. Schritt 6 **nie** weglassen – er ist der stärkste Moment.

---

## Schritt 0 – Start (0:45)

**Tun:** Terminal kurz zeigen (oder nur beschreiben, die App läuft schon).

**Sagen:**
> „Ich starte die Demo mit einem Skript: `./run-demo.sh`.
> Das Skript veröffentlicht die App im Release-Modus und startet sie in der Umgebung *Production*.
> So sehen Sie nie eine Entwickler-Fehlerseite.
> Es prüft vorher auch, ob die GPU da ist und ob die Ports frei sind.
> Die App läuft auf meinem Linux-PC. Ich teile hier nur den Browser-Tab.“

---

## Schritt 1 – Dashboard-Rundgang (1:30)

**Tun:** Mit der Maus langsam von oben nach unten zeigen.

**Sagen:**
> „Oben sehen Sie die Kennzahlen: **i.O.**, **n.i.O.**, **Gefahr** und **Unbekannt**.
> Daneben die **Ausschussquote**. Das ist n.i.O. geteilt durch i.O. plus n.i.O.
> Dann **FPS** und **Inferenz ms** – also wie lange das Modell pro Bild braucht.
> Das Badge **Erkennung** zeigt, worauf das Modell läuft: hier GPU.“

> „In der Mitte ist das Livebild. Das Rechteck ist die **Prüfzone**.
> Gezählt wird nur, was in diese Zone eintritt.“

> „Darunter: **Prüfungen pro Minute** als Diagramm, und das **Ereignisprotokoll** mit Uhrzeit, Objekt, Bewertung und Konfidenz.“

> „Rechts der **Kamerastatus**: Quelle, Verbindung, Eingangs-FPS und wann das letzte Bild kam.
> Darunter die Objekte, die gerade erkannt werden.“

> „Das Dashboard ist Blazor Server. Die Updates kommen über die SignalR-Verbindung, die Blazor sowieso hat.
> Ich brauche keinen eigenen Hub und kein JavaScript-Framework.“

---

## Schritt 2 – Flasche: i.O. (0:45)

**Tun:** Flasche langsam in die Prüfzone stellen. 2 Sekunden warten. Wieder herausnehmen.

**Sagen:**
> „Ich stelle eine Flasche in die Zone.
> Grüner Rahmen, Bewertung **i.O.** Der Zähler geht um eins hoch.
> Im Protokoll steht ein neuer Eintrag.“

> „Die Flasche steht jetzt mehrere Sekunden in der Zone, aber sie zählt nur **einmal**.
> Ein Objekt zählt erst wieder, wenn es mindestens fünf Bilder lang weg war.
> Das ist die Entprellung. Sie verhindert Doppelzählungen, wenn der Detektor kurz flackert.“

---

## Schritt 3 – „Ton an“, dann Tasse: n.i.O. + Alarm (1:00)

**Tun:** **Zuerst** oben rechts auf **„🔕 Ton aus“** klicken → wird **„🔔 Ton an“**. Dann Tasse in die Zone stellen.

**Sagen (beim Klick):**
> „Zuerst schalte ich den Ton ein.
> Browser erlauben Audio erst nach einem Klick des Benutzers. Deshalb gibt es diesen Schalter.“

**Sagen (Tasse in der Zone):**
> „Jetzt eine Tasse. Laut Regel ist das **n.i.O.**
> Roter Rahmen, roter Alarm-Banner oben, und ein Warnton.
> Der Zähler n.i.O. steigt, und die Ausschussquote ändert sich sofort.“

> „In einer echten Linie würde man hier nicht nur piepen,
> sondern ein Signal an die SPS schicken, zum Beispiel für einen Auswerfer.“

**Tun:** Tasse wieder herausnehmen und **außerhalb lassen** (für Schritt 6).

---

## Schritt 4 – Schere: GEFAHR (0:45)

**Tun:** Schere in die Zone legen. Danach wegnehmen.

**Sagen:**
> „Eine Schere ist als **Gefahr** konfiguriert.
> Orange, eigener Banner: *GEFAHR – Schere erkannt*. Ein anderer Ton.
> Das steht für ein Objekt, das nicht in die Linie gehört – zum Beispiel Werkzeug auf dem Förderband.
> Gefahr hat Vorrang vor n.i.O., wenn beides gleichzeitig passiert.“

---

## Schritt 5 – Marker: YOLO-World (1:00)

**Tun:** Marker in die Zone halten.

**Sagen:**
> „Jetzt etwas Interessantes. Ein Marker ist **keine** Klasse im Standard-Datensatz COCO.
> Ein normales YOLO-Modell kann ihn also gar nicht erkennen – egal mit welchem Schwellwert.“

> „Deshalb nutze ich **YOLO-World**. Das ist ein Open-Vocabulary-Modell.
> Die Klassen gebe ich als Text vor, zum Beispiel *marker pen*.
> Ähnliche Dinge wie Bleistift oder Kabel sind eigene Klassen und werden ignoriert.
> So gibt es weniger Fehlalarme. Die Konfidenz liegt hier live bei etwa 0,86 bis 0,97.“

> „Am C#-Code musste ich dafür nichts ändern. Das Modell wird als normales ONNX exportiert.
> In einem echten Kundenprojekt würde ich aber ein Modell mit Bildern der echten Teile trainieren.
> YOLO-World ist die pragmatische Brücke für die Demo.“

---

## Schritt 6 – Regeln live ändern (1:45) ⭐

**Tun:** Zum Tab **Regeln** wechseln (Navigation oben: „Regeln“).

**Sagen:**
> „Das ist der Regel-Editor. Jede Klasse hat eine Bewertung.
> Die Spalte **Aktiv** zeigt, was die Prüfstation gerade wirklich benutzt.“

**Tun:** Bei `cup` (Tasse) die Bewertung von **n.i.O.** auf **i.O.** ändern. Auf **Speichern** klicken.

**Sagen:**
> „Ich ändere die Tasse von n.i.O. auf i.O. und speichere.
> Meldung: *Gespeichert – Regeln werden live übernommen*.
> Nach etwa einer Sekunde zeigt die Spalte **Aktiv** auch i.O.“

> „Was passiert technisch? Der Editor schreibt nur die Datei `rules.json`, atomar über eine temporäre Datei.
> .NET erkennt die Änderung. `IOptionsMonitor` lädt die Regeln neu.
> Ein Adapter baut daraus ein unveränderliches `RuleSet` und tauscht es atomar aus.
> Die Erkennung liest pro Bild genau einen Schnappschuss. Kein Neustart, kein Lock im Kern.“

**Tun:** Zurück zur **Prüfstation**. Tasse in die Zone stellen.

**Sagen:**
> „Dieselbe Tasse ist jetzt grün und i.O.
> Wäre die Datei kaputt, würde die App einfach die letzten gültigen Regeln behalten.“

**Tun:** Tasse heraus. In `/regeln` die Tasse **zurück auf n.i.O.** setzen und speichern.

> „Ich setze es wieder zurück.“

---

## Schritt 7 – CSV-Export (1:00)

**Tun:** Auf der Prüfstation auf **„CSV exportieren“** klicken. Datei kurz öffnen (Excel oder Texteditor).

**Sagen:**
> „Alle Prüfungen sind in SQLite gespeichert, mit EF Core.
> Die Datenbank hängt an einem eigenen Channel. Wenn sie langsam ist, merkt die Erkennung nichts.“

> „Der Export ist für deutsches Excel gemacht: Semikolon, Komma als Dezimalzeichen, UTF-8 mit BOM.
> Er wird gestreamt, also auch bei vielen Zeilen ohne großen Speicherverbrauch.“

> „Und er ist gegen **CSV-Injection** geschützt. Beginnt ein Feld mit `=`, `+`, `-` oder `@`,
> setze ich ein Hochkomma davor. Sonst würde Excel das als Formel ausführen.
> Das hat übrigens das Code-Review gefunden, vor dem Commit.“

---

## Schritt 8 – GPU-Badge und /health (1:00)

**Tun:** Auf das Badge **Erkennung** zeigen (z. B. „GPU · yoloworld-qv“). Dann neuen Tab: `http://<PC-IP>:5080/health`.

**Sagen:**
> „Das Badge zeigt: Die Erkennung läuft auf der GPU, mit CUDA.
> Gemessen: ein mittleres YOLO-Modell braucht auf der CPU 198 Millisekunden, auf der GPU etwa 6,5.“

> „Wenn CUDA fehlt – Treiber, cuDNN – fällt die App automatisch auf die CPU zurück, mit einem kleineren Modell.
> Dann steht hier *CPU*, und im Log steht der Grund. Die Demo läuft weiter.“

> „`/health` ist ein Standard-Health-Check von ASP.NET Core.
> Er prüft, ob das Modell erfolgreich inferiert und ob die Datenbank erreichbar ist.
> Das Ergebnis ist *Healthy*, *Degraded* oder *Unhealthy*. Ein Load Balancer oder ein Monitoring kann das abfragen.“

---

## Schritt 9 – Plan B (1:00)

**Normalfall – nur erwähnen:**
> „Für den Notfall gibt es einen Plan B.
> Mit `./run-demo.sh --video` startet die Demo mit einem aufgenommenen Video statt der Kamera.
> Technisch ist das nur ein anderer Konfigurationswert, `Camera:Source = VideoFile`.
> Die Quelle ist ein Interface, `IFrameSource`. Der Rest der Pipeline merkt keinen Unterschied.“

**Optional vorführen (nur wenn Zeit ist und `samples/demo.mp4` existiert):**
1. Im Terminal `Strg+C` (App stoppt sauber).
2. `./run-demo.sh --no-build --video`
3. Browser-Tab neu laden (oder oben „Neu laden“).
> „Die Zähler von heute sind noch da. Sie kommen beim Start aus der Datenbank.“

---

## Wenn die Kamera ausfällt

Ruhig bleiben. Das ist eine Chance zu zeigen, dass ich vorbereitet bin.

**Symptome:** Badge **„● Getrennt“** im Kamerastatus, Text *„Kamera nicht erreichbar – neuer Versuch in … s“*, das Livebild steht.

**1. Kurz warten (max. 10–15 s).**
> „Die Verbindung zur Kamera ist weg. Sie sehen: Die App stürzt nicht ab.
> Sie meldet den Fehler und versucht es mit Backoff automatisch neu.“

**2. iPhone prüfen:** Ist die Seite `/kamera` noch offen und aktiv? Bildschirm gesperrt? Seite neu laden, Kamera erneut starten.

**3. Wenn das nicht hilft → Plan B (ca. 30 s):**
> „Ich wechsle jetzt auf Plan B, das aufgenommene Video. Das dauert ein paar Sekunden.“
- Terminal: `Strg+C`, dann `./run-demo.sh --no-build --video`
- Browser-Tab neu laden.
> „Das ist dieselbe Pipeline mit derselben Erkennung und denselben Regeln. Nur die Quelle ist anders.“

**4. Wenn auch das Video fehlt oder nicht startet:**
> „Dann zeige ich Ihnen die Architektur und den Code direkt.“
- README mit Mermaid-Diagramm zeigen, dann `RulesEngine`, `ZoneEntryTracker` und die Tests.
- Tests live laufen lassen: `dotnet test` (417 grün).

**Ehrlich bleiben:** Ein automatischer Wechsel von Kamera auf Video war geplant, ist aber **nicht** umgesetzt.
Die App verbindet sich automatisch neu; den Wechsel auf das Video mache ich von Hand.
Wenn jemand fragt: „Das wäre der nächste Schritt. Die Abstraktion `IFrameSource` ist schon dafür da.“

---

## Weitere Notfälle

| Problem | Reaktion |
|---|---|
| Kein Ton im Meet | „Tab-Audio teilen“ war nicht an. Kurz sagen: „Der Ton kommt nur bei mir an, aber Sie sehen den Banner.“ Weitermachen. |
| Badge zeigt **CPU** | „Gut, dann sehen Sie den Fallback live.“ Hinweis: Auf der CPU erkennt das COCO-Modell **keinen Marker** → Schritt 5 nur erklären. |
| Objekt wird nicht erkannt | Objekt näher, mittig, vor ruhigen Hintergrund halten. Nicht mehr als 2 Versuche, dann weiter. |
| Objekt zählt nicht neu | Es war nicht lange genug weg (Entprellung). Ganz aus der Zone nehmen, 1 Sekunde warten. |
| Browser zeigt „Verbindung getrennt“ | Oben auf „Neu laden“ klicken. Blazor-Server-Verbindung wird neu aufgebaut. |
| Port belegt beim Neustart | Alte Instanz läuft noch. `ss -ltnp 'sport = :5080'` → Prozess per PID beenden. |

---

## Schluss (15 s)

> „Das war QualityVision. Kamera, Erkennung, Regeln, Zählung, Dashboard, Speicherung, Export.
> Alles in einer C#-Anwendung, mit Tests und dokumentierten Entscheidungen.
> Ich freue mich auf Ihre Fragen.“

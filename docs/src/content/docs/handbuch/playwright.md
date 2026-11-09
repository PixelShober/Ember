---
title: Playwright
description: Browser für ein Projekt über px installieren, Playwright hinzufügen, neue Projekte aus einer Vorlage anlegen, Vorlagen aus Projekten machen und Edge nutzen, wenn der Download nicht klappt.
---

![Playwright](../../../assets/screens/playwright-de-light.png)

Der Tab hat drei Reiter: **Projekt** (Browser für ein vorhandenes Projekt), **Neues Projekt** (ein Projekt aus der Vorlage) und **Vorlage** (welche Vorlage gilt, und eine neue aus einem Projekt).

Ember installiert genau die Browser, die die Playwright-Version deines Projekts braucht, über px und mit den Firmenzertifikaten. Klappt der Download nicht, testest du mit dem Edge, der schon da ist.

Playwright gehört zum Projekt, als npm-Paket in der `package.json`. Die Browser liegen dagegen in einem gemeinsamen Cache unter `%LOCALAPPDATA%\ms-playwright`, ein Ordner pro Browser-Version. Jede Playwright-Version braucht ihre eigenen Browser-Builds, deshalb arbeitet Ember immer mit einem Projektordner.

## Browser für ein Projekt

1. Projektordner wählen. Ember zeigt, welches Playwright das Projekt nutzt und welche `playwright.config` es hat.
2. Browser wählen: nur Chromium, alle Browser oder Microsoft Edge (kein Download).
3. „Browser installieren“ führt im Projekt aus: bei Bedarf `npm ci` (oder `npm install`), danach `npx playwright install chromium` oder für alle Browser `npx playwright install`.

Dabei setzt Ember, woran es hinter einem Firmenproxy meistens scheitert:

- `HTTPS_PROXY` und `HTTP_PROXY` auf px, `NO_PROXY` auf die internen Domains
- `NODE_EXTRA_CA_CERTS`, wenn unter Proxy → Diagnose die Node.js-Zertifikate eingerichtet sind
- `PLAYWRIGHT_DOWNLOAD_HOST`, wenn die Team-Konfiguration einen internen Spiegel nennt (`playwright.downloadHost`)

Bricht der Download ab, sagt Ember, woran es lag. Ein Zertifikatsfehler heißt: Der Proxy bricht TLS auf, die Node.js-Zertifikate fehlen. Ein 407 heißt: px läuft nicht. Ist der Server nicht erreichbar, ist `cdn.playwright.dev` vermutlich gesperrt.

Unter „Installierte Browser“ zeigt Ember, was im Cache liegt: Chromium, Firefox, WebKit. Die Ordnernamen mit Build-Nummer stehen im Tooltip. Zwei weitere Ordner legt Playwright immer mit ab: `ffmpeg` nimmt Videos der Testläufe auf, `winldd` prüft, ob Windows alles hat, was die Browser brauchen. Die gehören dazu, Ember zeigt sie nicht als Browser an. Dasselbe gilt für `chromium_headless_shell`, den Chromium für Läufe ohne Fenster.

## Wenn der Download nicht klappt: Edge

Microsoft Edge ist auf jedem Windows-Rechner installiert, und Playwright kann ihn statt eines heruntergeladenen Chromium nehmen (`channel: 'msedge'`). Scheitert der Browser-Download, bietet Ember darunter „Edge verwenden“ an. Dasselbe passiert mit der Browser-Wahl „Microsoft Edge (kein Download)“.

- Hat das Projekt noch keine `playwright.config`, legt Ember eine `playwright.config.ts` für Edge an. Die Tests laufen dann mit `npm test`.
- Gibt es schon eine, bleibt sie unverändert. Ember legt daneben `playwright.msedge.config.ts` an (bei JavaScript-Projekten `.mjs`). Die übernimmt alles aus der vorhandenen Konfiguration und stellt nur die Chromium-Projekte auf Edge um. Firefox und WebKit lässt sie weg, die bräuchten einen Download. Dazu kommt das npm-Skript `test:edge`, gestartet wird also mit `npm run test:edge`.

Nutzt ein Projekt Playwright ohne Test-Runner (Paket `playwright` statt `@playwright/test`), gibt es keine Konfigurationsdatei. Ember zeigt dann die Zeile, mit der man Edge im Code startet: `chromium.launch({ channel: 'msedge' })`.

## Playwright hinzufügen

Steht im gewählten Ordner noch kein Playwright in der `package.json`, heißt der Knopf „Playwright hinzufügen“. Nach einer Rückfrage:

1. `npm init -y`, falls es noch gar keine `package.json` gibt,
2. `npm install -D @playwright/test @types/node` in der neuesten Version,
3. fehlt eine `playwright.config`, kommen Konfiguration und Beispieltest aus der Ember-Vorlage dazu,
4. die üblichen Einträge in der `.gitignore` und die npm-Skripte, die noch fehlen.

Danach installiert Ember wie oben die Browser oder richtet Edge ein. Vorhandene Dateien und Skripte bleiben, wie sie sind.

## Neues Projekt

„Neues Projekt“ funktioniert wie eine Projektvorlage in Visual Studio. Oben steht, aus welcher Vorlage das Projekt entsteht, darunter wählst du den Speicherort (vorgeschlagen ist der Ordner neben dem aktuellen Projekt oder `%USERPROFILE%\source\repos`) und den Namen. Der Name folgt den npm-Regeln, aus „Mein Projekt“ wird `mein-projekt`. Unter „Das entsteht“ siehst du die Dateien der Vorlage.

Ember kopiert die Vorlage in den neuen Ordner und ersetzt dabei in Dateien und Dateinamen:

| Platzhalter | wird zu |
|---|---|
| `$projectname$`, `$safeprojectname$` | dem Projektnamen, z. B. `kundenportal-tests` |
| `$year$` | dem aktuellen Jahr |

Danach installiert npm die Pakete der Vorlage. Fehlt `@playwright/test`, kommt es dazu. Zum Schluss lädt Ember die Browser oder richtet Edge ein. „Zum Projekt“ öffnet das neue Projekt im ersten Reiter.

„Diesmal die Ember-Vorlage nehmen“ legt ein Projekt aus Embers eingebauter Vorlage an, auch wenn das Team eine eigene hat.

## Die Vorlage

Welche Vorlage gilt, entscheidet Ember in dieser Reihenfolge:

1. `playwright.template` in der [Team-Konfiguration](/Ember/handbuch/team-konfiguration/#playwright), ein Ordner auf dem Netzlaufwerk,
2. sonst ein Ordner, den du im Reiter „Vorlage“ gewählt hast,
3. sonst Embers eingebaute Vorlage.

Ist der Ordner gerade nicht erreichbar (kein VPN), sagt Ember das, und neue Projekte bekommen so lange die eingebaute Vorlage.

Die eingebaute Vorlage entspricht dem, was `npm init playwright` anlegt:

| Datei | Inhalt |
|---|---|
| `package.json` | Skripte `test`, `test:ui`, `test:headed`, `report`, `codegen` |
| `playwright.config.ts` | Tests in `tests/`, HTML-Bericht ohne automatisches Öffnen, Trace beim ersten Wiederholen, Screenshot bei Fehlern, Projekte je nach Browser-Wahl |
| `tests/example.spec.ts` | der Beispieltest von Playwright |
| `.gitignore` | `node_modules`, Testergebnisse, Berichte |
| `.vscode/extensions.json` | Empfehlung für die Playwright-Erweiterung |
| `README.md` | die wichtigsten Befehle |

## Vorlage aus einem Projekt

Hat sich in einem Projekt ein Aufbau bewährt (Page Objects, Fixtures, Config, Skripte), machst du daraus die Vorlage für alle. Du wählst das Projekt, und Ember zeigt alle Dateien mit einem Vorschlag:

| Was | Vorschlag | Warum |
|---|---|---|
| Config, Code, Page Objects, Fixtures, README, `.env.example` | übernehmen | das ist die Struktur |
| Testdateien (`*.spec.ts`, `*.test.ts`) | weglassen | echte Tests gehören ins Projekt, nicht in die Vorlage |
| Ordner `data`, `testdata`, `*-snapshots`, Tabellen, Bilder, PDFs | weglassen | Testdaten |
| `.env`, `.auth`, Storage States, Zertifikate | weglassen | Zugangsdaten |
| `node_modules`, `.git`, Berichte, `package-lock.json` | immer weg | wird neu erzeugt |

Jeden Vorschlag kannst du per Häkchen ändern, ein Häkchen am Ordner gilt für alles darin. Ordner, deren Inhalt wegfällt, bleiben als leere Ordner erhalten (mit `.gitkeep`), damit die Struktur stimmt. Außerdem macht Ember die Vorlage neutral: In der `package.json` wird der Name zu `$projectname$` und die Version zu `1.0.0`, Autor und Repository fallen weg. Ein fester `baseURL` in der Playwright-Config wird zu `process.env.BASE_URL`.

Gespeichert wird in den Vorlagenordner: den aus der `team.json`, sonst deinen eigenen. Gibt es noch keinen, wählst du einen. Eine vorhandene Vorlage ersetzt Ember erst nach einer Rückfrage und sichert sie vorher unter `C:\Ember\Config\template-backups` (die letzten zehn). Neben die Dateien legt Ember `ember-template.json` mit Name, Beschreibung, Datum und wer sie erstellt hat. Die Datei landet nicht in neuen Projekten.

Für die Team-Vorlage brauchst du Schreibrechte auf den Ordner. Alle anderen können damit nur Projekte anlegen.

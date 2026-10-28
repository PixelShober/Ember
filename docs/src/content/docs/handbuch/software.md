---
title: Software
description: Entwicklerwerkzeuge, Playwright-Browser und die Programme aus dem Softwarecenter.
---

![Software-Seite](../../../assets/screens/software-de-light.png)

Die Seite hat drei Teile: die Entwicklerwerkzeuge, die Playwright-Browser für ein Projekt und die Programme aus der Team-Konfiguration (Abschnitt `software`), die über das Softwarecenter kommen.

## Entwicklerwerkzeuge

Der Grundstock für die Entwicklung:

| Werkzeug | Woher, wenn es kein Softwarecenter-Paket gibt | Adminrechte? |
|---|---|---|
| Node.js LTS (mit npm) | Zip von nodejs.org, entpackt nach `C:\Ember\Tools\node`, dazu `C:\Ember\Tools\node` und `%APPDATA%\npm` im Benutzer-PATH | nein |
| Git for Windows | Installer von GitHub (git-for-windows), Installation für dein Konto | nein (außer Git ist schon für alle Benutzer installiert) |
| Visual Studio Code | User Setup von Microsoft | nein |
| Python 3 | Installer von python.org, für dein Konto, mit `py`-Launcher und PATH | nein |
| Visual Studio Professional | Bootstrapper von Microsoft, Workloads .NET-Desktop und ASP.NET | ja |
| .NET SDK (LTS) | Installer aus den Release-Daten von Microsoft | ja |
| PowerShell 7 | MSI von GitHub (PowerShell/PowerShell) | ja |

Stellt eure IT ein Werkzeug im Softwarecenter bereit, nimmt Ember dieses Paket. Sonst lädt Ember den offiziellen Installer über px und prüft ihn, bevor er läuft: die Prüfsumme, die der Hersteller veröffentlicht (nodejs.org, VS Code, .NET, GitHub), und die digitale Signatur des Herstellers. Passt etwas nicht, löscht Ember die Datei und installiert nichts. Neben jedem Werkzeug steht die installierte und die aktuelle Version. „Aktualisieren“ erscheint nur, wenn es wirklich etwas Neueres gibt.

„Fehlende installieren“ arbeitet alle fehlenden Werkzeuge in einem Durchgang ab. Bei Visual Studio, .NET SDK und PowerShell 7 fragt Windows jeweils nach Adminrechten. Visual Studio lädt seine Workloads selbst nach, das dauert je nach Leitung 20 bis 60 Minuten. Nach der Installation finden neu geöffnete Konsolen die Werkzeuge über den PATH.

Die Team-Konfiguration kann die Liste einschränken, die Direktinstallation abschalten (dann bleibt nur Softwarecenter oder Service-Center) und für Visual Studio Bootstrapper und Workloads festlegen, siehe [Team-Konfiguration](/Ember/handbuch/team-konfiguration/#devtools).

## Playwright-Browser

Playwright gehört zum Projekt, als npm-Paket in der `package.json`. Die Browser liegen dagegen in einem gemeinsamen Cache unter `%LOCALAPPDATA%\ms-playwright`, ein Ordner pro Browser-Version. Jede Playwright-Version braucht ihre eigenen Browser-Builds. Deshalb installiert Ember pro Projekt:

1. Projektordner wählen (der mit der `package.json`). Ember zeigt, welches Playwright das Projekt nutzt.
2. „Browser installieren“ führt im Projekt aus: bei Bedarf `npm ci` (oder `npm install`), danach `npx playwright install chromium` oder für alle Browser `npx playwright install`.

Dabei setzt Ember, woran es hinter einem Firmenproxy meistens scheitert:

- `HTTPS_PROXY` und `HTTP_PROXY` auf px, `NO_PROXY` auf die internen Domains
- `NODE_EXTRA_CA_CERTS`, wenn unter Proxy → Diagnose die Node.js-Zertifikate eingerichtet sind
- `PLAYWRIGHT_DOWNLOAD_HOST`, wenn die Team-Konfiguration einen internen Spiegel nennt (`playwright.downloadHost`)

Bricht der Download ab, sagt Ember, woran es lag. Ein Zertifikatsfehler heißt: Der Proxy bricht TLS auf, die Node.js-Zertifikate fehlen. Ein 407 heißt: px läuft nicht. Ist der Server nicht erreichbar, ist `cdn.playwright.dev` vermutlich gesperrt.

:::tip[Ganz ohne Download: Edge]
Microsoft Edge ist auf jedem Windows-Rechner installiert. Mit `use: { channel: 'msedge' }` in der `playwright.config` testet Playwright in Edge, und für Chromium-Tests braucht es keinen Browser-Download.
:::

## Programme aus dem Softwarecenter

Die Programme aus `team.json` installiert Ember nur über den Configuration-Manager-Client, nie an der IT vorbei.

### Was Ember pro Programm anzeigt

- Ob es installiert ist und in welcher Version. Das liest Ember aus „Programme und Features“ (die Uninstall-Einträge der Registry).
- Ob das Softwarecenter es anbietet, als Paket oder als Anwendung, und in welchem Zustand.
- Knöpfe für „Installieren“, „Im Softwarecenter öffnen“ und „Beim Service-Center anfragen“.

### Pakete und Anwendungen

Configuration Manager kennt zwei Arten der Verteilung:

- **Pakete** (Paket-ID plus Programm) sind bei vielen Firmen der Normalfall. „Installieren“ bittet den Client per `CCM_ProgramsManager.ExecuteProgram`, das Programm auszuführen. Das ist dasselbe wie der Knopf im Softwarecenter, mit denselben Rechten und Wartungsfenstern. Ember verfolgt danach den Status und meldet „installiert“, „Neustart nötig“ oder einen Fehler.
- **Anwendungen** öffnet Ember per Direktlink `softwarecenter:SoftwareID=…` im Softwarecenter.

Welche Programme auf deinem Rechner bereitstehen, fragt Ember beim Client ab (`root\ccm\ClientSDK`). Das Team muss dafür keine IDs pflegen, der Name in `team.json` reicht. Platzhalter wie `alfaview*` sind erlaubt.

### Wenn der Direktlink fehlt

Für Pakete gibt es kein verlässliches Link-Format. „Im Softwarecenter öffnen“ zeigt dann die Startseite „Verfügbare Software“ und nennt den Paketnamen. Wer einen festen Link möchte: Im Softwarecenter die App öffnen, über „Teilen“ den Link kopieren (falls die Version das anbietet) und in `team.json` als `softwareCenterLink` eintragen.

### Ohne Softwarecenter oder ohne Freigabe

„Beim Service-Center anfragen“ öffnet die vorbereitete Mail „Software anfordern“ mit Programmname, Benutzer und Rechner. Mehr dazu unter [Service-Center](/Ember/handbuch/service-center/).

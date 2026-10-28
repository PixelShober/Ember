---
title: Installation
description: Ember herunterladen, starten und wissen, wo es seine Dateien ablegt.
---

Ember ist eine einzelne Datei, `Ember.exe`. Sie bringt .NET mit, Adminrechte brauchst du nicht.

## Herunterladen

Die aktuelle Version liegt unter [Releases](https://github.com/PixelShober/Ember/releases/latest). Lade `Ember.exe` herunter und starte sie, zum Beispiel direkt aus dem Download-Ordner.

Beim ersten Start passiert Folgendes:

1. Ember kopiert sich nach `C:\Ember\App\Ember.exe` und legt im Startmenü den Eintrag „Ember“ an.
2. Ember startet sich von dort neu. Die Datei im Download-Ordner kannst du danach löschen.
3. Fehlt die WebView2-Runtime (auf Windows 11 ist sie Teil des Systems), lädt Ember sie herunter und installiert sie. Mehr dazu [unten](#webview2).

Liegt in `C:\Ember\App` schon eine neuere Version, startet Ember diese, statt sie zu überschreiben.

Der feste Ort hat einen Grund: Verknüpfungen, Autostart und eventuelle Firewall-Freigaben der IT beziehen sich alle auf denselben Pfad.

:::note[Keine Schreibrechte auf C:\\?]
Dann läuft Ember einfach vom aktuellen Ort weiter und schreibt einen Hinweis ins Log. Mit `Ember.exe --portable` erzwingst du das, falls du die Kopie nicht willst.
:::

## Die Team-Konfiguration verbinden

Hat dein Team eine `team.json` auf dem Netzlaufwerk, trag beim ersten Start unter **Einstellungen → Team-Konfiguration** ihren Pfad ein, etwa `\\server\entwicklung\ember\team.json`. Ember holt dort Proxy, Softwareliste und Service-Center-Adresse.

Wer Ember im Team verteilt, legt neben die Exe eine Datei `ember-team.txt` mit diesem Pfad. Ember liest sie beim Start und kopiert sie bei der Selbstinstallation mit. Details stehen unter [Team-Konfiguration](/Ember/handbuch/team-konfiguration/).

## WebView2

Die Oberfläche von Ember ist eine Webseite, die in Microsoft WebView2 läuft. Auf fast allen Rechnern ist die Runtime schon installiert. Falls nicht, holt Ember den offiziellen Standalone-Installer von Microsoft:

- über px, wenn es schon läuft,
- sonst über den Firmenproxy aus der Team-Konfiguration, angemeldet mit deinem Windows-Login,
- sonst über den Windows-Systemproxy.

Vor dem Start prüft Ember die digitale Signatur. Stimmt sie nicht, wird nichts ausgeführt. Ohne Adminrechte installiert sich WebView2 nur für dein Konto.

## Testversionen aus Pull Requests

Jeder Pull Request baut eine `Ember.exe`, zu finden als Artefakt „Ember-PR-…“ auf der Seite des CI-Laufs. Diese Testversionen erkennt Ember an der Versionsnummer (`0.0.0-ci.…`). Sie laufen immer portabel, überschreiben keine installierte Version und laden keine Updates. So siehst du wirklich den Stand des Pull Requests.

## Wo Ember seine Dateien ablegt

| Was | Wo |
|---|---|
| Programm | `C:\Ember\App\Ember.exe` |
| px | `C:\Ember\px` (änderbar in den Einstellungen) |
| llama.cpp | `C:\Ember\llama.cpp\<version>-<variante>` |
| Einstellungen | `%APPDATA%\Ember\settings.json` |
| Kopie der Team-Konfiguration | `%APPDATA%\Ember\team.cache.json` |
| Logs | `%APPDATA%\Ember\logs\ember-JJJJ-MM-TT.log` |
| Gesicherte Energiewerte | `%APPDATA%\Ember\power-backup.json` |
| Oberflächen-Updates, WebView2-Daten | `%LOCALAPPDATA%\Ember\ui`, `%LOCALAPPDATA%\Ember\WebView2` |
| Diagnoseberichte | `%LOCALAPPDATA%\Ember\reports` |
| KI-Modelle | `%LOCALAPPDATA%\Ember\models` (änderbar) |

## Deinstallieren

Erst unter **Proxy** auf „Deinstallieren“ klicken. Damit verschwinden px, der Autostart und alle Proxy-Einträge, die auf px zeigen. Danach kannst du `C:\Ember`, `%APPDATA%\Ember`, `%LOCALAPPDATA%\Ember` und den Startmenü-Eintrag löschen.

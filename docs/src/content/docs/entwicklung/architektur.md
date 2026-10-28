---
title: Architektur
description: WPF-Hülle mit WebView2, eine JSON-Bridge und eine Svelte-Oberfläche.
---

Ember besteht aus drei Teilen:

```
┌─────────────────────────────── Ember.exe (WPF, .NET 10) ───────────────────────────────┐
│                                                                                        │
│   MainWindow ── WebView2 ── https://ui.ember.invalid/  ←  ui/dist (Svelte 5, TypeScript) │
│        │                         │  postMessage (JSON)                                  │
│        │                         ▼                                                      │
│        └──────────────►  Bridge  ──►  EmberHost  ──►  Module in Ember.Core              │
│                                        (Proxy, Git, Software, Energie, System, KI …)    │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

`src/Ember.App` ist die Hülle: ein WPF-Fenster mit WebView2, Selbstinstallation nach `C:\Ember\App`, Einzelinstanz, Exe-Update, WebView2-Bootstrap, Datei-Dialoge. Die einzige externe Bibliothek im ganzen Programm ist `Microsoft.Web.WebView2`.

`src/Ember.Core` enthält die gesamte Logik ohne WPF und ganz ohne NuGet-Pakete. Was Windows-spezifisch ist, läuft über die Registry-API, P/Invoke, CLI-Werkzeuge (`powercfg`, `gpresult`, `git`, `npm`) oder PowerShell 5.1. COM-Objekte wie Outlook und WScript.Shell werden late-bound angesprochen, Interop-Assemblies braucht es nicht. Dadurch lässt sich Core auch unter Linux bauen und testen.

`ui` ist die Oberfläche: Svelte 5 mit Runes, TypeScript, Vite. Zur Laufzeit hängt nur die Svelte-Runtime im Bundle (rund 60 KB gzip für alles).

## Warum WebView2 und nicht reines WPF

Die Oberfläche ist eine Webseite. Sie lässt sich mit allem bauen, was das Web kann, von Übergängen bis zu den Slide-Animationen fürs geplante Onboarding, und sie lässt sich getrennt von der Exe aktualisieren. Ein Oberflächen-Update braucht keinen Neustart (siehe [Updates](/Ember/handbuch/updates/)). Die Exe ändert sich selten und bleibt klein in ihrer Verantwortung: Sie führt Systemaktionen aus und sonst nichts.

Eine echte PWA, also eine Oberfläche, die von einem Webserver kommt, wäre noch flexibler. Ohne Netz gäbe es dann aber keine Oberfläche, und eine kompromittierte Webseite könnte Systemaktionen auslösen. Deshalb liegt die Oberfläche immer lokal: eingebettet in die Exe oder als signaturgeprüftes Paket in `%LOCALAPPDATA%\Ember\ui`.

## Sicherheit der Hülle

- WebView2 lädt ausschließlich `https://ui.ember.invalid/` (eine lokale Ordner-Zuordnung, `.invalid` ist als Domain reserviert). Jede andere Navigation wird abgebrochen. Links öffnen sich im Standardbrowser, erlaubt sind nur `https:`, `http:`, `mailto:` und `softwarecenter:`.
- Die Bridge nimmt nur Nachrichten von diesem Ursprung an.
- Die gebaute Oberfläche hat eine strenge Content-Security-Policy (`script-src 'self'`, keine fremden Verbindungen).
- DevTools, Browser-Tastenkürzel und das Standard-Kontextmenü sind aus. Die DevTools gibt es nur im Debug-Build oder für Admins mit eingeblendeten erweiterten Modulen.
- Downloads (px, llama.cpp, UI-Pakete, Exe) werden per SHA256 geprüft, der WebView2-Installer per Authenticode-Signatur. Bei den Entwicklerwerkzeugen prüft Ember beides: die Prüfsumme, die der Hersteller veröffentlicht, und seine Signatur.

## Ablauf beim Start

1. `App.OnStartup` lädt die Sprache aus den Einstellungen und prüft per `SelfInstaller`, ob Ember aus `C:\Ember\App` läuft. Falls nicht, wird kopiert und neu gestartet.
2. `SingleInstance` sorgt für genau ein Fenster. Ein zweiter Start holt das erste nach vorn.
3. `EmberHost.InitializeAsync` lädt die Team-Konfiguration und startet die Timer für Log, px-Status und Updates.
4. `WebView2Bootstrap` installiert bei Bedarf die WebView2-Runtime.
5. `MainWindow` erstellt die WebView2-Umgebung (Sprache = Windows-Sprache), wählt das UI-Paket (`UiHost`) und navigiert.
6. Die Oberfläche ruft `app.info` und Co. auf und meldet sich mit `app.ready`. Kommt diese Meldung bei einem heruntergeladenen UI-Paket nicht, fällt Ember auf die eingebaute Oberfläche zurück.

## Projektstruktur

```
src/Ember.App/        WPF-Hülle (MainWindow, UiHost, SelfInstaller, ExeUpdate, WebView2Bootstrap)
src/Ember.Core/
  Infrastructure/     Log, L (Texte), Net, ProcessRunner, PowerShell, ConsoleEncoding, Paths, AppInfo
  Config/             UserSettings, TeamConfig, EffectiveSettings
  Jobs/               SetupStep, JobManager (Schrittfolgen mit Fortschritt, Abbruch, Wiederholen)
  Px/                 px installieren und steuern, Proxy pro Werkzeug, JSONC- und NuGet-Editor
  Modules/            GitIdentity, Software, Mail, Power, Wol, SystemChecks, Ai
  Updates/            Updater, UI-Pakete
  Bridge/             Bridge (JSON-Protokoll), EmberHost (alle Methoden)
tests/Ember.Core.Tests/  xUnit
ui/                   Svelte-Oberfläche, Sprachdateien in ui/src/locales
docs/                 diese Doku (Starlight)
config/               team.example.json und das JSON-Schema
scripts/              Prüfungen und Release-Helfer (Node, ohne Pakete)
```

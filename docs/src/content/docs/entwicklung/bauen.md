---
title: Bauen und starten
description: Ember lokal bauen, die Oberfläche im Browser entwickeln, Tests ausführen.
---

Releases baut die Pipeline. Lokal bauen musst du nur, wenn du an Ember selbst arbeitest.

## Voraussetzungen

- .NET SDK 10 (`dotnet --list-sdks`). Ohne Adminrechte geht auch das Zip von [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet), entpackt nach `%LOCALAPPDATA%\dotnet` und in den Benutzer-PATH eingetragen.
- Node.js 22 oder neuer (für die Doku 22.23 oder neuer)
- Windows zum Ausführen. Bauen und Testen klappt auch unter Linux und macOS.

Hinter dem Firmenproxy brauchen `npm ci` und `dotnet restore` Zugriff auf npmjs.org und nuget.org. Mit px geht das ohne weitere Einstellungen.

## Oberfläche

```bash
cd ui
npm ci
npm run dev      # http://localhost:5173 mit Beispieldaten
```

Im normalen Browser gibt es keine Ember-Exe. Die Oberfläche merkt das und lädt `src/lib/mock.ts`, eine nachgebaute Bridge mit Beispieldaten. Ändern kann sie am System nichts. Mit `?lang=en` oder `?lang=de-CH` in der Adresse prüfst du andere Sprachen.

```bash
npm run check    # svelte-check, TypeScript
npm test         # Vitest
npm run build    # nach ui/dist
```

## Exe

```bash
dotnet build src/Ember.App
dotnet run --project src/Ember.App
```

Der Build bettet `ui/dist` als Zip in die Exe ein. Fehlt `ui/dist`, gibt es eine Warnung und Ember zeigt eine Platzhalterseite. Im Debug-Build kopiert sich Ember nicht nach `C:\Ember\App`, und die DevTools sind an (<kbd>F12</kbd> oder Rechtsklick → „Untersuchen“).

Für die Arbeit an der Oberfläche mit echtem Host startest du den Vite-Server und Ember mit `--ui-dev`. Ember lädt dann `http://localhost:5173`, Änderungen erscheinen sofort.

```bash
dotnet run --project src/Ember.App -- --ui-dev
```

Die Release-Exe (eine Datei, .NET eingebaut, rund 65 MB):

```bash
dotnet publish src/Ember.App -c Release -o publish -p:Version=1.2.3
```

## Tests und Prüfungen

```bash
dotnet test tests/Ember.Core.Tests
node scripts/check-encoding.mjs   # UTF-8, Umlaute, Übersetzungsschlüssel
node scripts/check-deps.mjs       # Abhängigkeits-Wächter
```

Die .NET-Tests laufen auch unter Linux. Sie decken unter anderem das Parsen deutscher und englischer `powercfg`-Ausgaben, die OEM-Codepage, mailto-Kodierung, den JSONC-Editor für VS Code, Magic Packets und die Auswahl der llama.cpp-Variante ab.

## Doku

```bash
cd docs
npm ci
npm run dev      # http://localhost:4321/Ember/
```

Die Screenshots im Handbuch entstehen aus der Oberfläche mit Beispieldaten:

```bash
cd ui && npm run build && npx vite preview --port 4173 &
node scripts/screenshots.mjs docs/src/assets/screens de
```

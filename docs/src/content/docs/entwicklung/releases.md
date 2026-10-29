---
title: Releases und Pipelines
description: Vom Pull Request zum Release, ohne dass irgendwer lokal baut.
---

Lokal baut niemand ein Release. Jeder Pull Request wird in der CI gebaut und getestet, jeder Merge nach `main` erzeugt automatisch ein GitHub-Release.

## Der PR-Titel bestimmt die Version

Titel folgen [Conventional Commits](https://www.conventionalcommits.org/de/):

| Titel | Version vorher → nachher |
|---|---|
| `feat: Energieprofil für Builds` | 1.2.0 → 1.3.0 |
| `fix(proxy): NuGet-Proxy erkennen` | 1.2.0 → 1.2.1 |
| `feat!: neues Format der Team-Konfiguration` | 1.2.0 → 2.0.0 |
| `docs: …`, `chore: …`, `ci: …` | Patch, aber nur wenn sich die App geändert hat |

Das erste Release ist immer 1.0.0. Der Workflow `pr-title.yml` prüft den Titel schon im Pull Request. Die Regeln stehen in `scripts/next-version.mjs`.

## Workflows

| Workflow | Wann | Was |
|---|---|---|
| `ci.yml` | jeder PR, jeder Push auf `main` | UI: Typprüfung, Tests, Build, Umlaut-Prüfung. Exe auf `windows-latest`: .NET-Tests, Abhängigkeits-Wächter, `dotnet publish`. Doku-Build. Artefakt „Ember-PR-&lt;nr&gt;“ mit Exe und UI-Paket. |
| `pr-title.yml` | PR geöffnet oder Titel geändert | Titel nach Conventional Commits |
| `release.yml` | Push auf `main` (also nach jedem Merge) | Version bestimmen, alles bauen und testen, Release mit `Ember.exe`, `ember-ui-<version>.zip`, `latest.json`, `SHA256SUMS.txt` |
| `docs.yml` | Änderungen unter `docs/` | Starlight bauen, auf GitHub Pages veröffentlichen |

Alle Workflows nutzen nur offizielle `actions/*` und die `gh`-CLI des Runners. Der PR-Titel kommt über eine Umgebungsvariable ins Skript, nie direkt in den Befehl.

Ein Release entsteht nur, wenn sich seit dem letzten Tag etwas unter `src/`, `ui/` oder `config/` geändert hat. Reine Doku-Änderungen aktualisieren nur die Doku-Seite. Ein Release lässt sich auch von Hand mit fester Version auslösen (Actions → Release → „Run workflow“).

## latest.json

```json
{
  "version": "1.3.0",
  "bridge": 1,
  "exe": { "name": "Ember.exe", "sha256": "…", "size": 66012345, "codeVersion": "1.2.0" },
  "ui": { "name": "ember-ui-1.3.0.zip", "sha256": "…", "version": "1.3.0", "requiresBridge": 1 },
  "notes": "https://github.com/PixelShober/Ember/releases/tag/v1.3.0"
}
```

`codeVersion` ist die letzte Version, in der sich der Exe-Code (`src/`) geändert hat. Im Beispiel hat 1.3.0 nur die Oberfläche geändert: Eine installierte Ember 1.2.0 lädt das UI-Paket und lädt neu, ohne Neustart. Erst wenn `codeVersion` höher ist als die eigene Version, holt sie die neue Exe.

## Einmalig im Repository einstellen

- Settings → Pages → Source: „GitHub Actions“ (für die Doku)
- Settings → Actions → General: Workflows dürfen Releases anlegen. Das übernimmt `permissions: contents: write` in `release.yml`, solange die Organisation das nicht verbietet.
- Optional: Branch-Schutz für `main` mit den Checks „Oberfläche“, „Exe“ und „PR-Titel“ als Pflicht.

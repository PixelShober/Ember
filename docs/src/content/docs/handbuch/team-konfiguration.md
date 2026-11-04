---
title: Team-Konfiguration
description: Eine team.json auf dem Netzlaufwerk für interne Domains, Software, Mailvorlagen, Energieprofile und Rollen.
---

Die Team-Konfiguration ist eine JSON-Datei, die alle Ember-Installationen eines Teams lesen. Sie liegt auf einem Netzlaufwerk, zum Beispiel `\\server\entwicklung\ember\team.json`. Darin steht alles, was für alle gleich ist und nicht ins öffentliche Repository gehört: interne Domains, Softwareliste, Entwicklerwerkzeuge, Service-Center-Adresse, Mailvorlagen, Energieprofile und die Rollen, also wer welche Module sieht.

Pflegen sollte sie eine Person oder ein kleines Team mit Schreibrechten auf dem Ordner. Alle anderen brauchen nur Leserechte. Ember schreibt nur an einer Stelle hinein: Im Tab [Berechtigungen](/Ember/handbuch/berechtigungen/) ändert, wer Schreibrechte hat, den Abschnitt `access`. Alles andere bleibt Handarbeit.

## So findet Ember die Datei

In dieser Reihenfolge, der erste Treffer gilt:

1. der Pfad unter **Einstellungen → Team-Konfiguration** (oder den jemand beim ersten Start eingetragen hat),
2. eine Gruppenrichtlinie: der Zeichenfolgenwert `TeamConfig` unter `HKEY_CURRENT_USER\Software\Policies\Ember` oder `HKEY_LOCAL_MACHINE\Software\Policies\Ember`,
3. eine Datei `ember-team.txt` neben `Ember.exe` oder in `C:\Ember\App`, in der der Pfad steht,
4. eine `team.json` direkt neben `Ember.exe`,
5. die Umgebungsvariable `EMBER_TEAM_CONFIG`.

Am einfachsten verteilt ihr Ember über das Netzlaufwerk: `Ember.exe` neben die `team.json` legen. Wer Ember von dort startet, ist sofort verbunden. Ember kopiert sich nach `C:\Ember\App` und merkt sich dabei in `C:\Ember\App\ember-team.txt`, wo die `team.json` liegt. Ab dann startet es aus dem Startmenü und findet die Datei trotzdem. Wer Ember von GitHub lädt, trägt den Pfad beim ersten Start ein oder schaltet Ember mit einem [Schlüssel](/Ember/handbuch/berechtigungen/#ohne-teamjson-die-schlüssel) frei.

Ember liest die Datei bei jedem Start mit deinem Windows-Login, wie jede andere Datei auf dem Laufwerk. Die letzte erfolgreich gelesene Fassung speichert es in `C:\Ember\Config\team.cache.json`. Ohne VPN arbeitet Ember mit dieser Kopie weiter und zeigt in den Einstellungen, von wann sie ist. Hängt das Laufwerk, wartet Ember höchstens zehn Sekunden.

## Beispiel

```json
{
  "$schema": "https://pixelshober.github.io/Ember/team.schema.json",
  "version": 3,
  "name": "Team Entwicklung",
  "proxy": { "internalDomains": ["firma.local", "intranet.firma.example"] },
  "serviceCenter": { "email": "servicecenter@firma.example", "subjectPrefix": "[Ember]" },
  "software": [
    { "name": "Visual Studio Professional 2022", "sccmName": "Visual Studio Professional*", "category": "IDE" },
    { "name": "Alfaview", "sccmName": "alfaview*", "category": "Kommunikation" }
  ],
  "access": {
    "contact": "ember-admins@firma.example",
    "roles": [
      { "name": "Admins", "modules": ["*"], "users": ["FIRMA\\max.mustermann"] },
      { "name": "Test", "modules": ["proxy", "playwright"], "groups": ["FIRMA\\Test"] }
    ]
  }
}
```

Mit der `$schema`-Zeile schlägt VS Code beim Bearbeiten alle Felder vor und markiert Tippfehler. Das vollständige Beispiel liegt im Repository unter [`config/team.example.json`](https://github.com/PixelShober/Ember/blob/main/config/team.example.json).

Texte wie Notizen, Mailvorlagen und Profilnamen dürfen ein einfacher String sein oder pro Sprache angegeben werden: `{ "de": "…", "en": "…" }`.

## Die Abschnitte

### proxy

Den Firmenproxy (PAC-Datei oder fester Proxy) erkennt Ember bei jedem Start selbst aus den Windows-Einstellungen, er steht deshalb nicht in der `team.json`.

| Feld | Bedeutung |
|---|---|
| `internalDomains` | Domains, die ohne Proxy erreichbar sind. Ember trägt sie bei px, `NO_PROXY`, npm, git, NuGet und VS Code als Ausnahme ein, siehe [Proxy](/Ember/handbuch/proxy/#interne-server-ohne-proxy). |
| `port` | Port von px, Standard 3128 |

### serviceCenter und mailTemplates

`serviceCenter.email` ist der Empfänger aller Mails, `subjectPrefix` wird jedem Betreff vorangestellt. Unter `mailTemplates` lassen sich die vier Vorlagen `softwareRequest`, `adminAction`, `problem` und `accessRequest` ersetzen, jeweils mit `subject` und `body`. Platzhalter: `{user}`, `{computer}`, `{app}`, `{details}`, `{date}`, `{ember}`.

### software

Die Kacheln zeigen ohnehin alles, was das Softwarecenter anbietet. Ein Eintrag hier sortiert ein Programm unter eine Überschrift, ergänzt eine Notiz oder einen festen Link. Programme, die das Softwarecenter (noch) nicht anbietet, erscheinen trotzdem, mit dem Weg über das Service-Center.

| Feld | Bedeutung |
|---|---|
| `name` | Anzeigename, Pflicht |
| `sccmName` | Name im Softwarecenter, `*` und `?` erlaubt. Standard: `name` |
| `displayName` | Name unter „Programme und Features“, mit Platzhaltern. Standard: `name*` |
| `category` | Überschrift, unter der das Programm erscheint |
| `note` | kurze Notiz, zum Beispiel zur Lizenz |
| `softwareCenterLink` | fester Direktlink (`softwarecenter:SoftwareID=…`), falls der automatische nicht passt |

### Energieprofile

`power.allowedSchemes` legt fest, welches Schema Ember ändern darf (Standard `["SCHEME_BALANCED"]`). Unter `power.profiles` ersetzt ein Profil mit derselben `id` ein eingebautes (`ai-performance`, `sleep-ac`, `lid-ac`), eine neue `id` kommt dazu.

```json
{
  "id": "sleep-ac",
  "name": { "de": "Kein Standby am Netzteil", "en": "No sleep when plugged in" },
  "settings": [
    { "sub": "SUB_SLEEP", "setting": "STANDBYIDLE", "ac": 0 },
    { "sub": "SUB_VIDEO", "setting": "VIDEOIDLE", "ac": 1200 }
  ]
}
```

`sub` und `setting` sind Aliase wie bei `powercfg` oder GUIDs. `ac` gilt am Netzteil, `dc` im Akkubetrieb, ein fehlender Wert bleibt unverändert. Zeiten stehen in Sekunden.

### access

Wer welche Module sieht. Jede Rolle hat einen Namen, eine Liste von Modulen und ihre Mitglieder. Wer in mehreren Rollen ist, sieht die Module aller dieser Rollen. Wer in keiner ist, sieht nur einen Sperrbildschirm mit „Zugriff anfragen“. Ohne Rollen sieht also niemand etwas.

| Feld | Bedeutung |
|---|---|
| `roles[].name` | Name der Rolle, zum Beispiel „Admins“ oder „Test“ |
| `roles[].modules` | `proxy`, `git`, `software`, `playwright`, `system`, `power` (Energie), `ai` (KI), `access` (Berechtigungen), `settings` (Einstellungen), `log`. `"*"` steht für alle, auch für Module, die später dazukommen. |
| `roles[].users`, `roles[].groups` | Konten und AD-Gruppen, mit oder ohne Domäne (`FIRMA\Entwicklung` oder `Entwicklung`) |
| `contact` | Mailadresse für Zugriffsanfragen. Standard: das Service-Center. |

Die Startseite gibt es, sobald jemand mindestens ein Modul hat. Sie zeigt nur Karten zu den eigenen Modulen. Energie, KI und Berechtigungen blendet <kbd>Strg</kbd> + <kbd>E</kbd> aus und ein. Gepflegt werden die Rollen am bequemsten im Tab [Berechtigungen](/Ember/handbuch/berechtigungen/).

Ältere Dateien mit `admins` und einer Zugriffsliste (`access.users`, `access.groups`) funktionieren weiter. Aus `admins` macht Ember die Rolle „Admins“ mit allen Modulen. Die alte Zugriffsliste wird zur Rolle „Ember“ mit allem außer Energie, KI und Berechtigungen. Speichert jemand im Tab Berechtigungen, schreibt Ember das neue Format.

### devTools

Steuert die Entwicklerwerkzeuge im Software-Tab.

| Feld | Bedeutung |
|---|---|
| `tools` | Welche Werkzeuge erscheinen: `nodejs`, `git`, `vscode`, `python`, `visualstudio`, `dotnet`, `pwsh`, `bruno`. Standard: alle. |
| `allowDirect` | `false`: Ember installiert nie direkt vom Hersteller, nur über das Softwarecenter oder per Mail ans Service-Center. Standard: `true`. |
| `visualStudio.bootstrapperUrl` | Bootstrapper, Standard `https://aka.ms/vs/17/release/vs_professional.exe` |
| `visualStudio.workloads` | Workload-IDs für `--add`. Standard: `Microsoft.VisualStudio.Workload.ManagedDesktop` und `Microsoft.VisualStudio.Workload.NetWeb` |

Gibt es ein passendes Paket im Softwarecenter, nimmt Ember immer das.

### playwright

`playwright.downloadHost` ist ein interner Spiegel für die Browser-Downloads, etwa ein Artifactory-Remote auf `cdn.playwright.dev`. Ember setzt ihn als `PLAYWRIGHT_DOWNLOAD_HOST`, wenn es Browser installiert.

### models

`models` ergänzt die Modellliste im KI-Tab. Eingebaut ist Qwen3.8-27B (UD-Q4_K_XL), ein Eintrag mit derselben `id` (`qwen3.8-27b`) ersetzt es.

| Feld | Bedeutung |
|---|---|
| `url` | direkte Download-Adresse der GGUF-Datei |
| `path` | statt `url`: eine GGUF-Datei, die schon liegt, etwa auf einem Netzlaufwerk. Ember lädt nichts herunter und löscht nichts. |
| `id`, `name`, `note`, `sha256` | Kennung, Anzeigename, kurze Notiz, Prüfsumme für den Download |
| `alias` | Name für `--alias` und für Pi, Standard: der Dateiname |
| `mtp` | `true`, wenn das Modell einen MTP-Kopf hat |
| `args` | llama-server-Argumente des Modells, eines pro Eintrag, etwa `["--temp", "1.0", "--top-k", "20"]` |

```json
"models": [
  { "id": "devstral", "name": "Devstral Small", "path": "\\\\server\\ki\\Devstral-Small-Q4_K_M.gguf" }
]
```

### update.mirror

Ist GitHub im Firmennetz gesperrt, kann ein UNC-Ordner als Update-Quelle dienen. Kopiert man dorthin `latest.json`, `Ember.exe` und `ember-ui-<version>.zip` eines Releases, holt Ember Updates von dort statt von GitHub.

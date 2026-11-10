---
title: Team-Konfiguration
description: "Die signierte Teamdatei auf dem Netzlaufwerk: Rollen, Sperrliste, Service-Center, Vorgesetzte, Updates, Playwright, Mailtexte."
---

Die Teamdatei lesen alle Ember-Installationen eines Teams. Sie liegt auf einem Netzlaufwerk, zum Beispiel `\\server\entwicklung\ember\team.ember`. Darin steht, was für alle gleich ist und nicht ins öffentliche Repository gehört.

Im Betrieb ist sie **signiert**: Ember lädt sie nur, wenn die Unterschrift der Admins passt, sie an ihrem Ort liegt und nicht älter ist als die zuletzt gesehene Fassung. Bearbeitet wird sie deshalb nur im Tab [Berechtigungen](/Ember/handbuch/berechtigungen/), nicht im Editor. Wie das Signieren funktioniert und wie ihr anfangt, steht dort unter [Einrichten](/Ember/handbuch/berechtigungen/#einrichten-schlüssel-und-erste-teamdatei).

## So findet Ember die Datei

In dieser Reihenfolge, der erste Treffer gilt:

1. der Pfad unter **Einstellungen → Team-Konfiguration** (oder den jemand beim ersten Start eingetragen hat),
2. eine Datei `ember-team.txt` neben `Ember.exe` oder in `C:\Ember\App`, in der der Pfad steht,
3. eine `team.ember` oder `team.json` direkt neben `Ember.exe`,
4. die Umgebungsvariable `EMBER_TEAM_CONFIG`.

Am einfachsten verteilt ihr Ember über das Netzlaufwerk: `Ember.exe` neben die `team.ember` legen. Wer Ember von dort startet, ist sofort verbunden. Ember kopiert sich nach `C:\Ember\App` und merkt sich dabei in `C:\Ember\App\ember-team.txt`, wo die Teamdatei liegt. Wer Ember anders bekommt, trägt beim ersten Start den Pfad ein. Ohne Teamdatei bleibt Ember gesperrt.

Ember liest die Datei bei jedem Start und danach alle zwei Minuten, mit deinem Windows-Login wie jede andere Datei auf dem Laufwerk. Die letzte gültige Fassung speichert es in `C:\Ember\Config\team.cache.json`. Ohne VPN arbeitet Ember mit dieser Kopie weiter, höchstens 14 Tage lang. Hängt das Laufwerk, wartet Ember höchstens zehn Sekunden.

## Pflicht und optional

| Abschnitt | Pflicht | Bearbeitet im Tab Berechtigungen unter |
|---|---|---|
| `serviceCenter` | ja | Team |
| `supervisor` | ja | Team |
| `access` | ja | Rollen, Sperrliste |
| `update` | ja | Team |
| `playwright` | ja | Team |
| `mailTemplates` | ja | Team |
| `proxy`, `software`, `power`, `devTools`, `name` | nein | Teamdatei → Weitere Einstellungen (JSON) |

`version`, `location`, `signedBy` und `signedAt` setzt Ember beim Signieren selbst.

## Beispiel

So sieht der Inhalt aus, bevor er signiert wird. Mit einer Ember-Version ohne eingebauten Schlüssel lässt sich genau diese Datei als `team.json` laden und dann im Tab Berechtigungen signieren.

```json
{
  "version": 1,
  "serviceCenter": { "email": "servicecenter@firma.example", "name": "IT-Service-Center", "subjectPrefix": "[Ember]" },
  "supervisor": { "name": "Petra Lindner", "email": "petra.lindner@firma.example" },
  "access": {
    "roles": [
      { "name": "Admins", "modules": ["*"], "users": ["FIRMA\\max.mustermann"] },
      { "name": "Entwickler", "modules": ["proxy", "playwright", "git", "software"], "groups": ["FIRMA\\Entwicklung"], "hideTools": ["bruno"] },
      { "name": "Test", "modules": ["proxy", "playwright"], "groups": ["FIRMA\\Test"] }
    ],
    "blocked": { "users": ["FIRMA\\ehemalig.mitarbeiter"] }
  },
  "update": { "mirror": "\\\\fs01\\entwicklung\\ember\\releases" },
  "playwright": { "template": "\\\\fs01\\entwicklung\\vorlagen\\playwright" },
  "mailTemplates": {}
}
```

Das Beispiel liegt im Repository unter [`config/team.example.json`](https://github.com/PixelShober/Ember/blob/main/config/team.example.json), das Schema unter [`team.schema.json`](/Ember/team.schema.json).

## Die Abschnitte

### proxy

Den Firmenproxy (PAC-Datei oder fester Proxy) erkennt Ember bei jedem Start selbst aus den Windows-Einstellungen, er steht deshalb nicht in der `team.json`.

| Feld | Bedeutung |
|---|---|
| `internalDomains` | Domains, die ohne Proxy erreichbar sind. Ember trägt sie bei px, `NO_PROXY`, npm, git, NuGet und VS Code als Ausnahme ein, siehe [Proxy](/Ember/handbuch/proxy/#interne-server-ohne-proxy). |
| `port` | Port von px, Standard 3128 |

### serviceCenter, supervisor und mailTemplates

`serviceCenter.email` bekommt die Mails „Software anfordern“ und „Problem melden“, `subjectPrefix` wird jedem Betreff vorangestellt. `serviceCenter.problemIntro` ersetzt den ersten Satz der Problem-Mail. Ohne den Eintrag schreibt Ember: „Ich stehe vor folgendem Problem in meinem Entwicklungs- bzw. Arbeitsprozess, das sich wie folgt gestaltet:“.

`supervisor` ist die direkte Vorgesetzte bzw. der direkte Vorgesetzte. Anträge auf Adminrechte gehen dorthin und beginnen mit „Hallo“ und dem Vornamen:

```json
"supervisor": { "name": "Petra Lindner", "email": "petra.lindner@firma.example" }
```

Den Vornamen nimmt Ember aus `name`, auch bei „Lindner, Petra“. Passt das nicht, setzt `firstName` ihn fest. Fehlt `supervisor.email`, bietet Ember den Antrag nicht an.

Unter `mailTemplates` lassen sich die vier Vorlagen `softwareRequest`, `adminAction`, `problem` und `accessRequest` ersetzen, jeweils mit `subject` und `body`. Platzhalter: `{user}`, `{computer}`, `{app}`, `{details}`, `{date}`, `{ember}`, `{sender}` (dein Vor- und Nachname), `{supervisor}` (Vorname der bzw. des Vorgesetzten) und `{problemIntro}`. Steht `{sender}` nicht im Text, hängt Ember „Mit freundlichen Grüßen“ und den Namen selbst an.

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

Wer welche Module sieht. Jede Rolle hat einen Namen, eine Liste von Modulen und ihre Mitglieder. Wer in mehreren Rollen ist, sieht die Module aller dieser Rollen. Wer in keiner ist, sieht nur einen Sperrbildschirm. Ohne Rollen sieht also niemand etwas.

| Feld | Bedeutung |
|---|---|
| `roles[].name` | Name der Rolle, zum Beispiel „Admins“ oder „Test“ |
| `roles[].modules` | `proxy`, `git`, `software`, `playwright`, `system`, `power` (Energie), `ai` (KI), `access` (Berechtigungen), `settings` (Einstellungen), `log`. `"*"` steht für alle, auch für Module, die später dazukommen. |
| `roles[].users`, `roles[].groups` | Konten und AD-Gruppen, mit oder ohne Domäne (`FIRMA\Entwicklung` oder `Entwicklung`) |
| `roles[].hideTools` | Entwicklerwerkzeuge, die die Rolle nicht angeboten bekommt, z. B. `["bruno"]` |
| `blocked.users`, `blocked.groups` | Sperrliste: wer hier steht, kommt nicht in Ember, egal was eine Rolle sagt |

Die Startseite gibt es, sobald jemand mindestens ein Modul hat. Sie zeigt nur Karten zu den eigenen Modulen. Energie, KI und Berechtigungen blendet <kbd>Strg</kbd> + <kbd>E</kbd> aus und ein. Gepflegt werden die Rollen am bequemsten im Tab [Berechtigungen](/Ember/handbuch/berechtigungen/).

Ältere Dateien mit `admins` und einer Zugriffsliste (`access.users`, `access.groups`) liest Ember noch. Aus `admins` wird die Rolle „Admins“ mit allen Modulen, aus der alten Zugriffsliste die Rolle „Ember“ mit allem außer Energie, KI und Berechtigungen. Beim Signieren schreibt Ember das neue Format.

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

`playwright.template` ist der Ordner mit der Projektvorlage des Teams, zum Beispiel `\\fs01\entwicklung\vorlagen\playwright`. „Neues Projekt“ kopiert ihn, im Reiter „Vorlage“ lässt er sich aus einem Projekt neu erzeugen. Steht hier nichts, kann jeder seinen eigenen Ordner wählen. Mehr unter [Playwright](/Ember/handbuch/playwright/#die-vorlage).

### update.mirror

Ist GitHub im Firmennetz gesperrt, kann ein UNC-Ordner als Update-Quelle dienen. Kopiert man dorthin `latest.json`, `Ember.exe` und `ember-ui-<version>.zip` eines Releases, holt Ember Updates von dort statt von GitHub.

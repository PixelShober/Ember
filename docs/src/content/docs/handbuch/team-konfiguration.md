---
title: Team-Konfiguration
description: Eine team.json auf dem Netzlaufwerk für interne Domains, Software, Mailvorlagen, Energieprofile, Zugriff und Admins.
---

Die Team-Konfiguration ist eine JSON-Datei, die alle Ember-Installationen eines Teams lesen. Sie liegt auf einem Netzlaufwerk, zum Beispiel `\\server\entwicklung\ember\team.json`. Darin steht alles, was für alle gleich ist und nicht ins öffentliche Repository gehört: interne Domains, Softwareliste, Entwicklerwerkzeuge, Service-Center-Adresse, Mailvorlagen, Energieprofile, wer Ember nutzen darf und wer Admin ist.

Pflegen sollte sie eine Person oder ein kleines Team mit Schreibrechten auf dem Ordner. Alle anderen brauchen nur Leserechte. Ember schreibt nur an einer Stelle hinein: Im Tab [Berechtigungen](/Ember/handbuch/berechtigungen/) ändern Admins mit Schreibrechten die Abschnitte `access` und `admins`. Alles andere bleibt Handarbeit.

## So findet Ember die Datei

In dieser Reihenfolge:

1. der Pfad unter **Einstellungen → Team-Konfiguration**,
2. eine Datei `ember-team.txt` neben `Ember.exe` (oder in `C:\Ember\App`), in der der Pfad steht,
3. die Umgebungsvariable `EMBER_TEAM_CONFIG`.

Zum Verteilen eignet sich Weg 2 am besten. Leg `Ember.exe` und `ember-team.txt` zusammen ab, und wer Ember von dort startet, ist sofort verbunden. Ember kopiert die Datei bei der Selbstinstallation mit.

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
  "admins": { "users": ["FIRMA\\max.mustermann"], "groups": ["FIRMA\\Entwicklung-Admins"] },
  "access": { "groups": ["FIRMA\\Entwicklung"], "contact": "ember-admins@firma.example" }
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

### admins

Wer hier steht, ist Admin: sieht die erweiterten Module KI, Energie und Berechtigungen und darf Ember immer nutzen. `users` und `groups` listen Konten und AD-Gruppen, mit oder ohne Domäne (`FIRMA\Entwicklung-Admins` oder `Entwicklung-Admins`). Ohne Einträge gibt es keine Admins. Admins blenden die Tabs mit <kbd>Strg</kbd> + <kbd>E</kbd> ein und aus. Ember prüft das auch auf der Host-Seite, eine Sicherheitsgrenze ist es aber nicht, siehe [KI-Modul](/Ember/handbuch/ki/).

### access

Wer Ember überhaupt nutzen darf. Wer nicht auf der Liste steht, sieht nur einen Sperrbildschirm mit „Zugriff anfragen“.

| Feld | Bedeutung |
|---|---|
| `users`, `groups` | Konten und AD-Gruppen wie bei `admins`. Admins dürfen immer, sie müssen hier nicht stehen. |
| `enabled` | `false`: Die Liste bleibt stehen, gilt aber nicht. Standard: an, sobald es einen Eintrag gibt. |
| `contact` | Mailadresse für Zugriffsanfragen. Standard: das Service-Center. |

Ohne `access` darf jeder Ember nutzen, der die `team.json` lesen kann. Gepflegt wird die Liste am bequemsten im Tab [Berechtigungen](/Ember/handbuch/berechtigungen/).

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

`models` ergänzt die Modellliste im KI-Tab (`url`, optional `id`, `name`, `sha256`, `note`).

### update.mirror

Ist GitHub im Firmennetz gesperrt, kann ein UNC-Ordner als Update-Quelle dienen. Kopiert man dorthin `latest.json`, `Ember.exe` und `ember-ui-<version>.zip` eines Releases, holt Ember Updates von dort statt von GitHub.

---
title: Team-Konfiguration
description: Eine team.json auf dem Netzlaufwerk für Proxy, Software, Mailvorlagen, Energieprofile und Freigaben.
---

Die Team-Konfiguration ist eine JSON-Datei, die alle Ember-Installationen eines Teams lesen. Sie liegt auf einem Netzlaufwerk, zum Beispiel `\\server\entwicklung\ember\team.json`. Darin steht alles, was für alle gleich ist und nicht ins öffentliche Repository gehört: Proxy, interne Domains, Softwareliste, Service-Center-Adresse, Mailvorlagen, Energieprofile und wer den KI-Tab sieht.

Pflegen sollte sie eine Person oder ein kleines Team mit Schreibrechten auf dem Ordner. Alle anderen brauchen nur Leserechte. Ember selbst schreibt nie in diese Datei.

## So findet Ember die Datei

In dieser Reihenfolge:

1. der Pfad unter **Einstellungen → Team-Konfiguration**,
2. eine Datei `ember-team.txt` neben `Ember.exe` (oder in `C:\Ember\App`), in der der Pfad steht,
3. die Umgebungsvariable `EMBER_TEAM_CONFIG`.

Zum Verteilen eignet sich Weg 2 am besten. Leg `Ember.exe` und `ember-team.txt` zusammen ab, und wer Ember von dort startet, ist sofort verbunden. Ember kopiert die Datei bei der Selbstinstallation mit.

Ember liest die Datei bei jedem Start mit deinem Windows-Login, wie jede andere Datei auf dem Laufwerk. Die letzte erfolgreich gelesene Fassung speichert es in `%APPDATA%\Ember\team.cache.json`. Ohne VPN arbeitet Ember mit dieser Kopie weiter und zeigt in den Einstellungen, von wann sie ist. Hängt das Laufwerk, wartet Ember höchstens zehn Sekunden.

## Beispiel

```json
{
  "$schema": "https://pixelshober.github.io/Ember/team.schema.json",
  "version": 3,
  "name": "Team Entwicklung",
  "proxy": {
    "pacUrl": "http://pac.firma.example/proxy.pac",
    "upstream": "proxy.firma.example:8080",
    "internalDomains": ["firma.local", "intranet.firma.example"]
  },
  "serviceCenter": { "email": "servicecenter@firma.example", "subjectPrefix": "[Ember]" },
  "software": [
    { "name": "Visual Studio Professional 2022", "sccmName": "Visual Studio Professional*", "category": "IDE" },
    { "name": "Alfaview", "sccmName": "alfaview*", "category": "Kommunikation" }
  ],
  "modules": { "ai": { "allowGroups": ["FIRMA\\KI-Entwicklung"] } }
}
```

Mit der `$schema`-Zeile schlägt VS Code beim Bearbeiten alle Felder vor und markiert Tippfehler. Das vollständige Beispiel liegt im Repository unter [`config/team.example.json`](https://github.com/PixelShober/Ember/blob/main/config/team.example.json).

Texte wie Notizen, Mailvorlagen und Profilnamen dürfen ein einfacher String sein oder pro Sprache angegeben werden: `{ "de": "…", "en": "…" }`.

## Die Abschnitte

### proxy

| Feld | Bedeutung |
|---|---|
| `usePac` | `true`: px nutzt die PAC-Datei (Standard), `false`: den festen Proxy |
| `pacUrl` | PAC-Adresse. Leer: Ember nimmt die aus den Windows-Einstellungen. |
| `upstream` | Firmenproxy als `host:port`. Ember nutzt ihn auch für Downloads, solange px noch nicht läuft. |
| `internalDomains` | Domains, die ohne Proxy erreichbar sind. Sie landen in `NO_PROXY`, npm `noproxy` und px `noproxy`. |
| `port` | Port von px, Standard 3128 |

### serviceCenter und mailTemplates

`serviceCenter.email` ist der Empfänger aller Mails, `subjectPrefix` wird jedem Betreff vorangestellt. Unter `mailTemplates` lassen sich die drei Vorlagen `softwareRequest`, `adminAction` und `problem` ersetzen, jeweils mit `subject` und `body`. Platzhalter: `{user}`, `{computer}`, `{app}`, `{details}`, `{date}`, `{ember}`.

### software

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

### modules.ai

`allowUsers` und `allowGroups` listen Konten und AD-Gruppen, die das KI-Modul sehen dürfen, mit oder ohne Domäne (`FIRMA\KI-Entwicklung` oder `KI-Entwicklung`). Ohne Einträge ist es für alle freigegeben.

### agentTools und models

`agentTools` ergänzt die Liste der npm-Pakete im KI-Tab (`package`, optional `name` und `description`). `models` ergänzt die Modellliste (`url`, optional `id`, `name`, `sha256`, `note`).

### update.mirror

Ist GitHub im Firmennetz gesperrt, kann ein UNC-Ordner als Update-Quelle dienen. Kopiert man dorthin `latest.json`, `Ember.exe` und `ember-ui-<version>.zip` eines Releases, holt Ember Updates von dort statt von GitHub.

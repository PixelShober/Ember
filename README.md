<p align="center"><img src="src/Ember.App/Assets/ember.svg" width="96" alt="Ember"></p>

<h1 align="center">Ember</h1>

<p align="center">Richtet Entwicklerrechner fürs Firmennetz ein: Proxy, Softwarecenter, Energie, KI-Werkzeuge.</p>

<p align="center">
  <a href="https://github.com/PixelShober/Ember/releases/latest">Download</a> ·
  <a href="https://pixelshober.github.io/Ember/">Handbuch</a> ·
  <a href="https://pixelshober.github.io/Ember/entwicklung/architektur/">Entwickler-Doku</a>
</p>

---

Ein neuer Dienstrechner kostet einen halben Tag, bis git, npm, NuGet und pip durch den Firmenproxy kommen. Ember erledigt das in ein paar Minuten, und zwar ohne dass irgendwo ein Passwort gespeichert wird. Dazu kommen die Dinge, die man sonst in fünf verschiedenen Windows-Dialogen zusammensucht.

![Ember – Startseite](docs/src/assets/screens/start-de-light.png)

## Was Ember kann

| Bereich | Was passiert |
|---|---|
| Proxy | Installiert [px](https://github.com/genotrance/px) und hält es aktuell. px meldet sich per Kerberos mit deinem Windows-Login am Firmenproxy an, deine Werkzeuge sprechen nur mit `127.0.0.1:3128`. Eine Tabelle zeigt für Umgebungsvariablen, git, npm, NuGet, pip und VS Code, wohin sie gerade zeigen. Ein Klick stellt sie auf px um. Dazu die Firmenzertifikate für Node.js (`NODE_EXTRA_CA_CERTS`), falls der Proxy TLS aufbricht. Den Firmenproxy erkennt Ember beim Start selbst. |
| Git | Schlägt Name und E-Mail aus dem Active Directory vor und setzt sie nach Bestätigung. |
| Software | Entwicklerwerkzeuge (Node.js LTS mit npm, Git, VS Code, Python, Visual Studio, .NET SDK, PowerShell 7): über das Softwarecenter, wenn die IT ein Paket hat, sonst direkt vom Hersteller mit geprüfter Prüfsumme und Signatur. Playwright-Browser passend zur Playwright-Version eines Projekts. Die Programme aus der Team-Liste kommen über das Softwarecenter, ohne Freigabe per vorbereiteter Mail ans Service-Center. |
| Service-Center | Mail-Entwurf in Outlook mit Diagnosebericht als Anhang. Abschicken tust du selbst. |
| Energie (Admins) | Profile für das Schema „Ausbalanciert“, etwa volle CPU-Leistung am Netzteil für lokale Modelle. Ist, Ziel und Werkseinstellung stehen nebeneinander, zurück auf Standard ist ein Klick. |
| System | Gruppenrichtlinien einsehen und auffrischen, PowerShell-Ausführungsrichtlinie, lange Pfade für git und Windows. |
| KI (Admins) | Diesen Rechner für Wake-on-LAN aus dem ausgeschalteten Zustand freischalten, llama.cpp in der passenden Variante (CUDA, Vulkan, CPU) installieren, GGUF-Modelle laden, `llama-server` starten. |

KI und Energie sind erweiterte Module: Sie erscheinen nur für Admins aus der Team-Konfiguration, <kbd>Strg</kbd> + <kbd>E</kbd> blendet sie ein und aus.

Gemeinsame Einstellungen fürs Team (Proxy, interne Domains, Softwareliste, Entwicklerwerkzeuge, Mailvorlagen, Energieprofile, wer Admin ist) stehen in einer `team.json` auf dem Netzlaufwerk. Ändert sich dort etwas, haben es alle beim nächsten Start. Ein Beispiel liegt unter [`config/team.example.json`](config/team.example.json).

Ember folgt der Windows-Sprache (Deutsch oder Englisch, Schweizer Deutsch ohne „ß“). Alles, was Ember speichert, liegt unter `C:\Ember`: Einstellungen, Logs, Berichte, Caches.

## Installation

1. [`Ember.exe` aus dem neuesten Release](https://github.com/PixelShober/Ember/releases/latest) herunterladen und starten. .NET bringt die Exe mit, Adminrechte braucht es nicht.
2. Ember kopiert sich nach `C:\Ember\App`, legt einen Startmenü-Eintrag an und startet von dort.
3. Unter Einstellungen den Pfad zur `team.json` eintragen, unter Proxy px installieren.

Danach aktualisiert sich Ember selbst. Neue Oberflächen kommen ohne Neustart, eine neue Exe braucht einen kurzen.

Mehr im [Handbuch](https://pixelshober.github.io/Ember/handbuch/installation/).

## Wie Ember gebaut ist

Eine WPF-Hülle (.NET 10) mit WebView2 zeigt eine Oberfläche aus Svelte 5 und TypeScript. Die Oberfläche spricht über eine JSON-Bridge mit der Exe, und nur die Exe fasst das System an. Die einzige externe Bibliothek zur Laufzeit ist `Microsoft.Web.WebView2`, die Logik in `Ember.Core` kommt ohne NuGet-Pakete aus.

```
src/Ember.App     WPF-Hülle mit WebView2
src/Ember.Core    Logik, Bridge, Module (ohne WPF, testbar unter Linux)
tests/            xUnit
ui/               Svelte-Oberfläche und Sprachdateien (de, en)
docs/             Handbuch und Entwickler-Doku (Starlight)
config/           Beispiel und JSON-Schema der Team-Konfiguration
scripts/          Prüfungen und Release-Helfer
```

Lokal bauen:

```bash
cd ui && npm ci && npm run build && cd ..
dotnet run --project src/Ember.App
```

Für die Oberfläche allein reicht `npm run dev` in `ui/`. Im Browser läuft sie mit Beispieldaten. Details unter [Bauen und starten](https://pixelshober.github.io/Ember/entwicklung/bauen/).

## Mitmachen und Releases

Änderungen laufen über Pull Requests. Der Titel folgt [Conventional Commits](https://www.conventionalcommits.org/de/) und bestimmt die nächste Version:

| PR-Titel | Version |
|---|---|
| `feat: …` | 1.2.0 → 1.3.0 |
| `fix: …` (und alles andere) | 1.2.0 → 1.2.1 |
| `feat!: …` | 1.2.0 → 2.0.0 |

Jeder Pull Request wird gebaut und getestet. Das fertige `Ember.exe` liegt als Artefakt am CI-Lauf, PR-Builds laufen immer portabel. Nach dem Merge entsteht das Release automatisch, mit Exe, UI-Paket, `latest.json` und Prüfsummen. Lokal baut niemand ein Release. Wie das genau abläuft, steht unter [Releases und Pipelines](https://pixelshober.github.io/Ember/entwicklung/releases/).

Texte stehen in `ui/src/locales/de.json` und `en.json`, für Oberfläche und Exe gemeinsam. Die CI prüft UTF-8, Umlaute und dass beide Sprachen dieselben Schlüssel haben.

Geplantes für Version 2 steht in [ROADMAP-V2.md](ROADMAP-V2.md).

## Datenschutz

Ember speichert keine Passwörter, sendet keine Telemetrie und verschickt keine Mails selbst. Es spricht nur mit GitHub (Updates, px, llama.cpp), mit den Download-Adressen deiner Modelle und mit npm, jeweils über den Firmenproxy. Was Ember wo ablegt, steht [im Handbuch](https://pixelshober.github.io/Ember/handbuch/datenschutz/).

## Lizenz

Ember steht unter der [MIT-Lizenz](LICENSE). Du darfst es nutzen, ändern und weitergeben, auch in der Firma, solange der Lizenzhinweis erhalten bleibt.

px ist ein eigenes Projekt unter MIT-Lizenz und wird zur Laufzeit von GitHub geladen, nicht mit Ember ausgeliefert. Dasselbe gilt für llama.cpp (MIT). Microsoft WebView2 unterliegt den Lizenzbedingungen von Microsoft.

### Grafiken

Logo und Hinweisfigur zeigen Calcifer aus „Das wandelnde Schloss“. Die Figur gehört Studio Ghibli, die Pixel-Grafiken stammen aus dem Cursor-Pack „Color Pixels Calcifer“ von [custom-cursor.com](https://custom-cursor.com). Sie fallen **nicht** unter die MIT-Lizenz. Das betrifft `src/Ember.App/Assets/ember.svg` und `ember.ico`, `ui/src/assets/calcifer-hint.png` sowie deren Kopien als Favicon und Doku-Logo. Wer Ember weitergibt oder darauf aufbaut, sollte sie durch eigene Grafiken ersetzen: `ember.svg` austauschen und `node scripts/make-icon.mjs` ausführen.

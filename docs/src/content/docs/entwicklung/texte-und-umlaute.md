---
title: Texte und Umlaute
description: Wie Ember dafür sorgt, dass ä, ö, ü und ß überall richtig ankommen.
---

Kaputte Umlaute entstehen fast nie im eigenen Code, sondern an den Übergängen: zwischen Prozessen, Dateien, Konsolen und Mailprogrammen. Ember behandelt jeden Übergang ausdrücklich.

## Eine Quelle für alle Texte

Alle Texte stehen in `ui/src/locales/de.json` und `en.json`. Die Oberfläche importiert sie, die Exe bettet dieselben Dateien als Ressource ein (`L.T("schlüssel", ("name", wert))` in C#, `t('schlüssel', { name })` in Svelte). Platzhalter sind `{name}`, Zahlen und Daten werden nach Sprache formatiert (1.830 auf Deutsch, 1,830 auf Englisch).

Für Schweizer Deutsch gibt es keine eigene Datei. Ember ersetzt „ß“ durch „ss“, sowohl in eigenen Texten als auch in solchen aus der Team-Konfiguration.

## Die Übergänge

| Übergang | Was Ember tut |
|---|---|
| Konsolenwerkzeuge (`powercfg`, `gpupdate`, `whoami`) | Ausgabe mit der OEM-Codepage des Rechners dekodieren (CP850 auf deutschem Windows, `GetOEMCP`) |
| PowerShell | Skript als `-EncodedCommand` (UTF-16), Ergebnisse als JSON über eine UTF-8-Datei. Das funktioniert auch im Constrained Language Mode. |
| git, npm | direkte Argumente (`ArgumentList`), nie Nicht-ASCII über `cmd.exe` |
| px im Debug-Modus | `PYTHONIOENCODING=utf-8` |
| Einstellungen, Team-Kopie | JSON in UTF-8, Umlaute unmaskiert (`"Büro"`, nicht `"Büro"`) |
| Logs, Diagnoseberichte | UTF-8 mit BOM, damit Editor und Outlook-Vorschau die Kodierung erkennen |
| `mailto:`-Links | Prozentkodierung in UTF-8 nach RFC 6068 |
| Outlook-Entwurf | .NET-Strings direkt per COM |
| WebView2 | `<meta charset="utf-8">`, Sprache der WebView = Windows-Sprache |
| `gpresult` | XML statt Textausgabe |

## Prüfungen

In der CI laufen:

- `scripts/check-encoding.mjs`: Jede Textdatei muss gültiges UTF-8 sein, ohne Mojibake (typische Zeichenfolgen, die entstehen, wenn UTF-8 als Windows-1252 gelesen wird) und ohne Ersatzzeichen. de und en müssen dieselben Schlüssel und Platzhalter haben. Jeder Schlüssel, den die Oberfläche benutzt, muss existieren. Umschriften wie „fuer“ oder „Groesse“ in `de.json` fallen auf.
- Vitest: dieselben Regeln für die Sprachdateien, dazu `t()` mit Zahlenformaten und Schweizer Schreibweise.
- xUnit: CP850-Dekodierung echter `powercfg`-Ausgabe, mailto-Kodierung von „Größe“ und „Grüße“, Namen wie „Jürgen Mößner“ durch die Bridge, und ein Test, der alle in C# benutzten Schlüssel und Fehlercodes gegen beide Sprachdateien prüft.

## Regeln beim Schreiben

- Echte Umlaute und „ß“, deutsche Anführungszeichen „so“, Halbgeviertstrich – für Einschübe.
- „du“ in der Oberfläche, wie im Rest von Ember.
- Kurze Texte, die sagen, was passiert: „px läuft · http://127.0.0.1:3128“ statt „Der Dienst wurde erfolgreich gestartet“.

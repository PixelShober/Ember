---
title: Einstellungen
description: Was sich einstellen lässt und welcher Wert gewinnt, wenn Team und du etwas anderes wollen.
---

![Einstellungen](../../../assets/screens/settings-de-light.png)

Die Einstellungen liegen in `C:\Ember\Config\settings.json`. Änderungen gelten erst nach „Speichern“. Solange etwas offen ist, steht unten eine Leiste mit „Ungespeicherte Änderungen“.

Die Seite selbst zeigt nur die Felder. Was sie bedeuten, steht hier. Dieses Handbuch öffnest du in Ember jederzeit mit <kbd>Strg</kbd> + <kbd>K</kbd>, und zwar auf der Seite zum gerade offenen Tab.

## Team-Konfiguration

Die gemeinsame, signierte Teamdatei auf dem Netzlaufwerk liefert Rollen, Service-Center-Adresse, Mailvorlagen und was das Team sonst vorgibt. Ändert sich dort etwas, übernimmt Ember es nach spätestens zwei Minuten. „Neu laden“ holt die Datei sofort. Den Firmenproxy erkennt Ember selbst, der gehört nicht in die Datei.

## Woher die Proxy-Werte kommen

Den Firmenproxy erkennt Ember bei jedem Start selbst: die PAC-Adresse aus Windows, den ersten Proxy aus der PAC-Datei oder den Systemproxy. Für PAC-Datei und Firmenproxy nimmt Ember:

1. deinen eigenen Wert, falls du einen eingetragen hast,
2. sonst das, was Ember beim Start erkannt hat,
3. sonst Embers Standard.

Interne Domains und px-Port kann die Team-Konfiguration vorgeben. Hier gilt: dein eigener Wert, sonst der aus der `team.json`, sonst der Standard. Bei den internen Domains kommt vor dem Standard noch die PAC-Datei: Ember nimmt die Ziele, die sie ohne Proxy (`DIRECT`) erreicht.

Trag nur dann selbst etwas ein, wenn die Erkennung danebenliegt.

Arbeitet Ember mit der PAC-Datei, ist das Feld für den festen Firmenproxy ausgegraut. px nimmt den Proxy dann aus der PAC-Datei. Umgekehrt ist bei „Fester Proxy“ das Feld für die PAC-Adresse aus. Der feste Proxy dient auch für Downloads, solange px noch nicht läuft. Leer heißt: Windows-Systemproxy.

Interne Domains trägst du durch Komma getrennt ein, zum Beispiel `firma.local, intranet.firma.de`. Ohne eigenen Eintrag und ohne `team.json` nimmt Ember, was die PAC-Datei ohne Proxy erreicht.

Neben jedem dieser Felder steht, woher der Wert gerade kommt: „eigener Wert“, „aus Team-Konfiguration“, „automatisch erkannt“, „aus PAC-Datei“ oder „Ember-Standard“. Bei eigenen Werten stellt der Knopf daneben den Team- oder den erkannten Wert wieder her.

Ändert die IT den Proxy, braucht es also nichts weiter: Beim nächsten Start erkennt Ember den neuen. Wer einen eigenen Wert hat, behält ihn.

## Die Bereiche

| Bereich | Inhalt |
|---|---|
| Team-Konfiguration | Pfad zur Teamdatei, Stand und Quelle, „Neu laden“ |
| Firmenproxy | PAC-Datei oder fester Proxy, PAC-Adresse (leer = aus Windows), Firmenproxy als `host:port`, interne Domains, px-Port, „Automatisch erkennen“ |
| px | Installationsordner, Autostart |
| Werkzeuge | welche Werkzeuge die px-Installation umstellt (Umgebungsvariablen, PowerShell, git, npm, NuGet, pip, VS Code) |
| Sprache | Deutsch oder English, vorausgewählt ist die Windows-Sprache |
| Updates | automatisch suchen, „Jetzt prüfen“, Stand und Quelle |
| Deine Module | deine Rollen aus der Teamdatei. Hast du Energie, KI oder Berechtigungen, blendest du sie mit <kbd>Strg</kbd> + <kbd>E</kbd> aus und ein. |
| Ordner | öffnet Logs, Einstellungen, px, px-Konfiguration und Berichte im Explorer |

Die Erkennung läuft bei jedem Start von selbst. „Automatisch erkennen“ macht dasselbe auf Knopfdruck und trägt das Ergebnis als eigenen Wert ein. Gespeichert wird auch das erst nach deiner Bestätigung.

Wählst du die Sprache, die auch Windows hat, folgt Ember weiter der Windows-Einstellung. Auf einem Schweizer Windows schreibt Ember dann „ss“ statt „ß“.

## Darstellung

Design und hell oder dunkel wählst du unten in der Seitenleiste, in den Einstellungen gibt es dafür keinen eigenen Bereich mehr. Ember hat zwei Designs. **Efeu** ist am Tag hell und salbeigrün, nachts waldgrün mit dunkelgrüner Seitenleiste. An der Seitenleiste rankt Efeu hoch, die Akzente sind glutrot. **Glut** ist wärmer: rosiger Grund am Tag, verkohltes Ochsenblut bei Nacht, Moosgrün für „erledigt“. Beide gibt es hell und dunkel. Beim allerersten Start übernimmt Ember, was Windows gerade nutzt, danach bleibt deine Wahl.

Beim Umschalten wächst das neue Aussehen als Kreis aus dem gedrückten Schalter. Wer unter Windows „Animationseffekte“ abgeschaltet hat, bekommt den Wechsel ohne Animation. Die Wahl merkt sich Ember in `settings.json`, auch die Titelleiste des Fensters passt sich an.

### Im Hintergrund

Fenstermanager wie GlazeWM, den WinMarchy einrichtet, lassen Fenster durchscheinen, solange sie nicht im Vordergrund sind. Sie tun das über die Deckkraft des ganzen Fensters. Den Inhalt von Ember zeichnet aber WebView2, und WebView2 übergeht diese Deckkraft. Ember liest sie deshalb selbst aus und zeichnet die Seite mit derselben Deckkraft. Einstellen musst du dafür nichts. Ohne Fenstermanager bleibt Ember deckend, und im Vordergrund ist es sofort wieder deckend.

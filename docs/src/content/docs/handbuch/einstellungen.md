---
title: Einstellungen
description: Was sich einstellen lässt und welcher Wert gewinnt, wenn Team und du etwas anderes wollen.
---

![Einstellungen](../../../assets/screens/settings-de-light.png)

Die Einstellungen liegen in `C:\Ember\Config\settings.json`. Änderungen gelten erst nach „Speichern“. Solange etwas offen ist, steht unten eine Leiste mit „Ungespeicherte Änderungen“.

## Woher die Proxy-Werte kommen

Den Firmenproxy erkennt Ember bei jedem Start selbst: die PAC-Adresse aus Windows, den ersten Proxy aus der PAC-Datei oder den Systemproxy. Für PAC-Datei und Firmenproxy nimmt Ember:

1. deinen eigenen Wert, falls du einen eingetragen hast,
2. sonst das, was Ember beim Start erkannt hat,
3. sonst Embers Standard.

Interne Domains und px-Port kann die Team-Konfiguration vorgeben. Hier gilt: dein eigener Wert, sonst der aus der `team.json`, sonst der Standard.

Neben jedem dieser Felder steht, woher der Wert gerade kommt: „eigener Wert“, „aus Team-Konfiguration“, „automatisch erkannt“ oder „Ember-Standard“. Bei eigenen Werten stellt der Knopf daneben den Team- oder den erkannten Wert wieder her.

Ändert die IT den Proxy, braucht es also nichts weiter: Beim nächsten Start erkennt Ember den neuen. Wer einen eigenen Wert hat, behält ihn.

## Die Bereiche

| Bereich | Inhalt |
|---|---|
| Team-Konfiguration | Pfad zur `team.json`, Stand und Quelle, „Neu laden“ |
| Firmenproxy | PAC-Datei oder fester Proxy, PAC-Adresse (leer = aus Windows), Firmenproxy als `host:port`, interne Domains, px-Port, „Automatisch erkennen“ |
| Darstellung | Design „Efeu“ oder „Glut“, hell, dunkel oder wie Windows. Dieselben Schalter stehen unten in der Seitenleiste. |
| px | Installationsordner, lokales px-Zip statt Download, Autostart |
| Werkzeuge | welche Werkzeuge die px-Installation umstellt |
| Sprache | Deutsch oder English, vorausgewählt ist die Windows-Sprache |
| Updates | automatisch suchen, „Jetzt prüfen“, Stand und Quelle |
| Erweiterte Module | ob du als Admin eingetragen bist; KI, Energie und Berechtigungen blendest du mit <kbd>Strg</kbd> + <kbd>E</kbd> ein und aus |
| Ordner | öffnet Logs, Einstellungen, px, px-Konfiguration und Berichte im Explorer |

Die Erkennung läuft bei jedem Start von selbst. „Automatisch erkennen“ macht dasselbe auf Knopfdruck und trägt das Ergebnis als eigenen Wert ein. Gespeichert wird auch das erst nach deiner Bestätigung.

Wählst du die Sprache, die auch Windows hat, folgt Ember weiter der Windows-Einstellung. Auf einem Schweizer Windows schreibt Ember dann „ss“ statt „ß“.

## Darstellung

Ember hat zwei Designs. **Efeu** ist am Tag hell und salbeigrün, nachts waldgrün mit dunkelgrüner Seitenleiste. An der Seitenleiste rankt Efeu hoch, die Akzente sind glutrot. **Glut** ist wärmer: rosiger Grund am Tag, verkohltes Ochsenblut bei Nacht, Moosgrün für „erledigt“. Beide gibt es hell und dunkel, „System“ folgt der Windows-Einstellung, auch während Ember läuft.

Beim Umschalten wächst das neue Aussehen als Kreis aus dem gedrückten Schalter. Wer unter Windows „Animationseffekte“ abgeschaltet hat, bekommt den Wechsel ohne Animation. Die Wahl merkt sich Ember in `settings.json`, auch die Titelleiste des Fensters passt sich an.

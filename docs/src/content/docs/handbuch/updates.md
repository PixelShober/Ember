---
title: Updates
description: Wie sich Ember selbst aktualisiert, meistens ohne Neustart.
---

Ember sucht beim Start (nach 15 Sekunden) und danach alle sechs Stunden nach Updates. Quelle ist `latest.json` im neuesten GitHub-Release, oder der Spiegel aus der Team-Konfiguration. Abschalten lässt sich das unter **Einstellungen → Updates**.

Es gibt zwei Arten von Updates.

## Neue Oberfläche, kein Neustart

Die Oberfläche steckt zwar in der Exe, jedes Release liefert sie aber zusätzlich als eigenes Paket `ember-ui-<version>.zip`. Ember lädt es im Hintergrund, prüft die SHA256-Prüfsumme und entpackt es nach `%LOCALAPPDATA%\Ember\ui\<version>`. Oben erscheint dann „Neue Oberfläche bereit“. „Jetzt neu laden“ dauert etwa eine Sekunde, Ember bleibt dabei offen, und px läuft einfach weiter.

Meldet sich die neue Oberfläche nicht innerhalb von 15 Sekunden als gestartet, nimmt Ember sie zurück, merkt sich die Version als fehlerhaft und zeigt wieder die eingebaute Oberfläche. Ein kaputtes Update kann Ember also nicht unbenutzbar machen.

## Neue Exe, kurzer Neustart

Ändert sich der Programmcode selbst, lädt Ember die neue `Ember.exe` neben die alte. Oben steht dann „Ember x.y.z ist bereit“. Bei „Neu starten“ benennt Ember die laufende Exe in `Ember.exe.old` um, setzt die neue an ihre Stelle und startet sie. Windows erlaubt das Umbenennen einer laufenden Exe, nur Löschen geht nicht. Die alte Datei entfernt die neue Instanz nach dem Start.

Während eine Aktion läuft, etwa eine px-Installation, wartet der Neustart. Exe-Updates gibt es nur im installierten Modus (`C:\Ember\App`). Im portablen Modus zeigt Ember nur an, dass eine neue Version da ist.

## Welche Art ein Release ist

Das entscheidet die Release-Pipeline: Hat sich seit dem letzten Release nur die Oberfläche geändert, steht in `latest.json` als `codeVersion` die alte Version. Installierte Exes holen dann nur das UI-Paket. Neue Downloads bekommen trotzdem die aktuelle Exe mit der neuesten Oberfläche.

Braucht eine neue Oberfläche Funktionen, die ältere Exes nicht haben, ist im UI-Paket eine höhere Bridge-Version eingetragen. Dann installiert Ember die Oberfläche nur zusammen mit der neuen Exe.

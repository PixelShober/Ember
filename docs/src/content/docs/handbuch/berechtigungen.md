---
title: Berechtigungen
description: Rollen legen fest, wer welche Module sieht. Ohne team.json schaltet ein Schlüssel Ember frei.
---

![Berechtigungen](../../../assets/screens/access-de-light.png)

Jeder Tab in Ember ist ein Modul: Proxy, Git, Software, Playwright, System, Energie, KI, Berechtigungen, Einstellungen und Log. Wer welches Modul sieht, legen Rollen in der [Team-Konfiguration](/Ember/handbuch/team-konfiguration/#access) fest. Damit gelten sie für alle und müssen nicht auf jedem Rechner einzeln gepflegt werden. Wer in keiner Rolle steht, sieht nichts außer einem Sperrbildschirm.

Gepflegt werden die Rollen im Tab **Berechtigungen**. Den sieht nur, wer eine Rolle mit dem Modul Berechtigungen hat. Energie, KI und Berechtigungen blendest du mit <kbd>Strg</kbd> + <kbd>E</kbd> aus und wieder ein, etwa um Ember so zu sehen wie die anderen.

## Rollen

Eine Rolle hat einen Namen, Module und Mitglieder. Wer in mehreren Rollen ist, sieht die Module aller seiner Rollen zusammen. Typisch sind zwei oder drei Rollen:

| Rolle | Module | Mitglieder |
|---|---|---|
| Admins | alle | die Leute, die Ember betreuen |
| Entwicklung | Proxy, Git, Software, Playwright, System, Einstellungen, Log | die AD-Gruppe des Teams |
| Test | Proxy, Playwright | die Testgruppe, einzelne Kolleginnen und Kollegen |

**Alle Module** heißt in der `team.json` `"*"`. Diese Rolle bekommt auch Module, die in späteren Versionen dazukommen.

Mitglieder fügst du per Name hinzu, mit oder ohne Domäne (`jmoessner` oder `FIRMA\jmoessner`). Ember schlägt den Namen im Verzeichnis nach, schreibt ihn in der richtigen Form und erkennt selbst, ob es ein Benutzer oder eine Gruppe ist. Gibt es den Namen nicht, kommt er nicht in die Rolle. Ohne Verbindung zum Verzeichnis (kein VPN) trägt Ember ihn ungeprüft mit der gewählten Art ein und sagt das dazu.

Am einfachsten sind AD-Gruppen, die es ohnehin gibt. Nimmt die IT jemanden in die Gruppe auf, sieht er beim nächsten Start die Module der Rolle, und in Ember ist nichts zu tun. Unter „Deine Anmeldung“ stehen deine eigenen Domänengruppen. Jede trägst du mit einem Klick in eine Rolle ein.

„Zugriffsanfragen gehen an“ legt fest, wohin die Mail vom Sperrbildschirm geht. Leer heißt: ans Service-Center.

## Speichern

„In team.json speichern“ schreibt den Abschnitt `access` zurück in die Datei auf dem Netzlaufwerk. Alles andere bleibt Zeichen für Zeichen stehen, auch Reihenfolge, Einrückung und Zeilenenden. Vorher legt Ember eine Kopie der alten Fassung unter `C:\Ember\Config\team-backups` ab (die letzten 20).

Ember speichert nicht, wenn

- jemand die `team.json` geändert hat, seit du die Seite geöffnet hast. Dann neu laden, prüfen, noch einmal speichern.
- du dich damit selbst aussperren würdest. Eine deiner Rollen muss das Modul Berechtigungen behalten.
- zwei Rollen gleich heißen oder eine keinen Namen hat.
- die Datei gerade nicht erreichbar ist (VPN, Netzlaufwerk) oder du sie nur lesen darfst. Die Seite sagt das oben, die Knöpfe sind dann aus.

Schreibrechte auf die `team.json` vergibt die IT auf dem Netzlaufwerk. Für alle anderen reichen Leserechte. Die anderen bekommen die neuen Rollen beim nächsten Start von Ember, oder sofort mit „Neu laden“ in den Einstellungen.

Steht in der `team.json` noch das alte Format mit `admins` und einer Zugriffsliste, zeigt der Tab beides als Rollen an: „Admins“ mit allen Modulen und „Ember“ mit allem außer Energie, KI und Berechtigungen. Beim Speichern schreibt Ember das neue Format.

## Was Nutzer ohne Rolle sehen

Wer in keiner Rolle steht, sieht statt der Oberfläche einen Hinweis mit seinem Anmeldenamen und zwei Knöpfen:

- **Zugriff anfragen** öffnet eine vorbereitete Mail (Vorlage `accessRequest`) an die eingetragene Adresse.
- **Erneut prüfen** liest die `team.json` neu. Steht der Name inzwischen in einer Rolle, startet die Oberfläche.

Mehr zeigt die Oberfläche nicht, und die Exe lehnt alle anderen Aufrufe ab. Das gilt auch für einzelne Module: Wer Playwright nicht hat, kann es auch nicht über die Bridge aufrufen. Im Hintergrund sucht Ember weiter nach Updates.

## Ohne team.json: die Schlüssel

Beim allerersten Start kennt Ember noch keine `team.json`, es sei denn, es liegt eine daneben oder eine Gruppenrichtlinie nennt sie (siehe [So findet Ember die Datei](/Ember/handbuch/team-konfiguration/#so-findet-ember-die-datei)). Dann erscheint **Ember einrichten** mit zwei Wegen:

- **Schlüssel**: Ein Schlüssel wie `EMBER-XXXXX-XXXXX-XXXXX-XXXXX` schaltet Ember frei, bis es eine `team.json` gibt. Es gibt drei: **Proxy** (nur der Proxy-Tab), **Proxy und Playwright** und **Vollzugriff** (alle Module). Bindestriche, Leerzeichen und Groß- und Kleinschreibung sind egal. Ember merkt sich den Schlüssel in den Einstellungen.
- **Team-Konfiguration**: den Pfad zur `team.json` eintragen oder auswählen. Ember liest die Datei einmal zur Probe, bevor es den Pfad übernimmt. Ab dann bestimmen ihre Rollen, was du siehst.

Wer mit einem Schlüssel arbeitet, sieht auf der Startseite die Karte „Team-Konfiguration“ mit dem Knopf „Team-Konfiguration einrichten“. Den Pfad kannst du also jederzeit nachtragen, auch ohne das Modul Einstellungen. Sobald eine `team.json` geladen ist, zählt der Schlüssel nicht mehr. Steht dann keine Rolle für dich drin, siehst du den Sperrbildschirm.

Die Schlüssel verteilt euer Team. Im Code steht nur ihr SHA-256-Hash. Neue Schlüssel erzeugt `node scripts/make-key.mjs <proxy|proxyPlaywright|all>`. Das Skript gibt den Schlüssel und die Zeile für `src/Ember.Core/Config/StarterKeys.cs` aus. Ein Schlüssel hat 100 zufällige Bits, der Hash lässt sich also nicht zurückrechnen.

:::note[Keine Sicherheitsgrenze]
Rollen und Schlüssel halten Ember aus den falschen Händen, schützen aber nichts. Ember läuft mit den Rechten des Benutzers und kann nichts, was der Benutzer nicht auch selbst könnte. Der Code ist öffentlich, und wer will, baut sich eine Exe ohne die Prüfung. Was wirklich geschützt werden muss, schützen die Rechte auf Netzlaufwerk, Softwarecenter und Proxy.
:::

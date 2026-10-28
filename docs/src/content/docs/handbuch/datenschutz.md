---
title: Was Ember speichert
description: Welche Daten Ember liest, wohin es sie schreibt und was es nie tut.
---

Kurz: Ember speichert keine Passwörter, schickt keine Telemetrie und sendet keine Mails selbst.

## Was Ember liest

- deine Windows-Proxy-Einstellungen und die PAC-Datei
- die Proxy-Einstellungen von git, npm, NuGet, pip und VS Code
- Name und E-Mail aus dem Active Directory (für den git-Vorschlag)
- die Liste installierter Programme und die Angebote des Softwarecenters
- Energieeinstellungen, Netzwerkadapter-Eigenschaften und Gruppenrichtlinien-Ergebnisse
- die Team-Konfiguration auf dem Netzlaufwerk

## Was Ember ins Netz schickt

- Anfragen an GitHub: px, llama.cpp und Ember-Updates
- Downloads der Modelle, die in der Liste stehen
- npm-Aufrufe für Agent-Werkzeuge
- Magic Packets, wenn du einen Rechner weckst

Das läuft über px oder den Firmenproxy und meldet sich mit deinem Windows-Login an. Ember hat keinen eigenen Server.

## Was Ember auf die Platte schreibt

Alles steht unter [Installation](/Ember/handbuch/installation/#wo-ember-seine-dateien-ablegt). Logs löscht Ember nach 30 Tagen. Diagnoseberichte bleiben liegen, bis du sie löschst.

## Was Ember ändert, und nur nach deinem Klick

- Proxy-Einträge der Werkzeuge (nur auf px setzen oder px-Einträge entfernen)
- git `user.name`, `user.email`, `core.longpaths`
- Werte im erlaubten Energieschema, Adapter-Einstellungen für Wake-on-LAN, Schnellstart
- die PowerShell-Ausführungsrichtlinie für dein Konto
- Dateien in `C:\Ember` und unter `%APPDATA%\Ember`, `%LOCALAPPDATA%\Ember`

Aktionen mit Adminrechten startet Ember einzeln, und Windows fragt jedes Mal nach. Ember selbst läuft immer mit Benutzerrechten.

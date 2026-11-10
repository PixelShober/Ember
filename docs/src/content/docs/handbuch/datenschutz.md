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
- die Namen deiner Windows- und AD-Gruppen (für die Rollen)

## Was Ember ins Netz schickt

- Anfragen an GitHub: px, llama.cpp und Ember-Updates
- Downloads der Modelle, die in der Liste stehen
- Versionsabfragen und Installer der Entwicklerwerkzeuge bei den Herstellern (nodejs.org, GitHub, Microsoft, python.org, nuget.org)
- npm- und Playwright-Downloads, wenn du Playwright-Browser installierst

Das läuft über px oder den Firmenproxy und meldet sich mit deinem Windows-Login an. Ember hat keinen eigenen Server.

## Was Ember auf die Platte schreibt

Alles steht unter [Installation](/Ember/handbuch/installation/#wo-ember-seine-dateien-ablegt). Logs löscht Ember nach 30 Tagen. Diagnoseberichte bleiben liegen, bis du sie löschst.

## Was Ember ändert, und nur nach deinem Klick

- Proxy-Einträge der Werkzeuge (nur auf px setzen oder px-Einträge entfernen)
- git `user.name`, `user.email`, `core.longpaths`
- Werte im erlaubten Energieschema, Adapter-Einstellungen für Wake-on-LAN, Schnellstart
- die PowerShell-Ausführungsrichtlinie für dein Konto
- Dateien in `C:\Ember`
- die Teamdatei `team.ember`, wenn du im Tab Berechtigungen signierst und speicherst (vorher eine Kopie nach `C:\Ember\Config\team-backups`)
- deinen Signaturschlüssel als passwortgeschützte Datei in dem Ordner, den du dafür wählst. Das Passwort speichert Ember nicht.
- `C:\Ember\App\ember-team.txt` mit dem Pfad zur Teamdatei, wenn du Ember vom Netzlaufwerk startest
- in `settings.json` die höchste gesehene Version der Teamdatei, damit eine ältere Kopie nicht mehr zählt
- einen markierten Block in `Dokumente\WindowsPowerShell\profile.ps1` und `Dokumente\PowerShell\profile.ps1`, wenn du PowerShell auf px setzt
- git `http.<url>.allowNTLMAuth` für die internen Domains und `http.sslBackend`, wenn es noch leer ist
- `%USERPROFILE%\.pi\agent\models.json` (Provider `llama-cpp`), wenn du Pi mit dem llama-server verbindest
- in Playwright-Projekten: `package.json`, `playwright.config`, Beispieltest und `.gitignore`, wenn du Playwright hinzufügst, Edge einrichtest oder ein neues Projekt anlegst
- den Benutzer-PATH (Node.js, Git, Python) und die Variable `NODE_EXTRA_CA_CERTS`, wenn du die Node.js-Zertifikate einrichtest
- `C:\Ember\Tools\bruno` und den Startmenü-Eintrag „Bruno“, wenn du Bruno installierst

Aktionen mit Adminrechten startet Ember einzeln, und Windows fragt jedes Mal nach. Ember selbst läuft immer mit Benutzerrechten.

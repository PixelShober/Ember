---
title: Software
description: Programme aus dem Softwarecenter installieren, mit den Freigaben der IT.
---

![Software-Seite](../../../assets/screens/software-de-light.png)

Ember installiert keine Software an der IT vorbei. Die Seite zeigt die Programme aus der Team-Konfiguration (Abschnitt `software`) und verbindet sie mit dem Softwarecenter des Configuration-Manager-Clients.

## Was Ember pro Programm anzeigt

- Ob es installiert ist und in welcher Version. Das liest Ember aus „Programme und Features“ (die Uninstall-Einträge der Registry).
- Ob das Softwarecenter es anbietet, als Paket oder als Anwendung, und in welchem Zustand.
- Knöpfe für „Installieren“, „Im Softwarecenter öffnen“ und „Beim Service-Center anfragen“.

## Pakete und Anwendungen

Configuration Manager kennt zwei Arten der Verteilung:

- **Pakete** (Paket-ID plus Programm) sind bei vielen Firmen der Normalfall. „Installieren“ bittet den Client per `CCM_ProgramsManager.ExecuteProgram`, das Programm auszuführen. Das ist dasselbe wie der Knopf im Softwarecenter, mit denselben Rechten und Wartungsfenstern. Ember verfolgt danach den Status und meldet „installiert“, „Neustart nötig“ oder einen Fehler.
- **Anwendungen** öffnet Ember per Direktlink `softwarecenter:SoftwareID=…` im Softwarecenter.

Welche Programme auf deinem Rechner bereitstehen, fragt Ember beim Client ab (`root\ccm\ClientSDK`). Das Team muss dafür keine IDs pflegen, der Name in `team.json` reicht. Platzhalter wie `alfaview*` sind erlaubt.

## Wenn der Direktlink fehlt

Für Pakete gibt es kein verlässliches Link-Format. „Im Softwarecenter öffnen“ zeigt dann die Startseite „Verfügbare Software“ und nennt den Paketnamen. Wer einen festen Link möchte: Im Softwarecenter die App öffnen, über „Teilen“ den Link kopieren (falls die Version das anbietet) und in `team.json` als `softwareCenterLink` eintragen.

## Ohne Softwarecenter oder ohne Freigabe

„Beim Service-Center anfragen“ öffnet die vorbereitete Mail „Software anfordern“ mit Programmname, Benutzer und Rechner. Mehr dazu unter [Service-Center](/Ember/handbuch/service-center/).

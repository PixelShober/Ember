---
title: Service-Center und Diagnosebericht
description: Mails ans Service-Center vorbereiten, mit Diagnosebericht als Anhang.
---

Manches darf nur die IT: Software freigeben, Adminrechte für eine Einstellung, Hilfe bei einem Problem. Ember schreibt die Mail dafür vor. Abschicken tust du sie selbst.

## So entsteht die Mail

Im Dialog wählst du das Anliegen (Software anfordern, Adminrechte für eine Aktion, Problem melden), ergänzt ein paar Sätze und entscheidest, ob der Diagnosebericht mitgeht. Empfänger, Betreff und Text kommen aus der Team-Konfiguration oder aus Embers Standardvorlagen.

Danach versucht Ember zuerst das klassische Outlook. Klappt das, öffnet sich ein fertiger Entwurf, der Bericht hängt als Datei an.

Mit dem neuen Outlook oder einem anderen Mailprogramm nimmt Ember einen `mailto:`-Link. Der kann keine Anhänge mitgeben. Deshalb liegt der Bericht zusätzlich in der Zwischenablage und als Datei bereit, und der Explorer zeigt sie dir. Umlaute im Betreff und Text kommen korrekt an, weil der Link UTF-8-kodiert ist.

Scheitert eine Aktion an fehlenden Adminrechten (etwa weil du die Windows-Abfrage abgebrochen hast), bietet Ember diese Mail direkt im Hinweis an, mit den genauen Befehlen im Text.

## Was im Diagnosebericht steht

- Ember- und Windows-Version, Rechnername, Benutzer, Sprache, ob Ember mit Adminrechten läuft
- Pfad und Stand der Team-Konfiguration
- px: installiert, läuft, Port, PAC-Datei, Firmenproxy
- die Proxy-Tabelle aller Werkzeuge
- Versionen von git, Node und .NET
- die letzten 150 Log-Einträge ab Stufe „Info“

Passwörter stehen nicht drin, Ember kennt keine. Der Bericht ist eine Textdatei (UTF-8 mit BOM, damit auch ältere Programme die Umlaute richtig zeigen) unter `%LOCALAPPDATA%\Ember\reports`.

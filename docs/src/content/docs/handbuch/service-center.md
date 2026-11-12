---
title: Service-Center und Diagnosebericht
description: Mails ans Service-Center und an die Vorgesetzten vorbereiten. Der Diagnosebericht bleibt bei dir.
---

Manches darf nur die IT: Software freigeben, Adminrechte vergeben, bei einem Problem helfen. Ember schreibt die Mail dafür vor. Abschicken tust du sie selbst.

## So entsteht die Mail

Im Dialog wählst du das Anliegen und ergänzt, worum es geht. Empfänger, Betreff und Text kommen aus der Team-Konfiguration oder aus Embers Standardvorlagen. Unten siehst du die Mail so, wie sie rausgeht.

| Anliegen | geht an | Text |
|---|---|---|
| Software anfordern | Service-Center | „Bitte stellen Sie mir die Software … über das Software Center bereit.“ Das Feld „Welche Software?“ füllt die Lücke. |
| Problem melden | Service-Center | Ein einleitender Satz, danach deine Beschreibung. Den Satz kann die Team-Konfiguration ersetzen (`serviceCenter.problemIntro`). |
| Adminrechte beantragen | deine direkte Vorgesetzte bzw. dein direkter Vorgesetzter | „Hallo …, kannst du bitte einmal für mich Admin-Rechte für meinen Rechner beim Service-Center beantragen?“ Adminrechte beantragt bei euch die Führungskraft, deshalb geht die Mail dorthin. |

Mails ans Service-Center beginnen mit „Sehr geehrte Kolleginnen und Kollegen“. Jede Mail endet mit „Mit freundlichen Grüßen“ und deinem Vor- und Nachnamen. Den Namen holt Ember aus dem Verzeichnis, ohne Verbindung dorthin aus `git config user.name`.

Fehlt in der Teamdatei die Adresse, bietet Ember das Anliegen gar nicht erst an. Ohne `serviceCenter.email` gibt es auf der Startseite also keinen Knopf „Service-Center kontaktieren“, ohne `supervisor.email` keinen Antrag auf Adminrechte.

Danach versucht Ember zuerst das klassische Outlook und öffnet dort einen Entwurf. Mit dem neuen Outlook oder einem anderen Mailprogramm nimmt Ember einen `mailto:`-Link. Umlaute im Betreff und Text kommen an, weil der Link UTF-8-kodiert ist.

Scheitert eine Aktion an fehlenden Adminrechten (etwa weil du die Windows-Abfrage abgebrochen hast), bietet der Hinweis „Adminrechte beantragen“ an. Die genauen Befehle stehen dann schon im Feld „Wofür?“.

## Logs gehen nie mit

Ember hängt weder seine Logs noch den Diagnosebericht an eine Mail. Beide enthalten interne Adressen, Rechnernamen und MAC-Adressen, die in einem Ticket nichts verloren haben. Braucht das Service-Center den Bericht, entscheidest du selbst, ob und wem du ihn schickst.

## Der Diagnosebericht

„Diagnosebericht erstellen“ auf der Startseite speichert den Bericht, kopiert ihn in die Zwischenablage und öffnet den Ordner im Explorer, die Datei ist schon markiert. Drin steht:

- Ember- und Windows-Version, Rechnername, Benutzer, Sprache, ob Ember mit Adminrechten läuft
- Pfad und Stand der Team-Konfiguration
- px: installiert, läuft, Port, PAC-Datei, Firmenproxy
- die Proxy-Tabelle aller Werkzeuge
- Versionen von git, Node und .NET
- die letzten 150 Log-Einträge ab Stufe „Info“

Passwörter stehen nicht drin, Ember kennt keine. Der Bericht ist eine Textdatei (UTF-8 mit BOM, damit auch ältere Programme die Umlaute richtig zeigen) unter `C:\Ember\Reports`.

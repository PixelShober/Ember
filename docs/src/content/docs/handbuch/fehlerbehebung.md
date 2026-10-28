---
title: Fehlerbehebung
description: Die häufigsten Probleme und was dagegen hilft.
---

Erste Anlaufstelle ist immer der Log-Tab. Mit „Debug“ siehst du jeden Befehl, den Ember ausführt, samt Ausgabe. Hilft das nicht weiter: Diagnosebericht erstellen und ans Service-Center schicken.

Die Logdatei liegt unter `%APPDATA%\Ember\logs\ember-JJJJ-MM-TT.log`, eine pro Tag, 30 Tage lang. Sie enthält immer alles, auch die Debug-Zeilen: jeden Befehl, jedes PowerShell-Skript, Exitcodes und die Antworten von Windows. Wenn du einen Fehler meldest, häng diese Datei an. Der Diagnosebericht hat nur die letzten Info-Zeilen.

## px-Installation bricht bei „Verbindungstest über px“ ab

Meist kommt HTTP 407 zurück: Der Firmenproxy hat die Kerberos-Anmeldung von px abgelehnt. Starte „px-Debug“ unter Proxy und schau im Log nach, was px versucht. Häufige Ursachen sind eine falsche PAC-Adresse, ein Proxy, der nur NTLM kann, oder kein Kontakt zum Domänencontroller (VPN aus).

## „Der Proxy ist per TCP nicht erreichbar“

Host oder Port stimmen nicht, oder die Firewall lässt Ember nicht ins Netz. Probier „Automatisch erkennen“ in den Einstellungen. Steht der Proxy in der PAC-Datei unter einem anderen Namen, nimm den.

## Team-Konfiguration „aus der lokalen Kopie“

Das Netzlaufwerk war beim Start nicht erreichbar, meist wegen VPN. Ember arbeitet mit der letzten Fassung weiter. Mit VPN reicht „Neu laden“ in den Einstellungen.

## Softwarecenter zeigt keine Programme

Ember fragt den Configuration-Manager-Client ab. Liefert er nichts, ist entweder kein Client installiert, oder deine IT verteilt über einen anderen Weg. Die Programme aus der Team-Liste erscheinen trotzdem, mit „Im Softwarecenter öffnen“ und „Beim Service-Center anfragen“.

## Energiewerte lassen sich nicht ändern

Steht „per Richtlinie festgelegt“ daneben, kommt der Wert aus einer Gruppenrichtlinie, und Ember lässt ihn in Ruhe. Andernfalls fehlen Adminrechte: Windows fragt nach. Hast du abgebrochen oder keine Rechte, bietet Ember die Mail ans Service-Center mit den genauen Befehlen an.

## Wake-on-LAN weckt den Rechner nicht

Auf dem Zielrechner unter Energie prüfen, ob die WOL-Karte „bereit zum Wecken“ zeigt. Dann das BIOS kontrollieren (siehe [Energie](/Ember/handbuch/energie/#wake-on-lan-für-diesen-rechner)). Hängt der Rechner am Dock, muss die MAC-Adresse des Dock-Adapters eingetragen sein, nicht die des eingebauten Anschlusses. Über Subnetze hinweg kommen Broadcasts oft nicht an. Dann im Gerät die Broadcast-Adresse des Zielnetzes eintragen.

## Umlaute sehen kaputt aus

Das sollte nicht passieren, und wenn doch, ist es ein Fehler in Ember. Bitte ein Issue auf GitHub mit einem Screenshot und dem Diagnosebericht anlegen. Ember liest Konsolenausgaben mit der OEM-Codepage, PowerShell-Ergebnisse als UTF-8, und schreibt Berichte als UTF-8 mit BOM.

## Die Oberfläche bleibt weiß

Ember braucht die WebView2-Runtime. Fehlt sie und kann Ember sie nicht installieren, erscheint eine Meldung mit Link zur Microsoft-Seite. Bleibt das Fenster nach einem Oberflächen-Update leer, nimmt Ember nach 15 Sekunden automatisch die eingebaute Version.

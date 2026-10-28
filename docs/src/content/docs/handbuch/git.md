---
title: Git-Identität
description: Name und E-Mail für Commits aus dem Windows-Konto übernehmen.
---

![Git-Identität](../../../assets/screens/git-de-light.png)

Ember liest, was in `git config --global` für `user.name` und `user.email` steht, und macht einen Vorschlag aus deinem Windows-Konto:

- Die E-Mail kommt aus dem Active-Directory-Attribut `mail`. Ohne AD-Verbindung (kein VPN, kein Domänenrechner) nimmt Ember den Anmeldenamen (UPN), sofern er wie eine Adresse aussieht.
- Den Namen setzt Ember aus Vor- und Nachname im AD zusammen. Viele deutsche Verzeichnisse speichern den Anzeigenamen als „Nachname, Vorname (Abteilung)“, git bekommt daraus „Vorname Nachname“.

Gespeichert wird erst, wenn du auf „In git speichern“ klickst und die Rückfrage bestätigst. Umlaute im Namen kommen dabei unverändert in git an, Ember ruft git direkt auf und nicht über `cmd.exe`.

Ist git nicht installiert, verweist die Seite aufs Softwarecenter.

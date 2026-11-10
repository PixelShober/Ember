---
title: Git
description: Name und E-Mail für Commits, dazu Proxy und Ausnahmen für interne Server.
---

![Git](../../../assets/screens/git-de-light.png)

## Identität

Ember liest, was in `git config --global` für `user.name` und `user.email` steht, und macht einen Vorschlag aus deinem Windows-Konto:

- Die E-Mail kommt aus dem Active Directory. Ohne AD-Verbindung (kein VPN, kein Domänenrechner) nimmt Ember den Anmeldenamen (UPN), sofern er wie eine Adresse aussieht.
- Den Namen setzt Ember aus Vor- und Nachname im AD zusammen. Viele deutsche Verzeichnisse speichern den Anzeigenamen als „Nachname, Vorname (Abteilung)“, git bekommt daraus „Vorname Nachname“.

Steht in git schon genau der Vorschlag, blendet Ember ihn aus und zeigt „Vorschlag schon übernommen“. Gespeichert wird erst, wenn du auf „In git speichern“ klickst und die Rückfrage bestätigst. Umlaute im Namen kommen dabei unverändert in git an, Ember ruft git direkt auf und nicht über `cmd.exe`.

Ist git nicht installiert, verweist die Seite aufs Softwarecenter.

## Proxy und interne Server

Externe Repositories wie GitHub erreicht git über px. Interne Git-Server (GitLab, Bitbucket, Azure DevOps Server) sind über den Firmenproxy oft gar nicht erreichbar, git muss sie direkt ansprechen. Die Karte „Proxy für git“ zeigt beides: wohin `http.proxy` zeigt und für welche internen Domains git den Proxy auslässt.

„Auf px setzen“ trägt `http.proxy` ein und für jede interne Domain eine Ausnahme mit Windows-Anmeldung:

```ini
[http]
    proxy = http://127.0.0.1:3128
    sslBackend = schannel
[http "https://firma.local"]
    proxy =
    allowNTLMAuth = true
[http "https://*.firma.local"]
    proxy =
    allowNTLMAuth = true
```

Dasselbe gibt es für `http://`. Ein leerer Proxy heißt für git: direkt verbinden. `allowNTLMAuth` braucht es für interne Server mit Windows-Anmeldung, etwa Azure DevOps Server. Git for Windows bietet NTLM und Negotiate sonst nicht mehr von sich aus an, die Anmeldung scheitert dann trotz richtiger Daten. Ist `http.sslBackend` noch leer, stellt Ember es auf `schannel`. git prüft Zertifikate dann gegen den Windows-Zertifikatsspeicher, in dem die Firmen-CAs schon liegen. `sslVerify = false` ist damit meist überflüssig.

Ein paar Dinge braucht git dagegen nicht: Einen Abschnitt `[https]` liest git gar nicht, `http.proxy` gilt für beide Protokolle. `http.noProxy` gibt es als Einstellung nicht, die Ausnahmen kommen aus den URL-Abschnitten und aus `NO_PROXY`. Das klappt auch, wenn px gerade nicht läuft. Der Stern deckt bei git nur eine Ebene ab (`git.firma.local`, nicht `gitlab.dev.firma.local`). Für tiefere Subdomains sorgt `NO_PROXY` in den Umgebungsvariablen, das git ebenfalls auswertet. Fehlt dort etwas, weist die Karte darauf hin.

Die Liste der Domains ohne Proxy ist anfangs eingeklappt und zeigt nur die ersten drei. Die internen Domains kommen aus den Einstellungen, aus der Team-Konfiguration (`proxy.internalDomains`) oder aus der PAC-Datei. „Entfernen“ unter Proxy nimmt `http.proxy` und Embers Ausnahmen wieder heraus.

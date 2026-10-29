---
title: Bridge-API
description: Das JSON-Protokoll zwischen Oberfläche und Exe und alle Methoden.
---

Oberfläche und Exe reden über `window.chrome.webview.postMessage` miteinander. Es gibt drei Nachrichtenarten:

```jsonc
// Anfrage (UI → Exe)
{ "id": 7, "method": "proxy.set", "params": { "tool": "npm" } }
// Antwort (Exe → UI)
{ "id": 7, "result": "npm: proxy, https-proxy, noproxy" }
{ "id": 7, "error": { "code": "toolMissing", "message": "npm wurde nicht gefunden.", "params": { "tool": "npm" } } }
// Ereignis (Exe → UI)
{ "event": "px.status", "data": { "installed": true, "running": true, "portOpen": true, … } }
```

`message` ist schon übersetzt. Mit `code` und `params` kann die Oberfläche auf bestimmte Fehler reagieren. Bei `elevationCancelled` bietet sie zum Beispiel die Mail ans Service-Center an. Alle Namen sind camelCase, Enums kommen als Strings.

Auf der Oberflächenseite kapselt `ui/src/lib/bridge.ts` das Protokoll (`call`, `on`), `app.svelte.ts` stellt `api()` und `run()` mit Fehler-Toasts bereit. Die Datenformen stehen in `ui/src/lib/types.ts`.

## Methoden

| Bereich | Methoden |
|---|---|
| App | `app.info`, `app.ready`, `app.setAppearance`, `app.toggleAdvanced`, `app.openExternal`, `app.openFolder`, `app.pickFolder`, `app.pickFile` |
| Einstellungen | `settings.get`, `settings.save`, `settings.defaults`, `team.status`, `team.reload` |
| Proxy | `proxy.detect`, `proxy.diagnose`, `proxy.set`, `proxy.remove` |
| Berechtigungen | `access.get`, `access.lookup`, `access.save` |
| px | `px.status`, `px.install`, `px.uninstall`, `px.start`, `px.stop`, `px.restart`, `px.debug` |
| Jobs | `jobs.list`, `jobs.cancel`, `jobs.retry`, `jobs.dismiss` |
| Git | `git.get`, `git.set`, `git.proxy` |
| Software & Mail | `software.list`, `software.open`, `software.install`, `devtools.latest`, `devtools.install`, `devtools.installMany`, `playwright.state`, `playwright.inspect`, `playwright.install`, `playwright.create`, `playwright.useEdge`, `playwright.openInCode`, `mail.preview`, `mail.compose`, `diagnostics.report` |
| Energie | `power.state`, `power.apply`, `power.defaults`, `power.restore` |
| Wake-on-LAN (lokal) | `wol.local`, `wol.apply`, `wol.reset` |
| System | `system.list`, `system.check`, `system.checks`, `system.action`, `system.adminCommands` |
| KI | `ai.status`, `ai.llama.install`, `ai.llama.select`, `ai.server.start`, `ai.server.stop`, `ai.models.download`, `ai.models.delete` |
| Updates | `update.status`, `update.check`, `update.applyUi`, `update.applyExe` |
| Log | `log.recent`, `log.clear`, `log.save` |

Die Methoden für KI, Energie, Wake-on-LAN und Berechtigungen prüfen auch auf der Exe-Seite, ob das Konto Admin in der Team-Konfiguration ist (`admins`). `app.toggleAdvanced` (<kbd>Strg</kbd> + <kbd>E</kbd>) blendet die Tabs nur ein und aus.

Steht ein Konto nicht auf der Zugriffsliste (`access`), lässt `Bridge.Guard` nur noch `app.info`, `app.ready`, `app.setAppearance`, `app.openExternal`, `team.reload`, `mail.preview` und `mail.compose` durch. Alles andere antwortet mit dem Fehler `accessDenied`. `app.info.access` sagt der Oberfläche, ob sie den Sperrbildschirm zeigen soll.

## Ereignisse

| Ereignis | Inhalt |
|---|---|
| `log` | Liste neuer Log-Einträge, gebündelt alle 150 ms |
| `job` | Zustand eines Jobs mit allen Schritten, höchstens etwa acht Mal pro Sekunde |
| `px.status` | px-Status, wenn er sich ändert (geprüft alle 4 s) |
| `update` | Update-Status (geprüft, bereit, Fehler) |
| `app.info` | nach Änderungen an Sprache, Team-Konfiguration oder erweiterten Modulen |
| `settings.detected` | die Proxy-Erkennung beim Start ist fertig |

## Jobs

Alles, was länger dauert, ist ein Job: px installieren, Software und Entwicklerwerkzeuge installieren, Playwright-Browser laden, Modelle laden. Ein Job ist eine Liste von `SetupStep`s mit Titel, Status, Detailtext, Fortschritt und Dauer. Die Methode startet den Job und antwortet sofort mit dem ersten Zustand, danach kommen `job`-Ereignisse. `jobs.retry` setzt am fehlgeschlagenen Schritt wieder an.

## Bridge-Version

`AppInfo.BridgeVersion` in Core und `requiresBridge` in `ui/public/ui-manifest.json` gehören zusammen. Wer der Oberfläche eine neue Exe-Methode beibringt, erhöht beide. Ältere Exes installieren das neue UI-Paket dann nicht live, sondern erst mit dem passenden Exe-Update. `scripts/check-deps.mjs` prüft, dass die UI nie mehr verlangt, als die Exe im selben Stand bietet.

---
title: Neues Modul anlegen
description: Schritt für Schritt von der C#-Logik bis zur Seite in der Oberfläche.
---

Als Beispiel ein Modul, das die Windows-Zeitzone anzeigt und setzt. Vier Stellen sind zu ändern.

## 1. Logik in Ember.Core

Neue Datei unter `src/Ember.Core/Modules/`. Systemaufrufe gehen über `ProcessRunner` oder `PowerShell`, nie direkt über `Process.Start`. Dann stimmen Kodierung, Timeout und Logging.

```csharp
namespace Ember.Core;

public sealed record TimeZoneInfoDto(string Id, string Name);

public static class TimeZones
{
    public static async Task<TimeZoneInfoDto> GetAsync(CancellationToken ct)
    {
        var r = await ProcessRunner.RunAsync(ProcessRunner.SystemExe("tzutil.exe"), new[] { "/g" }, ct, 10, OutputKind.Oem);
        var id = r.LastLine;
        return new TimeZoneInfoDto(id, TimeZoneInfo.FindSystemTimeZoneById(id).DisplayName);
    }
}
```

Konsolenwerkzeuge (`tzutil`, `powercfg`, `whoami`) liefern ihre Ausgabe in der OEM-Codepage, deshalb `OutputKind.Oem`. git, npm und PowerShell liefern UTF-8. Mehr dazu unter [Texte und Umlaute](/Ember/entwicklung/texte-und-umlaute/).

Fehler wirfst du als `EmberException` mit einem Code. Die Meldung steht dann unter `errors.<code>` in den Sprachdateien.

## 2. Methoden registrieren

In `src/Ember.Core/Bridge/EmberHost.cs` in `RegisterApi()`:

```csharp
b.Register("time.get", async ct => await TimeZones.GetAsync(ct));
```

Dauert etwas länger als ein, zwei Sekunden, mach einen Job daraus: Liste von `SetupStep`s bauen und mit `Jobs.Start(id, kind, title, steps)` starten. Fortschritt, Abbrechen und Wiederholen gibt es dann umsonst, ebenso die Anzeige über `<JobView id="…" />`.

## 3. Texte

Jeder sichtbare Text kommt in beide Dateien `ui/src/locales/de.json` und `en.json`, mit denselben Schlüsseln und Platzhaltern. Die Exe liest dieselben Dateien, die Texte gibt es also nur einmal. `scripts/check-encoding.mjs` und ein .NET-Test schlagen fehl, wenn ein Schlüssel fehlt.

## 4. Seite in der Oberfläche

Neue Datei `ui/src/pages/Time.svelte`:

```svelte
<script lang="ts">
  import { onMount } from 'svelte';
  import { api, run } from '../lib/app.svelte';
  import { t } from '../lib/i18n.svelte';

  let tz = $state<{ id: string; name: string } | null>(null);
  onMount(async () => (tz = (await run(() => api('time.get'))) ?? null));
</script>

<header class="page-head"><h1>{t('time.title')}</h1></header>
<section class="card">{tz?.name ?? t('common.loading')}</section>
```

Dann die Route in `ui/src/lib/types.ts` (`Route`), in `app.svelte.ts` (`routes`) und in `App.svelte` (Navigation und `{#if}`-Kette) ergänzen. Für die Browser-Vorschau bekommt `ui/src/lib/mock.ts` einen Eintrag `'time.get'`.

Zum Schluss erhöhst du `AppInfo.BridgeVersion` und `requiresBridge` in `ui/public/ui-manifest.json`, weil die neue Oberfläche eine Methode braucht, die alte Exes nicht kennen.

<script lang="ts">
  import { onMount } from 'svelte';
  import { api, app, run, toast } from '../lib/app.svelte';
  import { host, t } from '../lib/i18n.svelte';
  import { formatDate } from '../lib/format';
  import type { ProxyDetection, SettingsView, UpdateStatus, UserSettings } from '../lib/types';
  import Icon from '../lib/components/Icon.svelte';
  import Calcifer from '../lib/components/Calcifer.svelte';
  import SourceBadge from '../lib/components/SourceBadge.svelte';

  let view = $state<SettingsView | null>(null);
  let s = $state<UserSettings | null>(null);
  let saved = $state('');

  async function load() {
    view = (await run(() => api<SettingsView>('settings.get'))) ?? view;
    s = view ? structuredClone($state.snapshot(view.user)) : null;
    saved = JSON.stringify(s);
  }
  onMount(load);

  const dirty = $derived(s != null && JSON.stringify(s) !== saved);
  const team = $derived(view?.team);

  // Effective value shown in a field: own value, else team value, else default.
  const usePac = $derived(s?.usePac ?? team?.usePac ?? true);

  function setTeamManaged<K extends 'pacUrl' | 'upstreamProxy' | 'internalDomains'>(key: K, value: string) {
    if (!s) return;
    const teamValue = team?.[key] ?? '';
    s[key] = value === teamValue ? null : value;
  }

  async function save() {
    if (!s) return;
    const r = await run(() => api<SettingsView>('settings.save', { settings: $state.snapshot(s) }), t('settings.saved', { path: view?.paths.settings ?? '' }));
    if (r) {
      view = r;
      s = structuredClone($state.snapshot(r.user));
      saved = JSON.stringify(s);
    }
  }

  async function reloadTeam() {
    const r = await run(() => api<SettingsView>('team.reload'));
    if (r) {
      view = r;
      toast(r.teamStatus.error ? 'error' : 'ok', r.teamStatus.error ? t('settings.team.failed', { error: r.teamStatus.error }) : t('settings.team.status', { path: r.teamStatus.path ?? '', date: formatDate(r.teamStatus.loadedAt) }));
    }
  }

  async function detect() {
    const r = await run(() => api<ProxyDetection>('proxy.detect'));
    if (!r || !s) return;
    const found: string[] = [];
    if (r.pacUrl) { s.pacUrl = r.pacUrl; found.push('PAC'); }
    const proxy = r.pacProxies[0] ?? r.systemProxy;
    if (proxy) { s.upstreamProxy = proxy; found.push(proxy); }
    toast(found.length ? 'ok' : 'info', found.length ? t('settings.proxy.detected', { items: found.join(', ') }) : t('settings.proxy.notDetected'));
  }

  async function pickFolder() {
    const r = await api<string | null>('app.pickFolder', { title: t('settings.px.installDir'), initial: s?.installDir });
    if (r && s) s.installDir = r;
  }

  async function pickZip() {
    const r = await api<string | null>('app.pickFile', { title: t('settings.px.localZip'), filter: 'px (*.zip)|*.zip' });
    if (r && s) s.localZip = r;
  }

  async function checkUpdates() {
    const r = await run(() => api<UpdateStatus>('update.check'));
    if (r) {
      app.update = r;
      if (!r.uiAvailable && !r.exeAvailable && !r.error) toast('ok', t('settings.updates.upToDate'));
    }
  }

  const folders = ['logs', 'settings', 'px', 'pxConfig', 'reports'] as const;
  const languages = ['auto', 'de', 'de-CH', 'en'] as const;
  const toolKeys = [['configureEnvVars', 'envVars'], ['configureGit', 'git'], ['configureNpm', 'npm'], ['configureNuGet', 'nuget'], ['configurePip', 'pip']] as const;
</script>

<header class="page-head">
  <h1>{t('settings.title')}</h1>
  {#if view}<p class="break">{t('settings.subtitle', { path: view.paths.settings })}</p>{/if}
</header>

{#if s && view}
  <div class="stack">
    <!-- Team configuration -->
    <section class="card stack tight">
      <h2>{t('settings.team.title')}</h2>
      <p class="muted">{t('settings.team.description')}</p>
      <label class="field">
        <span>{t('settings.team.path')}</span>
        <div class="row nowrap-row">
          <input bind:value={s.teamConfigPath} placeholder={t('settings.team.placeholder')} spellcheck="false" class="mono" />
          <button class="btn" onclick={reloadTeam}><Icon name="refresh" size={14} />{t('settings.team.reload')}</button>
        </div>
      </label>
      {#if view.teamStatus.path}
        <p class="small">
          {#if view.teamStatus.fromCache}
            <span class="badge warn">{t('settings.team.fromCache', { error: view.teamStatus.error ?? '', date: formatDate(view.teamStatus.loadedAt) })}</span>
          {:else if view.teamStatus.error}
            <span class="badge error">{t('settings.team.failed', { error: view.teamStatus.error })}</span>
          {:else}
            <span class="badge ok">{t('settings.team.status', { path: view.teamStatus.path, date: formatDate(view.teamStatus.loadedAt) })}</span>
          {/if}
          {#if view.teamStatus.source}<span class="muted"> · {t(`settings.team.source.${view.teamStatus.source}`)}</span>{/if}
        </p>
      {:else}
        <div class="note"><Calcifer /><p>{t('settings.team.none')}</p></div>
      {/if}
    </section>

    <!-- Company proxy -->
    <section class="card stack tight">
      <div class="row between">
        <h2>{t('settings.proxy.title')}</h2>
        <button class="btn small" onclick={detect}><Icon name="proxy" size={14} />{t('settings.proxy.detect')}</button>
      </div>
      <div class="row">
        <span class="label">{t('settings.proxy.mode')}</span>
        <label class="check"><input type="radio" name="mode" checked={usePac} onchange={() => (s!.usePac = team?.usePac === true ? null : true)} />{t('settings.proxy.pac')}</label>
        <label class="check"><input type="radio" name="mode" checked={!usePac} onchange={() => (s!.usePac = team?.usePac === false ? null : false)} />{t('settings.proxy.fixed')}</label>
        <SourceBadge user={s.usePac} team={team?.usePac} onreset={() => (s!.usePac = null)} />
      </div>

      <label class="field">
        <span class="row">{t('settings.proxy.pacUrl')} <SourceBadge user={s.pacUrl} team={team?.pacUrl} onreset={() => (s!.pacUrl = null)} /></span>
        <input value={s.pacUrl ?? team?.pacUrl ?? ''} oninput={(e) => setTeamManaged('pacUrl', e.currentTarget.value)} spellcheck="false" class="mono"
          placeholder={t('settings.proxy.pacAuto', { value: view.effective.detectedPac ?? '–' })} />
      </label>
      <label class="field">
        <span class="row">{t('settings.proxy.upstream')} <SourceBadge user={s.upstreamProxy} team={team?.upstreamProxy} onreset={() => (s!.upstreamProxy = null)} /></span>
        <input value={s.upstreamProxy ?? team?.upstreamProxy ?? ''} oninput={(e) => setTeamManaged('upstreamProxy', e.currentTarget.value)} spellcheck="false" class="mono" placeholder="proxy.firma.de:8080" />
        <span class="hint">{t('settings.proxy.upstreamHint')}</span>
      </label>
      <label class="field">
        <span class="row">{t('settings.proxy.domains')} <SourceBadge user={s.internalDomains} team={team?.internalDomains} onreset={() => (s!.internalDomains = null)} /></span>
        <input value={s.internalDomains ?? team?.internalDomains ?? ''} oninput={(e) => setTeamManaged('internalDomains', e.currentTarget.value)} spellcheck="false" />
        <span class="hint">{t('settings.proxy.domainsHint')}</span>
      </label>
      <label class="field small-field">
        <span class="row">{t('settings.proxy.port')} <SourceBadge user={s.port} team={team?.port} onreset={() => (s!.port = null)} /></span>
        <input type="number" min="1" max="65535" value={s.port ?? team?.port ?? 3128}
          oninput={(e) => { const v = Number(e.currentTarget.value); s!.port = v === (team?.port ?? 3128) ? null : v; }} />
      </label>
    </section>

    <!-- px -->
    <section class="card stack tight">
      <h2>{t('settings.px.title')}</h2>
      <label class="field">
        <span>{t('settings.px.installDir')}</span>
        <div class="row nowrap-row"><input bind:value={s.installDir} spellcheck="false" class="mono" /><button class="btn" onclick={pickFolder}>{t('common.browse')}</button></div>
      </label>
      <label class="field">
        <span>{t('settings.px.localZip')}</span>
        <div class="row nowrap-row">
          <input bind:value={s.localZip} spellcheck="false" class="mono" />
          <button class="btn" onclick={pickZip}>{t('common.browse')}</button>
          {#if s.localZip}<button class="btn ghost" onclick={() => (s!.localZip = '')}><Icon name="x" size={14} /></button>{/if}
        </div>
      </label>
      <label class="check"><input type="checkbox" bind:checked={s.autostart} />{t('settings.px.autostart')}</label>
    </section>

    <!-- tools -->
    <section class="card stack tight">
      <h2>{t('settings.tools.title')}</h2>
      {#each toolKeys as [key, label] (key)}
        <label class="check"><input type="checkbox" bind:checked={s[key]} />{t(`settings.tools.${label}`)}</label>
      {/each}
    </section>

    <!-- language -->
    <section class="card stack tight">
      <h2>{t('settings.language.title')}</h2>
      <div class="row">
        {#each languages as l (l)}
          <label class="check lang"><input type="radio" name="lang" value={l} bind:group={s.language} />
            {l === 'auto' ? t('settings.language.auto', { value: navigator.language }) : t(`settings.language.${l}`)}</label>
        {/each}
      </div>
    </section>

    <!-- updates -->
    <section class="card stack tight">
      <div class="row between">
        <h2>{t('settings.updates.title')}</h2>
        <button class="btn small" disabled={app.update?.checking} onclick={checkUpdates}><Icon name="refresh" size={14} />{app.update?.checking ? t('settings.updates.checking') : t('settings.updates.check')}</button>
      </div>
      <p>{t('settings.updates.current', { version: app.info?.version, ui: app.info?.uiVersion })}</p>
      {#if app.update}
        <p class="small muted">
          {#if app.update.latest}{t('settings.updates.latest', { version: app.update.latest })} · {/if}
          {t('settings.updates.lastCheck', { date: formatDate(app.update.lastCheck) })} · {t('settings.updates.source', { source: app.update.source })}
        </p>
        {#if app.update.error}<p class="small error">{host(app.update.error)}</p>{/if}
      {/if}
      <label class="check"><input type="checkbox" bind:checked={s.autoUpdate} />{t('settings.updates.auto')}</label>
    </section>

    <!-- advanced -->
    <section class="card stack tight">
      <h2>{t('settings.advanced.title')}</h2>
      <label class="check"><input type="checkbox" bind:checked={s.advanced} disabled={!app.info?.aiAllowed} />{t('settings.advanced.toggle')}</label>
      <p class="hint">{app.info?.aiAllowed ? t('settings.advanced.shortcut') : t('app.advancedNotAllowed')}</p>
    </section>

    <!-- folders -->
    <section class="card stack tight">
      <h2>{t('settings.folders.title')}</h2>
      <div class="row">
        {#each folders as f (f)}
          <button class="btn small" onclick={() => run(() => api('app.openFolder', { key: f }))}><Icon name="folder" size={14} />{t(`settings.folders.${f}`)}</button>
        {/each}
      </div>
    </section>
  </div>

  {#if dirty}
    <div class="savebar">
      <span>{t('settings.unsaved')}</span>
      <span class="spacer"></span>
      <button class="btn ghost" onclick={load}>{t('common.cancel')}</button>
      <button class="btn primary" onclick={save}><Icon name="check" size={16} />{t('common.save')}</button>
    </div>
  {/if}
{:else}
  <p class="muted">{t('common.loading')}</p>
{/if}

<style>
  .nowrap-row { flex-wrap: nowrap; }
  .label { font-weight: 500; margin-right: 6px; }
  .small-field { max-width: 220px; }
  .lang { margin-right: 14px; }
  .error { color: var(--error); }
  .savebar {
    position: sticky; bottom: 0; margin-top: 16px; display: flex; align-items: center; gap: 10px;
    padding: 10px 14px; border-radius: var(--radius); background: var(--surface);
    border: 1px solid var(--border); box-shadow: 0 -4px 20px rgb(0 0 0 / 0.08);
  }
</style>

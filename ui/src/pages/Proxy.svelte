<script lang="ts">
  import { onMount } from 'svelte';
  import { api, app, confirm, run } from '../lib/app.svelte';
  import { host, t } from '../lib/i18n.svelte';
  import type { ProxyRow } from '../lib/types';
  import Icon from '../lib/components/Icon.svelte';
  import Calcifer from '../lib/components/Calcifer.svelte';
  import JobView from '../lib/components/JobView.svelte';

  let rows = $state<ProxyRow[] | null>(null);
  let loading = $state(false);

  async function load() {
    loading = true;
    rows = (await run(() => api<ProxyRow[]>('proxy.diagnose'))) ?? rows;
    loading = false;
  }
  onMount(load);

  // After an installation, uninstallation or start/stop of px the tools have changed: read them again.
  let lastPxJob = app.jobs['px']?.state;
  $effect(() => {
    const state = app.jobs['px']?.state;
    if (state !== lastPxJob && (state === 'done' || state === 'failed')) void load();
    lastPxJob = state;
  });

  const px = $derived(app.px);
  // Tools only point to px once it is installed and answers; otherwise they would lose the network.
  const pxReady = $derived(!!px?.installed && !!px?.portOpen);
  const busy = $derived(app.jobs['px']?.state === 'running');
  const statusText = $derived(
    !px ? t('common.loading')
    : px.portOpen ? (px.debug ? t('proxy.px.debugRunning', { url: px.url }) : t('proxy.px.running', { url: px.url }))
    : px.running ? t('proxy.px.portClosed')
    : px.installed ? t('proxy.px.stopped')
    : t('proxy.px.notInstalled'),
  );
  const tone = $derived(px?.portOpen ? 'ok' : px?.installed ? 'warn' : '');
  const stateTone: Record<string, string> = { px: 'ok', other: 'warn', unset: '', missing: '', info: 'info' };

  async function uninstall() {
    if (await confirm(t('proxy.px.uninstall'), t('proxy.px.confirmUninstall'), t('proxy.px.uninstall'), true))
      await run(() => api('px.uninstall'));
  }

  async function setTool(row: ProxyRow) {
    const ok = await confirm(row.label, t('proxy.diag.confirmSet', { tool: row.label, url: px?.url ?? '' }), t('proxy.diag.set'));
    if (!ok) return;
    if (await run(() => api<string>('proxy.set', { tool: row.tool }), (r) => r)) await load();
  }

  async function removeTool(row: ProxyRow) {
    if (await run(() => api<string>('proxy.remove', { tool: row.tool }), (r) => r)) await load();
  }
</script>

<header class="page-head">
  <h1>{t('proxy.title')}</h1>
  <p>{t('proxy.subtitle')}</p>
</header>

<div class="stack">
  <section class="card">
    <div class="card-head">
      <h2>{t('proxy.px.title')}</h2>
      <span class="badge {tone}"><span class="dot"></span>{statusText}</span>
    </div>

    {#if px?.configOutdated}
      <div class="note warn"><Calcifer /><p>{t('proxy.px.outdated')}</p></div>
    {/if}

    <div class="row actions">
      <button class="btn primary" disabled={busy} onclick={() => run(() => api('px.install'))}><Icon name="download" size={16} />{t('proxy.px.install')}</button>
      <button class="btn" disabled={busy || !px?.installed || px?.portOpen} onclick={() => run(() => api('px.start'))}><Icon name="play" size={16} />{t('proxy.px.start')}</button>
      <button class="btn" disabled={busy || !px?.running} onclick={() => run(() => api('px.stop'))}><Icon name="stop" size={16} />{t('proxy.px.stop')}</button>
      <button class="btn" disabled={busy || !px?.installed} onclick={() => run(() => api('px.restart'))}><Icon name="refresh" size={16} />{t('proxy.px.restart')}</button>
      <button class="btn" disabled={busy || !px?.installed} title={t('proxy.px.debugHint')} onclick={() => run(() => api('px.debug'))}>
        <Icon name="bug" size={16} />{px?.debug ? t('proxy.px.debugStop') : t('proxy.px.debugStart')}
      </button>
      <span class="spacer"></span>
      <button class="btn danger" disabled={busy || !px?.installed} onclick={uninstall}><Icon name="trash" size={16} />{t('proxy.px.uninstall')}</button>
    </div>

    <JobView id="px" />
  </section>

  <section class="card">
    <div class="card-head">
      <div>
        <h2>{t('proxy.diag.title')}</h2>
        <p class="small muted">{t('proxy.diag.subtitle')}</p>
      </div>
      <span class="spacer"></span>
      <button class="btn small" disabled={loading} onclick={load}><Icon name="refresh" size={14} />{t('common.refresh')}</button>
    </div>
    <div class="note kerberos"><Icon name="info" /><p class="small">{t('proxy.diag.kerberosNote')}</p></div>

    {#if rows}
      <table class="table">
        <thead><tr><th>{t('proxy.diag.tool')}</th><th>{t('proxy.diag.value')}</th><th>{t('proxy.diag.state')}</th><th></th></tr></thead>
        <tbody>
          {#each rows as row (row.tool)}
            <tr>
              <td class="nowrap"><strong>{host(row.label)}</strong></td>
              <td>
                <code class="break">{row.value}</code>
                {#if row.pac}
                  <dl class="pac">
                    {#if row.pac.proxies.length}
                      <dt>{t('proxy.pac.proxies')}</dt>
                      <dd>{#each row.pac.proxies as p (p)}<span class="badge">{p}</span>{/each}</dd>
                    {/if}
                    {#if row.pac.direct.length && row.pac.source !== 'pac'}
                      <dt>{t('proxy.pac.direct')}</dt>
                      <dd>{#each row.pac.direct as d (d)}<span class="badge">{d}</span>{/each}</dd>
                    {/if}
                    <dt>{t('proxy.pac.exceptions')}</dt>
                    <dd>
                      {#each row.pac.exceptions as d (d)}<span class="badge info">{d}</span>{:else}<span class="small muted">{t('proxy.pac.none')}</span>{/each}
                      <span class="small muted">· {t(`proxy.pac.sources.${row.pac.source}`)}</span>
                    </dd>
                  </dl>
                {/if}
                {#if row.hint}<p class="hint">{host(row.hint)}</p>{/if}
              </td>
              <td><span class="badge {stateTone[row.state]}">{row.tool === 'nodeCerts' ? t(`proxy.nodeCerts.states.${row.state}`) : t(`proxy.states.${row.state}`)}</span></td>
              <td class="nowrap right">
                {#if row.canSet}
                  {@const blocked = row.tool !== 'nodeCerts' && !pxReady}
                  <button class="btn small" disabled={blocked} title={blocked ? t('proxy.diag.needsPx') : undefined} onclick={() => setTool(row)}>{row.tool !== 'nodeCerts' ? t('proxy.diag.set') : row.state === 'px' ? t('proxy.nodeCerts.renew') : t('proxy.nodeCerts.set')}</button>
                {/if}
                {#if row.canRemove}<button class="btn small ghost" onclick={() => removeTool(row)}>{t('proxy.diag.remove')}</button>{/if}
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    {:else}
      <p class="muted">{t('common.loading')}</p>
    {/if}
  </section>
</div>

<style>
  .pac { display: grid; grid-template-columns: max-content 1fr; gap: 6px 12px; margin: 8px 0 4px; padding: 8px 10px; border-radius: var(--radius-small); background: var(--surface-2); }
  .pac dt { color: var(--text-muted); font-size: 12.5px; }
  .pac dd { margin: 0; display: flex; flex-wrap: wrap; gap: 4px; align-items: center; }
  .kerberos { margin-bottom: 12px; }
  .actions { margin-bottom: 8px; }
  .right { text-align: right; }
  .note { margin-top: 14px; }
</style>

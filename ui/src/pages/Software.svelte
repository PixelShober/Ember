<script lang="ts">
  import { onMount } from 'svelte';
  import { api, app, confirm, openMail, run } from '../lib/app.svelte';
  import { t } from '../lib/i18n.svelte';
  import { isNewer } from '../lib/format';
  import type { DevToolState, SoftwareItem, SoftwareList } from '../lib/types';
  import Icon from '../lib/components/Icon.svelte';
  import Calcifer from '../lib/components/Calcifer.svelte';
  import JobView from '../lib/components/JobView.svelte';
  import PlaywrightCard from '../lib/components/PlaywrightCard.svelte';

  let list = $state<SoftwareList | null>(null);
  let loading = $state(false);
  let latest = $state<Record<string, string>>({});
  let filter = $state('');

  async function load() {
    loading = true;
    list = (await run(() => api<SoftwareList>('software.list'))) ?? list;
    loading = false;
    // The vendors' current versions arrive one by one (through px, cached for an hour by the host).
    for (const d of list?.devTools ?? []) {
      if (d.method !== 'direct' || latest[d.id]) continue;
      api<string>('devtools.latest', { id: d.id }).then((v) => (latest[d.id] = v)).catch(() => {});
    }
  }
  onMount(load);

  // Reload when an installation finishes.
  $effect(() => {
    const finished = Object.values(app.jobs).filter((j) => (j.kind === 'devtool' || j.kind === 'software') && j.state === 'done').length;
    if (finished) void load();
  });

  const missing = $derived((list?.devTools ?? []).filter((d) => !d.installedVersion && d.method !== 'mail'));
  const busyTools = $derived(Object.values(app.jobs).some((j) => j.kind === 'devtool' && j.state === 'running'));

  function request(d: DevToolState) {
    return d.sccm?.packageId && d.sccm.programId ? { id: d.id, packageId: d.sccm.packageId, programId: d.sccm.programId } : { id: d.id };
  }

  async function installTool(d: DevToolState) {
    if (!(await confirm(d.name, t('devtools.confirmInstall', { name: d.name }), t('devtools.install')))) return;
    await run(() => api('devtools.install', request(d)));
  }

  async function installMissing() {
    const names = missing.map((d) => d.name).join(', ');
    if (!(await confirm(t('devtools.installMissing'), t('devtools.confirmMany', { names }), t('devtools.installMissing')))) return;
    await run(() => api('devtools.installMany', { tools: missing.map(request) }));
  }

  const stateKey = (s: number | null): string =>
    s == null || s <= 1 ? 'available' : s <= 4 ? 'preparing' : s === 5 ? 'downloading' : s === 6 || s === 14 ? 'waiting'
    : s === 7 || s === 11 ? 'installing' : s >= 8 && s <= 10 ? 'reboot' : s === 12 ? 'installed' : s === 13 ? 'error' : 'unknown';

  async function install(item: SoftwareItem) {
    if (!item.sccm?.packageId || !item.sccm.programId) return;
    if (!(await confirm(item.name, t('software.confirmInstall', { name: item.name }), t('software.install')))) return;
    await run(() => api('software.install', { key: item.key, name: item.name, packageId: item.sccm!.packageId, programId: item.sccm!.programId }));
  }

  // team.json categories first, then everything else Software Center offers.
  const groups = $derived.by(() => {
    const q = filter.trim().toLowerCase();
    const map = new Map<string, SoftwareItem[]>();
    for (const item of list?.items ?? []) {
      if (q && !item.name.toLowerCase().includes(q) && !(item.note ?? '').toLowerCase().includes(q)) continue;
      const key = item.source === 'sccm' ? '\u0000sccm' : item.category ?? '';
      if (!map.has(key)) map.set(key, []);
      map.get(key)!.push(item);
    }
    return [...map.entries()].sort(([a], [b]) => (a === '\u0000sccm' ? 1 : b === '\u0000sccm' ? -1 : 0));
  });

  const initial = (name: string) => name.replace(/[^\p{L}\p{N}]/gu, '').slice(0, 1).toUpperCase() || '?';
</script>

<header class="page-head">
  <h1>{t('software.title')}</h1>
  <p>{t('software.subtitle')}</p>
</header>

{#if list}
  <div class="row head">
    {#if list.clientPresent}
      <span class="badge ok"><Icon name="check" size={13} />{t('software.client', { version: list.clientVersion })}</span>
    {:else}
      <div class="note warn"><Calcifer /><p>{t('software.noClient')}</p></div>
    {/if}
    <span class="spacer"></span>
    <button class="btn small" disabled={loading} onclick={load}><Icon name="refresh" size={14} />{t('common.refresh')}</button>
  </div>

  <!-- Developer tools -->
  <section class="section">
    <div class="section-head">
      <div class="grow"><h2>{t('devtools.title')}</h2><p class="small muted">{t('devtools.subtitle')}</p></div>
      {#if missing.length}
        <Calcifer size={30} />
        <button class="btn primary small" disabled={busyTools} onclick={installMissing}><Icon name="download" size={14} />{t('devtools.installMissing')} ({missing.length})</button>
      {:else}
        <span class="badge ok"><Icon name="check" size={13} />{t('devtools.allInstalled')}</span>
      {/if}
    </div>
    <JobView id="devtools" />
    <div class="tiles">
      {#each list.devTools as d (d.id)}
        {@const update = isNewer(latest[d.id], d.installedVersion)}
        <article class="card tile" class:done={!!d.installedVersion && !update}>
          <div class="tile-head">
            <span class="mono-icon">{initial(d.name)}</span>
            <div class="title"><h3>{d.name}</h3>
              {#if d.installedVersion}<span class="badge ok">{t('devtools.installedVersion', { version: d.installedVersion })}</span>
              {:else}<span class="badge">{t('devtools.notInstalled')}</span>{/if}
            </div>
          </div>
          <p class="small muted desc">{d.description}</p>
          <div class="row meta">
            {#if update}<span class="badge accent">{t('devtools.updateAvailable', { version: latest[d.id] })}</span>
            {:else if latest[d.id] && !d.installedVersion}<span class="small muted">{t('devtools.latest', { version: latest[d.id] })}</span>{/if}
            <span class="badge {d.method === 'sccm' ? 'info' : ''}">{t(d.method === 'sccm' ? 'devtools.viaSccm' : d.method === 'direct' ? 'devtools.direct' : 'devtools.mailOnly')}</span>
            {#if d.needsAdmin}<span class="badge" title={t('devtools.needsAdmin')}><Icon name="shield" size={12} />{t('common.admin')}</span>{/if}
          </div>
          <div class="row actions">
            {#if d.method !== 'mail' && (!d.installedVersion || update)}
              <button class="btn small {d.installedVersion ? '' : 'primary'}" disabled={busyTools} onclick={() => installTool(d)}>
                <Icon name="download" size={14} />{d.installedVersion ? t('devtools.update') : t('devtools.install')}
              </button>
            {/if}
            {#if d.method === 'mail'}
              <button class="btn small" onclick={() => openMail('softwareRequest', { app: d.name })}><Icon name="mail" size={14} />{t('software.request')}</button>
            {/if}
          </div>
          <div class="job"><JobView id={'devtool:' + d.id} compact /></div>
        </article>
      {/each}
    </div>
  </section>

  <div class="section"><PlaywrightCard /></div>

  <!-- Software Center -->
  <section class="section">
    <div class="section-head">
      <div class="grow"><h2>{t('software.center')}</h2><p class="small muted">{t('software.centerHint')}</p></div>
      {#if list.items.length > 8}
        <input class="filter" type="search" bind:value={filter} placeholder={t('software.filter')} aria-label={t('software.filter')} />
      {/if}
    </div>

    {#if list.items.length === 0}
      <div class="note"><Calcifer /><p>{list.clientPresent ? t('software.nothingOffered') : t('software.empty')}</p></div>
    {/if}

    {#each groups as [category, items] (category)}
      {#if category}<h3 class="category">{category === '\u0000sccm' ? t('software.more') : category}</h3>{/if}
      <div class="tiles">
        {#each items as item (item.key)}
          <article class="card tile" class:done={!!item.installed}>
            <div class="tile-head">
              <span class="mono-icon">{initial(item.name)}</span>
              <div class="title"><h3>{item.name}</h3>
                {#if item.installed}<span class="badge ok">{t('software.installed', { version: item.installed.version ?? '?' })}</span>
                {:else}<span class="badge">{t('software.notInstalled')}</span>{/if}
              </div>
            </div>
            {#if item.note}<p class="small muted desc">{item.note}</p>{/if}
            <div class="row meta">
              {#if item.sccm}
                <span class="badge info">{t(item.sccm.kind === 'package' ? 'software.package' : 'software.application')} · {t(`software.states.${stateKey(item.sccm.evaluationState)}`)}</span>
              {:else}
                <span class="badge warn">{t('software.notInCenter')}</span>
              {/if}
            </div>
            <div class="row actions">
              {#if item.canInstall && !item.installed}
                <button class="btn primary small" disabled={app.jobs['software:' + item.key]?.state === 'running'} onclick={() => install(item)}><Icon name="download" size={14} />{t('software.install')}</button>
              {/if}
              <span class="spacer"></span>
              <button class="btn small ghost icon-only" title={t('software.open')} aria-label={t('software.open')} onclick={() => run(() => api('software.open', { link: item.link }))}><Icon name="external" size={15} /></button>
              <button class="btn small ghost icon-only" title={t('software.request')} aria-label={t('software.request')} onclick={() => openMail('softwareRequest', { app: item.name })}><Icon name="mail" size={15} /></button>
            </div>
            <div class="job"><JobView id={'software:' + item.key} compact /></div>
          </article>
        {/each}
      </div>
    {/each}
  </section>
{:else}
  <p class="muted">{t('common.loading')}</p>
{/if}

<style>
  .head { margin-bottom: 16px; }
  .section { margin-bottom: 26px; }
  .section-head { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 12px; }
  .grow { flex: 1; min-width: 260px; display: flex; flex-direction: column; gap: 3px; }
  .filter { width: 240px; }
  .category { font-size: 12.5px; text-transform: uppercase; letter-spacing: 0.06em; color: var(--text-muted); margin: 14px 0 8px; }
  .tiles { display: grid; gap: 12px; grid-template-columns: repeat(auto-fill, minmax(250px, 1fr)); }
  .tile { display: flex; flex-direction: column; gap: 8px; padding: 14px 16px; border-top: 3px solid var(--border-strong); }
  .tile.done { border-top-color: var(--ok); }
  .tile-head { display: flex; gap: 10px; align-items: flex-start; }
  .mono-icon {
    flex: none; width: 34px; height: 34px; border-radius: 9px; display: grid; place-items: center;
    font-family: var(--font-display); font-weight: 700; font-size: 16px;
    background: var(--accent-soft); color: var(--accent-fg);
  }
  .tile.done .mono-icon { background: var(--ok-soft); color: var(--ok); }
  .title { display: flex; flex-direction: column; align-items: flex-start; gap: 4px; min-width: 0; }
  .title h3 { overflow-wrap: anywhere; }
  .desc { flex: 1; }
  .meta { gap: 6px; }
  .actions { margin-top: auto; gap: 4px; flex-wrap: nowrap; }
  .icon-only { padding: 3px 7px; }
  .job:empty { display: none; }
</style>

<script lang="ts">
  import { onMount } from 'svelte';
  import { api, app, confirm, openMail, run } from '../lib/app.svelte';
  import { t } from '../lib/i18n.svelte';
  import type { SoftwareItem, SoftwareList } from '../lib/types';
  import Icon from '../lib/components/Icon.svelte';
  import Calcifer from '../lib/components/Calcifer.svelte';
  import JobView from '../lib/components/JobView.svelte';

  let list = $state<SoftwareList | null>(null);
  let loading = $state(false);

  async function load() {
    loading = true;
    list = (await run(() => api<SoftwareList>('software.list'))) ?? list;
    loading = false;
  }
  onMount(load);

  const stateKey = (s: number | null): string =>
    s == null || s <= 1 ? 'available' : s <= 4 ? 'preparing' : s === 5 ? 'downloading' : s === 6 || s === 14 ? 'waiting'
    : s === 7 || s === 11 ? 'installing' : s >= 8 && s <= 10 ? 'reboot' : s === 12 ? 'installed' : s === 13 ? 'error' : 'unknown';

  async function install(item: SoftwareItem) {
    if (!item.sccm?.packageId || !item.sccm.programId) return;
    if (!(await confirm(item.name, t('software.confirmInstall', { name: item.name }), t('software.install')))) return;
    await run(() => api('software.install', { key: item.key, name: item.name, packageId: item.sccm!.packageId, programId: item.sccm!.programId }));
  }

  const groups = $derived.by(() => {
    const map = new Map<string, SoftwareItem[]>();
    for (const item of list?.items ?? []) {
      const key = item.category ?? '';
      if (!map.has(key)) map.set(key, []);
      map.get(key)!.push(item);
    }
    return [...map.entries()];
  });
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

  {#if list.items.length === 0}
    <div class="note"><Calcifer /><p>{t('software.empty')}</p></div>
  {/if}

  <div class="stack">
    {#each groups as [category, items] (category)}
      <section class="stack tight">
        {#if category}<h2 class="category">{category}</h2>{/if}
        {#each items as item (item.key)}
          <article class="card item">
            <div class="main">
              <div class="row">
                <h3>{item.name}</h3>
                {#if item.installed}
                  <span class="badge ok">{t('software.installed', { version: item.installed.version ?? '?' })}</span>
                {:else}
                  <span class="badge">{t('software.notInstalled')}</span>
                {/if}
                {#if item.sccm}
                  <span class="badge info">{t(item.sccm.kind === 'package' ? 'software.package' : 'software.application')} · {t(`software.states.${stateKey(item.sccm.evaluationState)}`)}</span>
                {:else}
                  <span class="badge warn">{t('software.notInCenter')}</span>
                {/if}
              </div>
              {#if item.note}<p class="small muted">{item.note}</p>{/if}
            </div>
            <div class="row">
              {#if item.canInstall && !item.installed}
                <button class="btn primary small" disabled={app.jobs['software:' + item.key]?.state === 'running'} onclick={() => install(item)}><Icon name="download" size={14} />{t('software.install')}</button>
              {/if}
              <button class="btn small" onclick={() => run(() => api('software.open', { link: item.link }))}><Icon name="external" size={14} />{t('software.open')}</button>
              <button class="btn ghost small" onclick={() => openMail('softwareRequest', { app: item.name })}><Icon name="mail" size={14} />{t('software.request')}</button>
            </div>
            <div class="job"><JobView id={'software:' + item.key} compact /></div>
          </article>
        {/each}
      </section>
    {/each}
  </div>
{:else}
  <p class="muted">{t('common.loading')}</p>
{/if}

<style>
  .head { margin-bottom: 16px; }
  .category { font-size: 13px; text-transform: uppercase; letter-spacing: 0.06em; color: var(--text-muted); margin-top: 6px; }
  .item { display: grid; grid-template-columns: 1fr auto; gap: 10px 16px; align-items: center; padding: 14px 18px; }
  .main { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
  .job { grid-column: 1 / -1; }
  .job:empty { display: none; }
</style>

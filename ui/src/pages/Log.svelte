<script lang="ts">
  import { tick } from 'svelte';
  import { api, app, copyText, run, toast } from '../lib/app.svelte';
  import { t } from '../lib/i18n.svelte';
  import { formatTime } from '../lib/format';
  import type { LogLevel } from '../lib/types';
  import Icon from '../lib/components/Icon.svelte';

  const levels: LogLevel[] = ['debug', 'info', 'warning', 'error'];
  let show = $state<Record<LogLevel, boolean>>({ debug: false, info: true, warning: true, error: true });
  let filter = $state('');
  let autoscroll = $state(true);
  let list = $state<HTMLElement | null>(null);

  const visible = $derived.by(() => {
    const f = filter.trim().toLowerCase();
    return app.logs.filter((e) => show[e.level] && (!f || e.message.toLowerCase().includes(f) || e.source.toLowerCase().includes(f))).slice(-1500);
  });

  $effect(() => {
    void visible.length;
    if (autoscroll && list) void tick().then(() => list && (list.scrollTop = list.scrollHeight));
  });

  const asText = () => visible.map((e) => `${e.time} [${e.level}] ${e.source}: ${e.message}`).join('\n');
</script>

<header class="page-head">
  <h1>{t('log.title')}</h1>
  <p class="break">{t('log.subtitle', { path: app.info?.logDir ?? 'C:\\Ember\\Logs' })}</p>
</header>

<div class="row tools">
  {#each levels as l (l)}
    <label class="check"><input type="checkbox" bind:checked={show[l]} />{t(`log.levels.${l}`)}</label>
  {/each}
  <input class="filter" bind:value={filter} placeholder={t('log.filter')} />
  <label class="check"><input type="checkbox" bind:checked={autoscroll} />{t('log.autoscroll')}</label>
  <span class="spacer"></span>
  <button class="btn small" onclick={async () => { await copyText(asText()); toast('ok', t('common.copied')); }}><Icon name="copy" size={14} />{t('log.copy')}</button>
  <button class="btn small" onclick={() => run(() => api<string>('log.save'), (p) => t('log.savedTo', { path: p }))}><Icon name="download" size={14} />{t('log.save')}</button>
  <button class="btn small" onclick={() => run(() => api('app.openFolder', { key: 'logs' }))}><Icon name="folder" size={14} />{t('log.openFolder')}</button>
  <button class="btn small ghost" onclick={async () => { await run(() => api('log.clear')); app.logs = []; }}><Icon name="trash" size={14} />{t('log.clear')}</button>
</div>

<div class="log card" bind:this={list}>
  {#if visible.length === 0}
    <p class="muted">{t('log.empty')}</p>
  {:else}
    {#each visible as e, i (i)}
      <div class="entry {e.level}">
        <span class="time">{formatTime(e.time)}</span>
        <span class="source">{e.source}</span>
        <span class="message">{e.message}</span>
      </div>
    {/each}
  {/if}
</div>

<style>
  .tools { margin-bottom: 12px; gap: 12px; }
  .filter { width: 220px; }
  .log { font-family: var(--mono); font-size: 12.5px; padding: 10px 0; height: calc(100vh - 260px); min-height: 300px; overflow: auto; }
  .entry { display: grid; grid-template-columns: 74px 52px 1fr; gap: 10px; padding: 2px 16px; }
  .entry:hover { background: var(--surface-2); }
  .time, .source { color: var(--text-muted); }
  .message { white-space: pre-wrap; overflow-wrap: anywhere; }
  .entry.debug .message { color: var(--text-muted); }
  .entry.warning .message { color: var(--warn); }
  .entry.error .message { color: var(--error); font-weight: 500; }
  .log .muted { padding: 0 16px; font-family: var(--font); }
</style>

<script lang="ts">
  import { onMount } from 'svelte';
  import { slide } from 'svelte/transition';
  import { api, run } from '../lib/app.svelte';
  import { host, t } from '../lib/i18n.svelte';
  import type { CheckAction, CheckResult } from '../lib/types';
  import Icon from '../lib/components/Icon.svelte';

  let checks = $state<CheckResult[] | null>(null);
  let loading = $state(false);
  let busy = $state<string | null>(null);
  let open = $state<Record<string, boolean>>({});
  let output = $state<Record<string, string>>({});

  async function load() {
    loading = true;
    checks = (await run(() => api<CheckResult[]>('system.checks'))) ?? checks;
    loading = false;
  }
  onMount(load);

  const tone: Record<string, string> = { ok: 'ok', warn: 'warn', locked: 'info', info: 'info', missing: '', error: 'error' };

  async function act(c: CheckResult, a: CheckAction) {
    busy = c.id + a.id;
    const details = a.elevated ? await api<string>('system.adminCommands', { check: c.id, action: a.id }).catch(() => a.label) : undefined;
    const r = await run(() => api<string>('system.action', { check: c.id, action: a.id }), undefined, details);
    busy = null;
    if (r !== undefined) {
      output[c.id] = r;
      open[c.id] = true;
      await load();
    }
  }
</script>

<header class="page-head">
  <h1>{t('system.title')}</h1>
  <p>{t('system.subtitle')}</p>
</header>

<div class="row head">
  <span class="spacer"></span>
  <button class="btn small" disabled={loading} onclick={load}><Icon name="refresh" size={14} />{t('system.run')}</button>
</div>

{#if checks}
  <div class="stack tight">
    {#each checks as c (c.id)}
      <section class="card check">
        <div class="row">
          <span class="badge {tone[c.status]}">{t(`system.status.${c.status}`)}</span>
          <h3>{host(c.title)}</h3>
          <span class="muted">{host(c.value)}</span>
          <span class="spacer"></span>
          {#each c.actions as a (a.id)}
            <button class="btn small {c.status === 'warn' ? 'primary' : ''}" disabled={busy !== null} title={a.elevated ? t('common.needsAdmin') : undefined} onclick={() => act(c, a)}>
              {#if a.elevated}<Icon name="shield" size={14} />{/if}{host(a.label)}
            </button>
          {/each}
          {#if c.detail || output[c.id]}
            <button class="btn ghost small" aria-expanded={!!open[c.id]} onclick={() => (open[c.id] = !open[c.id])}>{t('common.details')}</button>
          {/if}
        </div>
        {#if open[c.id]}
          <pre transition:slide={{ duration: 160 }}>{host(output[c.id] ?? c.detail)}</pre>
        {/if}
      </section>
    {/each}
  </div>
{:else}
  <p class="muted">{t('common.loading')}</p>
{/if}

<style>
  .head { margin: -8px 0 12px; }
  .check { padding: 12px 16px; }
  pre { margin: 10px 0 0; padding: 10px 12px; background: var(--surface-2); border-radius: var(--radius-small); white-space: pre-wrap; font-family: var(--mono); font-size: 12px; max-height: 280px; overflow: auto; }
</style>

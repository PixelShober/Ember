<script lang="ts">
  import { slide } from 'svelte/transition';
  import { api, app, run } from '../app.svelte';
  import { host, t } from '../i18n.svelte';
  import Icon from './Icon.svelte';

  let { id, compact = false }: { id: string; compact?: boolean } = $props();
  const job = $derived(app.jobs[id]);

  const icons: Record<string, string> = { pending: 'chevron', running: 'refresh', done: 'check', failed: 'x', skipped: 'chevron' };
  const badge: Record<string, string> = { running: 'accent', done: 'ok', failed: 'error', cancelled: 'warn' };
</script>

{#if job}
  <div class="job" transition:slide={{ duration: 180 }}>
    <div class="row between">
      <div class="row">
        <h3>{host(job.title)}</h3>
        <span class="badge {badge[job.state]}">{t(`jobs.states.${job.state}`)}</span>
      </div>
      <div class="row">
        {#if job.state === 'running'}
          <button class="btn small" onclick={() => run(() => api('jobs.cancel', { id }))}><Icon name="stop" size={14} />{t('jobs.cancel')}</button>
        {:else}
          {#if job.failedIndex >= 0}
            <button class="btn small primary" onclick={() => run(() => api('jobs.retry', { id }))}><Icon name="refresh" size={14} />{t('jobs.retry')}</button>
          {/if}
          <button class="btn small ghost" onclick={() => run(async () => { await api('jobs.dismiss', { id }); delete app.jobs[id]; })}>{t('jobs.dismiss')}</button>
        {/if}
      </div>
    </div>
    <div class="progress" aria-hidden="true"><div style="width: {job.progress}%"></div></div>
    {#if job.status}<p class="small muted">{host(job.status)}</p>{/if}

    {#if !compact || job.state !== 'done'}
      <ol class="steps">
        {#each job.steps as step, i (i)}
          <li class="step {step.status}">
            <span class="icon" title={t(`jobs.status.${step.status}`)}><Icon name={icons[step.status]} size={15} /></span>
            <div class="body">
              <div class="row between">
                <span class="title">{host(step.title)}</span>
                {#if step.duration}<span class="small muted nowrap">{step.duration}</span>{/if}
              </div>
              {#if step.detail}<p class="small detail break">{host(step.detail)}</p>{/if}
              {#if step.progressVisible}
                <div class="progress" class:indeterminate={step.progress == null}><div style="width: {step.progress ?? 0}%"></div></div>
              {/if}
            </div>
          </li>
        {/each}
      </ol>
    {/if}
  </div>
{/if}

<style>
  .job { display: flex; flex-direction: column; gap: 8px; padding-top: 4px; }
  .steps { list-style: none; margin: 4px 0 0; padding: 0; display: flex; flex-direction: column; gap: 2px; }
  .step { display: flex; gap: 10px; padding: 6px 8px; border-radius: var(--radius-small); }
  .step.running { background: var(--accent-soft); }
  .step.failed { background: var(--error-soft); }
  .icon { width: 22px; height: 22px; border-radius: 50%; display: grid; place-items: center; flex: none; background: var(--surface-2); color: var(--text-muted); }
  .step.done .icon { background: var(--ok-soft); color: var(--ok); }
  .step.failed .icon { background: var(--error); color: var(--surface); }
  .step.running .icon { background: var(--accent); color: var(--accent-text); }
  .step.running .icon :global(svg) { animation: spin 1.2s linear infinite; }
  .step.skipped { opacity: 0.6; }
  .body { flex: 1; display: flex; flex-direction: column; gap: 4px; min-width: 0; }
  .title { font-weight: 500; }
  .detail { color: var(--text-muted); }
  .step.failed .detail { color: var(--error); }
  @keyframes spin { to { transform: rotate(360deg); } }
</style>

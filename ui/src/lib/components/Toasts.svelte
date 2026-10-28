<script lang="ts">
  import { fly } from 'svelte/transition';
  import { flip } from 'svelte/animate';
  import { app, dismissToast } from '../app.svelte';
  import { t } from '../i18n.svelte';
  import Icon from './Icon.svelte';

  const icon = { ok: 'check', error: 'alert', info: 'info' } as const;
</script>

<div class="toasts" role="status" aria-live="polite">
  {#each app.toasts as toast (toast.id)}
    <div class="toast {toast.kind}" in:fly={{ y: 16, duration: 200 }} out:fly={{ x: 40, duration: 160 }} animate:flip={{ duration: 200 }}>
      <Icon name={icon[toast.kind]} />
      <p class="break">{toast.text}</p>
      {#if toast.action}
        <button class="btn small" onclick={() => { toast.action!.run(); dismissToast(toast.id); }}>{toast.action.label}</button>
      {/if}
      <button class="btn ghost small close" aria-label={t('common.close')} onclick={() => dismissToast(toast.id)}><Icon name="x" size={14} /></button>
    </div>
  {/each}
</div>

<style>
  .toasts { position: fixed; right: 20px; bottom: 20px; display: flex; flex-direction: column; gap: 8px; z-index: 50; max-width: min(460px, calc(100vw - 40px)); }
  .toast {
    display: flex; align-items: flex-start; gap: 10px; padding: 10px 10px 10px 14px;
    background: var(--surface); border: 1px solid var(--border); border-left: 4px solid var(--info);
    border-radius: var(--radius-small); box-shadow: 0 8px 28px rgb(0 0 0 / 0.16);
  }
  .toast.ok { border-left-color: var(--ok); }
  .toast.ok > :global(svg) { color: var(--ok); }
  .toast.error { border-left-color: var(--error); }
  .toast.error > :global(svg) { color: var(--error); }
  .toast > :global(svg) { flex: none; margin-top: 2px; color: var(--info); }
  p { flex: 1; }
  .close { padding: 2px 6px; }
</style>

<script lang="ts">
  import { fly } from 'svelte/transition';
  import { api, app, run } from '../app.svelte';
  import { t } from '../i18n.svelte';
  import Icon from './Icon.svelte';

  let hidden = $state('');
  const u = $derived(app.update);
  const key = $derived(u ? `${u.uiReady}|${u.exeReady}|${u.latest}` : '');
</script>

{#if u && hidden !== key && (u.uiReady || u.exeReady)}
  <div class="banner" transition:fly={{ y: -12, duration: 200 }}>
    <Icon name="sparkle" />
    {#if u.exeReady}
      <div class="text">
        <strong>{t('update.exeReadyTitle', { version: u.latest })}</strong>
        <span class="muted">{t('update.exeReadyText')}</span>
      </div>
      <button class="btn primary small" onclick={() => run(() => api('update.applyExe'))}>{t('update.restart')}</button>
    {:else}
      <div class="text">
        <strong>{t('update.uiReadyTitle')}</strong>
        <span class="muted">{t('update.uiReadyText', { version: u.uiReady })}</span>
      </div>
      <button class="btn primary small" onclick={() => run(() => api('update.applyUi'))}>{t('update.reload')}</button>
    {/if}
    <button class="btn ghost small" onclick={() => (hidden = key)}>{t('update.later')}</button>
  </div>
{/if}

<style>
  .banner {
    display: flex; align-items: center; gap: 12px; margin-bottom: 18px;
    padding: 10px 14px; border-radius: var(--radius);
    background: var(--accent-soft); border: 1px solid color-mix(in srgb, var(--accent) 25%, transparent);
  }
  .banner > :global(svg) { color: var(--accent-fg); flex: none; }
  .text { flex: 1; display: flex; flex-direction: column; }
</style>

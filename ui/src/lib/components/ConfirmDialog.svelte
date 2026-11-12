<script lang="ts">
  import { fade, scale } from 'svelte/transition';
  import { app, closeConfirm } from '../app.svelte';
  import { host, t } from '../i18n.svelte';
  import { modal } from '../modal';
</script>

{#if app.confirm}
  <div class="layer" transition:fade={{ duration: 120 }}>
    <div class="backdrop" onclick={() => closeConfirm(false)} role="presentation"></div>
    <div class="dialog" role="alertdialog" aria-modal="true" aria-labelledby="confirm-title" aria-describedby="confirm-text" use:modal={() => closeConfirm(false)} transition:scale={{ start: 0.96, duration: 150 }}>
      <h2 id="confirm-title">{host(app.confirm.title)}</h2>
      <p id="confirm-text" class="break">{host(app.confirm.text)}</p>
      <div class="row actions">
        <!-- Focus starts on the harmless choice: Cancel when the action deletes or removes something. -->
        <!-- svelte-ignore a11y_autofocus -->
        <button class="btn" autofocus={app.confirm.danger} onclick={() => closeConfirm(false)}>{t('common.cancel')}</button>
        <!-- svelte-ignore a11y_autofocus -->
        <button class="btn {app.confirm.danger ? 'danger' : 'primary'}" autofocus={!app.confirm.danger} onclick={() => closeConfirm(true)}>{app.confirm.confirm}</button>
      </div>
    </div>
  </div>
{/if}

<style>
  .layer { position: fixed; inset: 0; z-index: 40; display: flex; align-items: flex-start; justify-content: center; padding-top: 18vh; }
  .backdrop { position: absolute; inset: 0; background: var(--backdrop); }
  .dialog {
    position: relative;
    width: min(520px, calc(100vw - 40px));
    background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius);
    box-shadow: var(--shadow-lift), 0 24px 60px -12px rgb(0 0 0 / 0.3); padding: 22px 24px;
    display: flex; flex-direction: column; gap: 12px;
  }
  .dialog p { white-space: pre-line; color: var(--text-muted); }
  .actions { justify-content: flex-end; margin-top: 6px; }
</style>

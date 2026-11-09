<script lang="ts">
  // Admins see Ember through another role, a starter key or nobody's role, to tune what the
  // others get. Ember.exe keeps the choice until it closes; the bar brings you back.
  import { api, app, run } from '../app.svelte';
  import { t } from '../i18n.svelte';
  import Icon from './Icon.svelte';

  let { bar = false }: { bar?: boolean } = $props();

  const preview = $derived(app.info?.preview ?? null);

  function label(target: string): string {
    if (target === 'none') return t('preview.none');
    if (target.startsWith('key:')) return t('preview.key', { level: t(`access.keyLevels.${target.slice(4)}`) });
    return t('preview.role', { role: target.slice(5) });
  }

  async function choose(target: string | null) {
    const ok = await run(() => api('app.preview', { target }));
    // Start over, so every page loads with the modules of the new view.
    if (ok !== undefined) location.reload();
  }
</script>

{#if preview}
  {#if bar}
    {#if preview.current}
      <div class="preview-bar" role="status">
        <Icon name="eye" size={15} />
        <span>{t('preview.active', { view: label(preview.current) })}</span>
        <button class="btn small" onclick={() => choose(null)}>{t('preview.back')}</button>
      </div>
    {/if}
  {:else}
    <label class="switch small">
      <span class="muted">{t('preview.title')}</span>
      <select value={preview.current ?? ''} onchange={(e) => choose((e.currentTarget as HTMLSelectElement).value || null)}>
        <option value="">{t('preview.own')}</option>
        {#if preview.roles.length}
          <optgroup label={t('preview.roles')}>
            {#each preview.roles as role (role)}<option value={'role:' + role}>{role}</option>{/each}
          </optgroup>
        {/if}
        <optgroup label={t('preview.keys')}>
          {#each preview.keys as level (level)}<option value={'key:' + level}>{t(`access.keyLevels.${level}`)}</option>{/each}
        </optgroup>
        <option value="none">{t('preview.none')}</option>
      </select>
    </label>
  {/if}
{/if}

<style>
  .switch { display: flex; flex-direction: column; gap: 4px; }
  .switch select { width: 100%; }
  .preview-bar {
    position: fixed; left: 50%; bottom: 16px; transform: translateX(-50%); z-index: 50;
    display: flex; align-items: center; gap: 10px; padding: 8px 10px 8px 14px;
    background: var(--surface); border: 1px solid var(--warn, var(--border)); border-radius: 999px;
    box-shadow: var(--shadow, 0 6px 24px rgb(0 0 0 / 0.18)); font-size: 13px; max-width: calc(100vw - 32px);
  }
</style>

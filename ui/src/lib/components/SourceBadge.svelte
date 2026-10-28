<script lang="ts">
  import { t } from '../i18n.svelte';

  // Where a setting comes from: own value, team.json, detected at start, or Ember's default.
  let { user, team, detected, onreset }: { user: unknown; team: unknown; detected?: unknown; onreset?: () => void } = $props();
  const has = (v: unknown) => v !== null && v !== undefined;
  const source = $derived(has(user) ? 'user' : has(team) ? 'team' : has(detected) ? 'detected' : 'default');
  const tone: Record<string, string> = { team: 'info', user: 'accent', detected: 'ok', default: '' };
</script>

<span class="row source">
  <span class="badge {tone[source]}">{t(`common.sources.${source}`)}</span>
  {#if source === 'user' && onreset && (has(team) || has(detected))}
    <button class="btn ghost small" onclick={onreset}>{has(team) ? t('common.resetToTeam') : t('common.resetToDetected')}</button>
  {/if}
</span>

<style>
  .source { gap: 4px; }
</style>

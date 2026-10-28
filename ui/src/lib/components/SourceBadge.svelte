<script lang="ts">
  import { t } from '../i18n.svelte';

  // Where a setting comes from: own value, team.json or Ember's default.
  let { user, team, onreset }: { user: unknown; team: unknown; onreset?: () => void } = $props();
  const source = $derived(user !== null && user !== undefined ? 'user' : team !== null && team !== undefined ? 'team' : 'default');
</script>

<span class="row source">
  <span class="badge {source === 'team' ? 'info' : source === 'user' ? 'accent' : ''}">{t(`common.sources.${source}`)}</span>
  {#if source === 'user' && team !== null && team !== undefined && onreset}
    <button class="btn ghost small" onclick={onreset}>{t('common.resetToTeam')}</button>
  {/if}
</span>

<style>
  .source { gap: 4px; }
</style>

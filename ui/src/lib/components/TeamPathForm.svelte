<script lang="ts">
  // The way to the team file: type or browse to the path. Ember reads and checks the file once before it keeps the
  // path, so a typo shows up here and not at the next start.
  import { api, app, run, toast } from '../app.svelte';
  import { t } from '../i18n.svelte';
  import type { AppInfo } from '../types';
  import Icon from './Icon.svelte';

  let { onDone }: { onDone?: () => void } = $props();
  let path = $state(app.info?.team?.path ?? '');
  let busy = $state(false);

  async function browse() {
    const r = await api<string | null>('app.pickFile', { title: t('access.setup.team'), filter: 'Teamdatei (*.ember;*.json)|*.ember;*.json' }).catch(() => null);
    if (r) path = r;
  }

  async function apply() {
    busy = true;
    const info = await run(() => api<AppInfo>('team.setPath', { path: path.trim() }));
    busy = false;
    if (!info) return;
    toast('ok', t('access.setup.teamSaved', { path: path.trim() }));
    onDone?.();
  }
</script>

<form class="team" onsubmit={(e) => { e.preventDefault(); void apply(); }}>
  <input type="text" bind:value={path} placeholder={t('access.setup.teamPlaceholder')} spellcheck="false" autocomplete="off" aria-label={t('access.setup.team')} />
  <div class="row">
    <button type="button" class="btn" onclick={browse}><Icon name="folder" size={16} />{t('access.setup.browse')}</button>
    <button type="submit" class="btn primary" disabled={busy || !path.trim()}><Icon name="check" size={16} />{t('access.setup.useTeam')}</button>
  </div>
</form>

<style>
  .team { display: flex; flex-direction: column; gap: 10px; }
  .team input { width: 100%; font-family: var(--mono); font-size: 12.5px; }
</style>

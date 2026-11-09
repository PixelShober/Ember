<script lang="ts">
  // First start without team.json: a starter key unlocks a fixed set of modules until the team.json
  // is there; with a team.json its roles decide. Both ways start here.
  import { fade } from 'svelte/transition';
  import { api, app, openMail, run } from '../lib/app.svelte';
  import { t } from '../lib/i18n.svelte';
  import type { AppInfo } from '../lib/types';
  import Icon from '../lib/components/Icon.svelte';
  import Calcifer from '../lib/components/Calcifer.svelte';
  import TeamPathForm from '../lib/components/TeamPathForm.svelte';

  let key = $state('');
  let busy = $state(false);
  const team = $derived(app.info?.team);

  async function unlock() {
    busy = true;
    // The host answers with app.info; getting access reloads the page (lib/app.svelte.ts).
    await run(() => api<AppInfo>('app.unlock', { key: key.trim() }));
    busy = false;
  }
</script>

<main class="center" in:fade>
  <div class="setup">
    <div class="intro">
      <Calcifer size={72} />
      <h1>{t('access.setup.title')}</h1>
      <p>{t('access.setup.text')}</p>
    </div>

    {#if team?.path && team.error}
      <div class="note warn"><Icon name="alert" /><p>{t('access.setup.teamFailed', { path: team.path, detail: team.error })}</p></div>
    {/if}

    <section class="card stack tight">
      <div class="card-head"><Icon name="shield" /><h2>{t('access.setup.key')}</h2></div>
      <p class="small muted">{t('access.setup.keyHint')}</p>
      <form class="key" onsubmit={(e) => { e.preventDefault(); void unlock(); }}>
        <input type="text" bind:value={key} placeholder="EMBER-XXXXX-XXXXX-XXXXX-XXXXX" spellcheck="false" autocomplete="off" aria-label={t('access.setup.key')} />
        <button type="submit" class="btn primary" disabled={busy || !key.trim()}><Icon name="check" size={16} />{t('access.setup.unlock')}</button>
      </form>
    </section>

    <section class="card stack tight">
      <div class="card-head"><Icon name="folder" /><h2>{t('access.setup.team')}</h2></div>
      <p class="small muted">{t('access.setup.teamHint')}</p>
      <TeamPathForm />
    </section>

    <p class="small muted foot">
      {t('access.setup.account', { user: app.info?.user ?? '' })}
      <button class="btn ghost small" onclick={() => openMail('accessRequest')}><Icon name="mail" size={14} />{t('access.locked.request')}</button>
    </p>
  </div>
</main>

<style>
  .center { min-height: 100%; display: grid; place-items: center; padding: 32px 24px; }
  .setup { width: min(560px, 100%); display: flex; flex-direction: column; gap: 16px; }
  .intro { display: flex; flex-direction: column; align-items: center; gap: 10px; text-align: center; margin-bottom: 4px; }
  .intro h1 { margin: 6px 0 0; font-size: 22px; }
  .card-head { margin-bottom: 0; }
  .key { display: flex; gap: 8px; flex-wrap: wrap; }
  .key input { flex: 1 1 260px; font-family: var(--mono); font-size: 13px; letter-spacing: 0.04em; text-transform: uppercase; }
  .foot { display: flex; align-items: center; justify-content: center; gap: 8px; flex-wrap: wrap; }
</style>

<script lang="ts">
  // First start, or the team file cannot be used: Ember needs the path to the signed team file.
  // There is no other way in; the roles in the file decide what this account sees.
  import { fade } from 'svelte/transition';
  import { app } from '../lib/app.svelte';
  import { t } from '../lib/i18n.svelte';
  import Icon from '../lib/components/Icon.svelte';
  import Calcifer from '../lib/components/Calcifer.svelte';
  import TeamPathForm from '../lib/components/TeamPathForm.svelte';

  const team = $derived(app.info?.team);
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
      <div class="card-head"><Icon name="folder" /><h2>{t('access.setup.team')}</h2></div>
      <p class="small muted">{t('access.setup.teamHint')}</p>
      <TeamPathForm />
    </section>

    <p class="small muted foot">{t('access.setup.account', { user: app.info?.user ?? '' })}</p>
  </div>
</main>

<style>
  .center { min-height: 100%; display: grid; place-items: center; padding: 32px 24px; }
  .setup { width: min(560px, 100%); display: flex; flex-direction: column; gap: 16px; }
  .intro { display: flex; flex-direction: column; align-items: center; gap: 10px; text-align: center; margin-bottom: 4px; }
  .intro h1 { margin: 6px 0 0; font-size: 22px; }
  .card-head { margin-bottom: 0; }
  .foot { text-align: center; }
</style>

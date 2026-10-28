<script lang="ts">
  import { onMount } from 'svelte';
  import { api, confirm, navigate, run } from '../lib/app.svelte';
  import { t } from '../lib/i18n.svelte';
  import type { GitIdentityInfo } from '../lib/types';
  import Icon from '../lib/components/Icon.svelte';
  import Calcifer from '../lib/components/Calcifer.svelte';

  let info = $state<GitIdentityInfo | null>(null);
  let name = $state('');
  let email = $state('');

  onMount(async () => {
    info = (await run(() => api<GitIdentityInfo>('git.get'))) ?? null;
    name = info?.name ?? info?.suggestedName ?? '';
    email = info?.email ?? info?.suggestedEmail ?? '';
  });

  const valid = $derived(name.trim().length > 1 && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim()));
  const changed = $derived(info != null && (name.trim() !== (info.name ?? '') || email.trim() !== (info.email ?? '')));

  function useSuggestion() {
    name = info?.suggestedName ?? name;
    email = info?.suggestedEmail ?? email;
  }

  async function save() {
    if (!(await confirm(t('git.title'), t('git.confirm', { name: name.trim(), email: email.trim() }), t('git.save')))) return;
    const r = await run(() => api<GitIdentityInfo>('git.set', { name: name.trim(), email: email.trim() }), t('git.saved'));
    if (r) info = r;
  }
</script>

<header class="page-head">
  <h1>{t('git.title')}</h1>
  <p>{t('git.subtitle')}</p>
</header>

{#if info && !info.gitFound}
  <div class="note warn"><Calcifer /><p>{t('git.gitMissing')}</p>
    <button class="btn small" onclick={() => navigate('software')}>{t('nav.software')}</button></div>
{:else if info}
  <div class="grid two">
    <section class="card stack tight">
      <h2>{t('git.current')}</h2>
      <dl>
        <dt>{t('git.name')}</dt><dd>{info.name ?? t('git.notSet')}</dd>
        <dt>{t('git.email')}</dt><dd>{info.email ?? t('git.notSet')}</dd>
      </dl>
    </section>

    <section class="card stack tight">
      <div class="row between">
        <h2>{t('git.suggested')}</h2>
        {#if info.source}<span class="badge info">{t(`git.sources.${info.source}`)}</span>{/if}
      </div>
      <dl>
        <dt>{t('git.name')}</dt><dd>{info.suggestedName ?? '–'}</dd>
        <dt>{t('git.email')}</dt><dd>{info.suggestedEmail ?? '–'}</dd>
      </dl>
      <div><button class="btn small" onclick={useSuggestion} disabled={!info.suggestedName}>{t('git.useSuggestion')}</button></div>
    </section>
  </div>

  <section class="card stack form">
    <label class="field"><span>{t('git.name')}</span><input bind:value={name} autocomplete="name" spellcheck="false" /></label>
    <label class="field"><span>{t('git.email')}</span><input bind:value={email} type="text" inputmode="email" autocomplete="email" spellcheck="false" /></label>
    <div class="row">
      <button class="btn primary" disabled={!valid || !changed} onclick={save}><Icon name="check" size={16} />{t('git.save')}</button>
      {#if !valid && (name || email)}<span class="small muted">{t('errors.gitIdentityInvalid')}</span>{/if}
    </div>
  </section>
{:else}
  <p class="muted">{t('common.loading')}</p>
{/if}

<style>
  .two { grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); }
  dl { display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px; margin: 0; }
  dt { color: var(--text-muted); }
  dd { margin: 0; font-weight: 500; }
  .form { margin-top: 16px; max-width: 560px; }
</style>

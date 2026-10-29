<script lang="ts">
  import { onMount } from 'svelte';
  import { api, confirm, navigate, run } from '../lib/app.svelte';
  import { t } from '../lib/i18n.svelte';
  import type { GitIdentityInfo, GitProxyInfo } from '../lib/types';
  import Icon from '../lib/components/Icon.svelte';
  import Calcifer from '../lib/components/Calcifer.svelte';

  let info = $state<GitIdentityInfo | null>(null);
  let name = $state('');
  let email = $state('');
  let proxy = $state<GitProxyInfo | null>(null);

  const loadProxy = async () => (proxy = (await run(() => api<GitProxyInfo | null>('git.proxy'))) ?? null);

  async function setProxy() {
    await run(() => api<string>('proxy.set', { tool: 'git' }), (r) => r);
    await loadProxy();
  }

  onMount(async () => {
    void loadProxy();
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
  <h2 class="section-title first">{t('git.identity')}</h2>
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

  {#if proxy}
    <section class="card stack tight proxy">
      <div class="card-head">
        <Icon name="proxy" />
        <div class="grow"><h2>{t('git.proxy.title')}</h2><p class="small muted">{t('git.proxy.subtitle')}</p></div>
        <span class="badge {proxy.state === 'px' ? 'ok' : proxy.state === 'other' ? 'warn' : ''}">{t(`proxy.states.${proxy.state}`)}</span>
      </div>
      <dl>
        <dt>{t('git.proxy.httpProxy')}</dt><dd><code>{proxy.httpProxy ?? '–'}</code></dd>
        <dt>{t('git.proxy.direct')}</dt>
        <dd>
          {#if proxy.domains.length}
            <span class="row">{#each proxy.domains as d (d)}<span class="badge">{d}</span>{/each}</span>
          {:else}
            <span class="small muted">{t('git.proxy.noDomains')}</span>
          {/if}
        </dd>
      </dl>
      {#if proxy.state === 'px' && proxy.missingExceptions.length === 0 && proxy.noProxyEnv}
        <p class="small ok-text"><Icon name="check" size={14} /> {t('git.proxy.ok')}</p>
      {/if}
      {#if proxy.state === 'px' && proxy.missingExceptions.length}
        <div class="note warn"><Calcifer /><p class="small">{t('git.proxy.missing', { list: proxy.missingExceptions.join(', ') })}</p></div>
      {/if}
      {#if proxy.domains.length && !proxy.noProxyEnv}
        <div class="note"><Icon name="info" /><p class="small">{t('git.proxy.envMissing')}</p>
          <button class="btn small" onclick={() => navigate('proxy')}>{t('git.proxy.envFix')}</button></div>
      {/if}
      <div class="row">
        {#if proxy.canSet}<button class="btn primary small" onclick={setProxy}><Icon name="check" size={14} />{t('git.proxy.set')}</button>{/if}
        <button class="btn small ghost" onclick={() => navigate('settings')}><Icon name="settings" size={14} />{t('git.proxy.editDomains')}</button>
      </div>
    </section>
  {/if}
{:else}
  <p class="muted">{t('common.loading')}</p>
{/if}

<style>
  .two { grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); }
  dl { display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px; margin: 0; }
  dt { color: var(--text-muted); }
  dd { margin: 0; min-width: 0; overflow-wrap: anywhere; }
  .section-title { margin: 22px 0 10px; }
  .section-title.first { margin-top: 0; }
  .form { margin-top: 16px; }
  .proxy { margin-top: 16px; }
  .grow { flex: 1; min-width: 240px; display: flex; flex-direction: column; gap: 3px; }
  .ok-text { display: flex; align-items: center; gap: 6px; color: var(--ok); }
  dd { margin: 0; font-weight: 500; }
  .form { margin-top: 16px; max-width: 560px; }
</style>

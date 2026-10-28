<script lang="ts">
  import { onMount } from 'svelte';
  import { fly, fade } from 'svelte/transition';
  import { api, app, init, navigate, toast } from './lib/app.svelte';
  import { t } from './lib/i18n.svelte';
  import type { Route } from './lib/types';
  import Icon from './lib/components/Icon.svelte';
  import Toasts from './lib/components/Toasts.svelte';
  import ConfirmDialog from './lib/components/ConfirmDialog.svelte';
  import MailDialog from './lib/components/MailDialog.svelte';
  import UpdateBanner from './lib/components/UpdateBanner.svelte';
  import Start from './pages/Start.svelte';
  import Proxy from './pages/Proxy.svelte';
  import Git from './pages/Git.svelte';
  import Software from './pages/Software.svelte';
  import Power from './pages/Power.svelte';
  import System from './pages/System.svelte';
  import Ai from './pages/Ai.svelte';
  import Settings from './pages/Settings.svelte';
  import Log from './pages/Log.svelte';

  let failed = $state<string | null>(null);

  onMount(() => {
    init().catch((e) => (failed = String(e?.message ?? e)));
  });

  // KI and Energie are advanced modules: only admins from the team configuration see them,
  // and Ctrl+E shows or hides them.
  const advancedRoutes: Route[] = ['power', 'ai'];
  const showAdvanced = $derived(!!app.info?.showAdvanced);

  const nav = $derived(
    [
      { route: 'start', icon: 'home' },
      { route: 'proxy', icon: 'proxy' },
      { route: 'git', icon: 'git' },
      { route: 'software', icon: 'software' },
      { route: 'system', icon: 'system' },
      ...(showAdvanced ? [{ route: 'power', icon: 'power' }, { route: 'ai', icon: 'ai' }] : []),
    ] as { route: Route; icon: string }[],
  );

  $effect(() => {
    if (app.ready && advancedRoutes.includes(app.route) && !showAdvanced) navigate('start');
  });

  const pxState = $derived(
    !app.px ? 'muted' : app.px.portOpen ? 'ok' : app.px.installed ? 'warn' : 'muted',
  );

  /** Ctrl+E shows or hides the advanced tabs (admins only; remembered in the settings). */
  async function onKey(e: KeyboardEvent) {
    if (!(e.ctrlKey && !e.shiftKey && !e.altKey && e.key.toLowerCase() === 'e') || !app.info) return;
    e.preventDefault();
    if (!app.info.admin) {
      toast('info', t('app.advancedNotAllowed'));
      return;
    }
    const shown = await api<boolean>('app.toggleAdvanced').catch(() => null);
    if (shown === null) return;
    app.info.showAdvanced = shown;
    toast('info', shown ? t('app.advancedOn') : t('app.advancedOff'));
  }
</script>

<svelte:window onkeydown={onKey} />

{#if failed}
  <main class="center"><div class="note error"><Icon name="alert" /><p>{failed}</p></div></main>
{:else if !app.ready}
  <main class="center" in:fade><div class="loading"><img src="./favicon.svg" alt="" width="64" height="64" /><p>{t('app.loading')}</p></div></main>
{:else}
  <div class="shell">
    <aside class="sidebar">
      <div class="brand">
        <img src="./favicon.svg" alt="" width="32" height="32" />
        <div>
          <strong>Ember</strong>
          <span class="small muted">{app.info?.teamName ?? t('app.tagline')}</span>
        </div>
      </div>

      <nav aria-label="Ember">
        {#each nav as item (item.route)}
          <a href={'#/' + item.route} class:active={app.route === item.route} aria-current={app.route === item.route ? 'page' : undefined}>
            <Icon name={item.icon} />
            <span>{t(`nav.${item.route}`)}</span>
          </a>
        {/each}
        <div class="sep"></div>
        <a href="#/settings" class:active={app.route === 'settings'}><Icon name="settings" /><span>{t('nav.settings')}</span></a>
        <a href="#/log" class:active={app.route === 'log'}><Icon name="log" /><span>{t('nav.log')}</span></a>
      </nav>

      <div class="foot">
        <a class="px-pill {pxState}" href="#/proxy">
          <span class="dot"></span>
          <span>{app.px?.portOpen ? 'px · ' + app.px.url.replace('http://', '') : app.px?.installed ? t('proxy.px.stopped') : t('proxy.px.notInstalled')}</span>
        </a>
        <span class="small muted">Ember {app.info?.version} · {t(`app.modes.${app.info?.mode ?? 'dev'}`)}</span>
      </div>
    </aside>

    <main class="content">
      {#if !app.native && !new URLSearchParams(location.search).has('clean')}
        <div class="note preview"><Icon name="info" /><p>{t('app.browserPreview')}</p></div>
      {/if}
      <UpdateBanner />
      {#key app.route}
        <div class="page" in:fly={{ x: 18, duration: 220, delay: 60 }} out:fade={{ duration: 60 }}>
          {#if app.route === 'start'}<Start />
          {:else if app.route === 'proxy'}<Proxy />
          {:else if app.route === 'git'}<Git />
          {:else if app.route === 'software'}<Software />
          {:else if app.route === 'power'}<Power />
          {:else if app.route === 'system'}<System />
          {:else if app.route === 'ai'}<Ai />
          {:else if app.route === 'settings'}<Settings />
          {:else if app.route === 'log'}<Log />
          {/if}
        </div>
      {/key}
    </main>
  </div>
{/if}

<Toasts />
<ConfirmDialog />
<MailDialog />

<style>
  .center { height: 100%; display: grid; place-items: center; padding: 24px; }
  .loading { display: flex; flex-direction: column; align-items: center; gap: 10px; color: var(--text-muted); }
  .loading img { animation: glow 1.6s ease-in-out infinite; transform-origin: 50% 90%; }
  @keyframes glow { 50% { opacity: 0.7; transform: scale(0.94, 1.04); } }
  @media (prefers-reduced-motion: reduce) { .loading img { animation: none; } }

  .shell { display: grid; grid-template-columns: 232px 1fr; height: 100%; }
  /* The sidebar is the hearth: a soft glow behind Calcifer, fading out further down. */
  .sidebar {
    background: radial-gradient(ellipse 150% 38% at 12% 0%, var(--hearth), transparent 72%), var(--sidebar);
    border-right: 1px solid var(--border);
    display: flex; flex-direction: column; padding: 16px 12px; gap: 18px; min-height: 0;
  }
  .brand { display: flex; align-items: center; gap: 10px; padding: 4px 8px; }
  .brand div { display: flex; flex-direction: column; line-height: 1.25; min-width: 0; }
  .brand strong { font-family: var(--font-display); font-size: 17px; }
  .brand span { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  nav { display: flex; flex-direction: column; gap: 2px; overflow-y: auto; }
  nav a {
    display: flex; align-items: center; gap: 10px; padding: 7px 10px; border-radius: var(--radius-small);
    color: var(--text); text-decoration: none; font-weight: 500; position: relative;
  }
  nav a:hover { background: color-mix(in srgb, var(--accent) 7%, transparent); }
  nav a.active { background: var(--accent-soft); }
  nav a.active::before {
    content: ''; position: absolute; left: 0; top: 7px; bottom: 7px; width: 3px; border-radius: 2px; background: var(--ember-up);
  }
  nav a :global(svg) { color: var(--text-muted); }
  nav a.active :global(svg) { color: var(--accent-fg); }
  .sep { height: 1px; background: var(--border); margin: 8px 6px; }
  .foot { margin-top: auto; display: flex; flex-direction: column; gap: 6px; padding: 0 6px; }
  .px-pill {
    display: inline-flex; align-items: center; gap: 8px; padding: 6px 10px; border-radius: 999px;
    background: var(--surface); border: 1px solid var(--border); color: var(--text); text-decoration: none; font-size: 12.5px;
  }
  .px-pill.ok .dot { color: var(--ok); box-shadow: 0 0 0 3px var(--ok-soft); }
  .px-pill.warn .dot { color: var(--warn); }
  .px-pill.muted .dot { color: var(--text-muted); }

  .content { overflow-y: auto; padding: 28px 36px 48px; min-width: 0; }
  .page { max-width: 1120px; }
  .preview { margin-bottom: 18px; }
</style>

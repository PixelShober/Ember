<script lang="ts">
  import { onMount } from 'svelte';
  import { fade } from 'svelte/transition';
  import { api, app, can, canOpen, init, navigate, openMail, run, toast } from './lib/app.svelte';
  import { t } from './lib/i18n.svelte';
  import type { ModuleId, Route } from './lib/types';
  import Icon from './lib/components/Icon.svelte';
  import AppearanceSwitch from './lib/components/AppearanceSwitch.svelte';
  import ivy from './assets/ivy.svg';
  import Toasts from './lib/components/Toasts.svelte';
  import ConfirmDialog from './lib/components/ConfirmDialog.svelte';
  import MailDialog from './lib/components/MailDialog.svelte';
  import UpdateBanner from './lib/components/UpdateBanner.svelte';
  import PreviewSwitch from './lib/components/PreviewSwitch.svelte';
  import Start from './pages/Start.svelte';
  import Proxy from './pages/Proxy.svelte';
  import Git from './pages/Git.svelte';
  import Software from './pages/Software.svelte';
  import Playwright from './pages/Playwright.svelte';
  import Setup from './pages/Setup.svelte';
  import Power from './pages/Power.svelte';
  import System from './pages/System.svelte';
  import Ai from './pages/Ai.svelte';
  import Access from './pages/Access.svelte';
  import Calcifer from './lib/components/Calcifer.svelte';
  import Settings from './pages/Settings.svelte';
  import Log from './pages/Log.svelte';

  let failed = $state<string | null>(null);
  let content = $state<HTMLElement>();

  // A new page starts at the top.
  $effect(() => {
    void app.route;
    content?.scrollTo(0, 0);
  });

  onMount(() => {
    init().catch((e) => (failed = String(e?.message ?? e)));
  });

  // Every tab is a module: the roles in team.json (or the starter key) decide which ones show.
  // KI, Energie and Berechtigungen are advanced: Ctrl+E hides them.
  const modules: { route: ModuleId; icon: string }[] = [
    { route: 'proxy', icon: 'proxy' },
    { route: 'git', icon: 'git' },
    { route: 'software', icon: 'software' },
    { route: 'playwright', icon: 'browser' },
    { route: 'system', icon: 'system' },
    { route: 'power', icon: 'power' },
    { route: 'ai', icon: 'ai' },
    { route: 'access', icon: 'users' },
  ];
  const nav = $derived([{ route: 'start' as Route, icon: 'home' }, ...modules.filter((m) => canOpen(m.route))]);

  $effect(() => {
    if (app.ready && app.info?.access && !canOpen(app.route)) navigate('start');
  });

  /** Lock screen: check team.json again (an admin may just have added this account). */
  let checking = $state(false);
  async function recheck() {
    checking = true;
    await run(() => api('team.reload'));
    checking = false;
    if (!app.info?.access) toast('info', t('access.locked.stillLocked'));
  }

  const pxState = $derived(
    !app.px ? 'muted' : app.px.portOpen ? 'ok' : app.px.installed ? 'warn' : 'muted',
  );

  /** Ctrl+E shows or hides the advanced tabs (whoever has one; remembered in the settings). */
  async function onKey(e: KeyboardEvent) {
    if (!(e.ctrlKey && !e.shiftKey && !e.altKey && e.key.toLowerCase() === 'e') || !app.info?.access) return;
    e.preventDefault();
    if (!app.info.advanced) {
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
{:else if app.info?.locked === 'setup'}
  <Setup />
{:else if app.info && !app.info.access}
  <main class="center" in:fade>
    <div class="locked">
      <Calcifer size={72} />
      <h1>{t('access.locked.title')}</h1>
      <p>{t('access.locked.text', { user: app.info.user })}</p>
      {#if app.info.accessContact}<p class="small muted">{t('access.locked.contact', { email: app.info.accessContact })}</p>{/if}
      <div class="row">
        <button class="btn primary" onclick={() => openMail('accessRequest')}><Icon name="mail" size={16} />{t('access.locked.request')}</button>
        <button class="btn" disabled={checking} onclick={recheck}><Icon name="refresh" size={16} />{t('access.locked.retry')}</button>
      </div>
    </div>
  </main>
{:else}
  <div class="shell">
    <aside class="sidebar" style={`--ivy: url("${ivy}")`}>
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
        {#if can('settings') || can('log')}<div class="sep"></div>{/if}
        {#if can('settings')}<a href="#/settings" class:active={app.route === 'settings'}><Icon name="settings" /><span>{t('nav.settings')}</span></a>{/if}
        {#if can('log')}<a href="#/log" class:active={app.route === 'log'}><Icon name="log" /><span>{t('nav.log')}</span></a>{/if}
      </nav>

      <div class="foot">
        <AppearanceSwitch />
        <PreviewSwitch />
        {#if can('proxy')}
          <a class="px-pill {pxState}" href="#/proxy">
            <span class="dot"></span>
            <span>{app.px?.portOpen ? 'px · ' + app.px.url.replace('http://', '') : app.px?.installed ? t('proxy.px.stopped') : t('proxy.px.notInstalled')}</span>
          </a>
        {/if}
        <span class="small muted">Ember {app.info?.version} · {t(`app.modes.${app.info?.mode ?? 'dev'}`)}</span>
      </div>
    </aside>

    <main class="content" bind:this={content}>
      {#if !app.native && !new URLSearchParams(location.search).has('clean')}
        <div class="note preview"><Icon name="info" /><p>{t('app.browserPreview')}</p></div>
      {/if}
      <UpdateBanner />
      {#key app.route}
        <div class="page">
          {#if app.route === 'start'}<Start />
          {:else if app.route === 'proxy'}<Proxy />
          {:else if app.route === 'git'}<Git />
          {:else if app.route === 'software'}<Software />
          {:else if app.route === 'playwright'}<Playwright />
          {:else if app.route === 'power'}<Power />
          {:else if app.route === 'system'}<System />
          {:else if app.route === 'ai'}<Ai />
          {:else if app.route === 'access'}<Access />
          {:else if app.route === 'settings'}<Settings />
          {:else if app.route === 'log'}<Log />
          {/if}
        </div>
      {/key}
    </main>
  </div>
{/if}

{#if app.ready}<PreviewSwitch bar />{/if}
<Toasts />
<ConfirmDialog />
<MailDialog />

<style>
  .center { height: 100%; display: grid; place-items: center; padding: 24px; }
  .loading { display: flex; flex-direction: column; align-items: center; gap: 10px; color: var(--text-muted); }
  .locked { max-width: 520px; display: flex; flex-direction: column; align-items: center; gap: 12px; text-align: center; }
  .locked h1 { margin: 6px 0 0; font-family: var(--font-display); font-size: 22px; }
  .locked p { margin: 0; }
  .locked .row { margin-top: 10px; justify-content: center; }
  .loading img { animation: glow 1.6s ease-in-out infinite; transform-origin: 50% 90%; }
  @keyframes glow { 50% { opacity: 0.7; transform: scale(0.94, 1.04); } }
  @media (prefers-reduced-motion: reduce) { .loading img { animation: none; } }

  .shell { display: grid; grid-template-columns: 232px 1fr; height: 100%; }
  /* The sidebar is the hearth: a soft glow behind Calcifer, fading out further down. */
  /* Own layers for the page change: the sidebar stays put, the active tab's marker glides (app.css). */
  .sidebar {
    view-transition-name: sidebar;
    background: radial-gradient(ellipse 150% 38% at 12% 0%, var(--hearth), transparent 72%), var(--sidebar);
    border-right: 1px solid var(--border);
    display: flex; flex-direction: column; padding: 16px 12px; gap: 18px; min-height: 0;
    position: relative;
  }
  /* Ivy climbs up the right edge of the sidebar (pixel art like Calcifer), behind the content. */
  .sidebar::after {
    content: ''; position: absolute; right: 0; bottom: 0; width: 42px; height: 58%; pointer-events: none; z-index: 0;
    background: var(--ivy) repeat-y right bottom / 42px 108px;
    -webkit-mask-image: linear-gradient(to top, #000 50%, transparent); mask-image: linear-gradient(to top, #000 50%, transparent);
  }
  .sidebar > :global(*) { position: relative; z-index: 1; }
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
  nav a.active { background: var(--accent-soft); view-transition-name: active-nav; }
  nav a.active::before {
    content: ''; position: absolute; left: 0; top: 7px; bottom: 7px; width: 3px; border-radius: 2px; background: var(--ember-up);
  }
  nav a :global(svg) { color: var(--text-muted); }
  nav a.active :global(svg) { color: var(--accent-fg); }
  .sep { height: 1px; background: var(--border); margin: 8px 6px; }
  .foot { margin-top: auto; display: flex; flex-direction: column; align-items: flex-start; gap: 8px; padding: 0 34px 0 6px; }
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

<script lang="ts">
  // Playwright in three tabs: the open project (browsers, Edge), a new project from the template,
  // and the template itself (which one counts, a new one from a project).
  import { onMount } from 'svelte';
  import { api, app, run } from '../lib/app.svelte';
  import { t } from '../lib/i18n.svelte';
  import type { PlaywrightState, PlaywrightTemplateInfo } from '../lib/types';
  import PlaywrightCard from '../lib/components/PlaywrightCard.svelte';
  import PlaywrightNew from '../lib/components/PlaywrightNew.svelte';
  import PlaywrightTemplateCard from '../lib/components/PlaywrightTemplateCard.svelte';

  const tabs = ['project', 'new', 'template'] as const;
  type Tab = (typeof tabs)[number];

  const stored = (() => {
    try {
      return sessionStorage.getItem('ember.playwrightTab') as Tab | null;
    } catch {
      return null;
    }
  })();
  let tab = $state<Tab>(stored && tabs.includes(stored) ? stored : 'project');
  let pw = $state<PlaywrightState | null>(null);
  let template = $state<PlaywrightTemplateInfo | null>(null);
  // The project tab reloads itself; after "Neues Projekt" it starts fresh with the new project.
  let projectKey = $state(0);

  async function load() {
    [pw, template] = await Promise.all([
      run(() => api<PlaywrightState>('playwright.state')).then((r) => r ?? pw),
      run(() => api<PlaywrightTemplateInfo>('playwright.template')).then((r) => r ?? template),
    ]);
  }
  onMount(load);

  $effect(() => {
    const state = app.jobs['playwright']?.state;
    if (state === 'done' || state === 'failed') void load();
  });

  function show(next: Tab) {
    tab = next;
    try {
      sessionStorage.setItem('ember.playwrightTab', next);
    } catch {
      // private mode: the tab is not remembered
    }
  }

  /** Arrow keys move between the tabs (WAI-ARIA tabs pattern). */
  function onKey(e: KeyboardEvent) {
    const i = tabs.indexOf(tab);
    const next = e.key === 'ArrowRight' ? tabs[(i + 1) % tabs.length] : e.key === 'ArrowLeft' ? tabs[(i + tabs.length - 1) % tabs.length] : null;
    if (!next) return;
    e.preventDefault();
    show(next);
    document.getElementById(`pw-tab-${next}`)?.focus();
  }
</script>

<header class="page-head">
  <h1>{t('playwright.title')}</h1>
</header>

<div class="tabs" role="tablist" aria-label={t('playwright.title')} tabindex="-1" onkeydown={onKey}>
  {#each tabs as id (id)}
    <button id="pw-tab-{id}" role="tab" aria-selected={tab === id} aria-controls="pw-panel" tabindex={tab === id ? 0 : -1} class:active={tab === id} onclick={() => show(id)}>
      {t(`playwright.tabs.${id}`)}
    </button>
  {/each}
</div>

<div id="pw-panel" role="tabpanel" aria-labelledby="pw-tab-{tab}">
  {#if tab === 'project'}
    {#key projectKey}<PlaywrightCard />{/key}
  {:else if tab === 'new'}
    <PlaywrightNew {pw} {template} onopen={() => { projectKey++; show('project'); }} onchange={() => show('template')} />
  {:else}
    <PlaywrightTemplateCard {pw} bind:template />
  {/if}
</div>

<style>
  .tabs {
    display: flex; gap: 4px; margin: -4px 0 16px; border-bottom: 1px solid var(--border);
  }
  .tabs button {
    position: relative; padding: 8px 14px 10px; border: 0; background: none; cursor: pointer;
    font: inherit; font-size: 14px; color: var(--text-muted); border-radius: var(--radius-small) var(--radius-small) 0 0;
    transition: color 150ms ease-out, background-color 150ms ease-out;
  }
  .tabs button:hover { color: var(--text); background: var(--surface-2); }
  .tabs button.active { color: var(--text); font-weight: 600; }
  .tabs button::after {
    content: ''; position: absolute; left: 10px; right: 10px; bottom: -1px; height: 2px; border-radius: 2px;
    background: var(--accent); transform: scaleX(0); transition: transform 200ms cubic-bezier(0.16, 1, 0.3, 1);
  }
  .tabs button.active::after { transform: scaleX(1); }
  .tabs button:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }
  @media (prefers-reduced-motion: reduce) { .tabs button::after { transition: none; } }
</style>

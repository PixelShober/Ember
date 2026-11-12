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
  import Tabs from '../lib/components/Tabs.svelte';

  const tabs = ['project', 'new', 'template'] as const;
  type Tab = (typeof tabs)[number];

  let tab = $state<Tab>('project');
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
  }
</script>

<header class="page-head">
  <h1>{t('playwright.title')}</h1>
</header>

<Tabs {tabs} bind:value={tab} labelKey="playwright.tabs" id="pw" />

<div id="pw-panel" role="tabpanel" aria-labelledby="pw-tab-{tab}">
  {#if tab === 'project'}
    {#key projectKey}<PlaywrightCard />{/key}
  {:else if tab === 'new'}
    <PlaywrightNew {pw} {template} onopen={() => { projectKey++; show('project'); }} onchange={() => show('template')} />
  {:else}
    <PlaywrightTemplateCard {pw} bind:template />
  {/if}
</div>


<script lang="ts">
  // Playwright browsers for one project: Ember runs "npx playwright install" in the project
  // folder, through px and with the company certificates, so the browsers match its version.
  import { onMount } from 'svelte';
  import { api, app, copyText, run, toast } from '../app.svelte';
  import { t } from '../i18n.svelte';
  import type { PlaywrightState } from '../types';
  import Icon from './Icon.svelte';
  import Calcifer from './Calcifer.svelte';
  import JobView from './JobView.svelte';

  let pw = $state<PlaywrightState | null>(null);
  let browsers = $state<'chromium' | 'all'>('chromium');
  const edgeSnippet = "use: { channel: 'msedge' }";

  async function load() {
    pw = (await run(() => api<PlaywrightState>('playwright.state'))) ?? pw;
  }
  onMount(load);

  $effect(() => {
    if (app.jobs['playwright']?.state === 'done') void load();
  });

  async function pick() {
    const dir = await api<string | null>('app.pickFolder', { title: t('playwright.project'), initial: pw?.project?.dir ?? null });
    if (dir) pw = (await run(() => api<PlaywrightState>('playwright.inspect', { dir }))) ?? pw;
  }

  const project = $derived(pw?.project ?? null);
  const ready = $derived(!!project?.package && !!pw?.nodeFound);
  const running = $derived(app.jobs['playwright']?.state === 'running');
</script>

<section class="card">
  <div class="card-head">
    <Icon name="play" />
    <div class="grow"><h2>{t('playwright.title')}</h2><p class="small muted">{t('playwright.subtitle')}</p></div>
  </div>

  <div class="row pick">
    <code class="path">{project?.dir ?? '–'}</code>
    <button class="btn small" onclick={pick}><Icon name="folder" size={14} />{t('playwright.pick')}</button>
  </div>

  {#if !project}
    <div class="note"><Calcifer /><p>{t('playwright.noProject')}</p></div>
  {:else if !project.hasPackageJson}
    <div class="note warn"><Calcifer /><p>{t('playwright.noPackageJson')}</p></div>
  {:else if !project.package}
    <div class="note warn"><Calcifer /><p>{t('playwright.notInProject')}</p></div>
  {:else}
    <p class="row small">
      <span class="badge ok"><Icon name="check" size={12} />{project.version ? t('playwright.found', { package: project.package, version: project.version }) : t('playwright.foundWanted', { package: project.package, wanted: project.wanted ?? '?' })}</span>
      {#if pw?.downloadHost}<span class="badge info">{t('playwright.mirror', { host: pw.downloadHost })}</span>{/if}
    </p>
  {/if}

  {#if pw && !pw.nodeFound}
    <div class="note warn"><Calcifer /><p>{t('errors.playwrightNoNode')}</p></div>
  {:else if pw && !pw.nodeCerts}
    <div class="note"><Icon name="info" /><p class="small">{t('playwright.certsMissing')}</p></div>
  {/if}

  <div class="row options">
    <label class="check"><input type="radio" name="pw-browsers" value="chromium" bind:group={browsers} />{t('playwright.browsersChromium')}</label>
    <label class="check"><input type="radio" name="pw-browsers" value="all" bind:group={browsers} />{t('playwright.browsersAll')}</label>
    <span class="spacer"></span>
    <button class="btn primary small" disabled={!ready || running} onclick={() => run(() => api('playwright.install', { dir: project!.dir, browsers }))}>
      <Icon name="download" size={14} />{t('playwright.install')}
    </button>
  </div>
  <JobView id="playwright" />

  <div class="cache">
    <div class="row between">
      <h3>{t('playwright.cache')}</h3>
      <button class="btn ghost small" onclick={() => run(() => api('app.openFolder', { key: 'playwright' }))}><Icon name="folder" size={14} />{t('playwright.openCache')}</button>
    </div>
    {#if pw?.browsers.length}
      <p class="row">{#each pw.browsers as b (b.name)}<span class="badge">{b.name}</span>{/each}</p>
    {:else}
      <p class="small muted">{t('playwright.cacheEmpty')}</p>
    {/if}
  </div>

  <div class="note edge">
    <Icon name="info" />
    <div>
      <p class="small"><strong>{t('playwright.edgeTitle')}</strong> · {t('playwright.edgeText')}</p>
      <p class="row"><code>{edgeSnippet}</code>
        <button class="btn ghost small" onclick={async () => { await copyText(edgeSnippet); toast('ok', t('common.copied')); }}><Icon name="copy" size={13} /></button></p>
    </div>
  </div>
</section>

<style>
  .grow { flex: 1; min-width: 260px; display: flex; flex-direction: column; gap: 3px; }
  .pick { margin-bottom: 10px; }
  .path { flex: 1; min-width: 200px; padding: 6px 10px; background: var(--surface-2); border-radius: var(--radius-small); overflow-wrap: anywhere; }
  .options { margin: 12px 0 6px; }
  .cache { margin-top: 14px; padding-top: 12px; border-top: 1px solid var(--border); display: flex; flex-direction: column; gap: 6px; }
  .edge { margin-top: 12px; }
</style>

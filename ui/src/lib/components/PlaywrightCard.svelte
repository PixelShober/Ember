<script lang="ts">
  // Playwright for one project: browsers through px and with the company certificates, Playwright
  // added to a project that has none yet, and the installed Edge as the way out when the browser
  // download is blocked. New projects and templates have their own tabs.
  import { onMount } from 'svelte';
  import { slide } from 'svelte/transition';
  import { api, app, confirm, copyText, run, toast } from '../app.svelte';
  import { t } from '../i18n.svelte';
  import type { PlaywrightEdgeSetup, PlaywrightMode, PlaywrightState } from '../types';
  import Icon from './Icon.svelte';
  import Calcifer from './Calcifer.svelte';
  import JobView from './JobView.svelte';
  import BrowserChoice from './BrowserChoice.svelte';

  let pw = $state<PlaywrightState | null>(null);
  let mode = $state<PlaywrightMode>('chromium');
  const librarySnippet = "chromium.launch({ channel: 'msedge' })";

  async function load() {
    pw = (await run(() => api<PlaywrightState>('playwright.state'))) ?? pw;
  }
  onMount(load);

  $effect(() => {
    const state = app.jobs['playwright']?.state;
    if (state === 'done' || state === 'failed') void load();
  });

  async function pick() {
    const dir = await api<string | null>('app.pickFolder', { title: t('playwright.project'), initial: pw?.project?.dir ?? null });
    if (dir) pw = (await run(() => api<PlaywrightState>('playwright.inspect', { dir }))) ?? pw;
  }

  const project = $derived(pw?.project ?? null);
  const hasPlaywright = $derived(!!project?.package);
  const isLibrary = $derived(!!project?.package && project.package !== '@playwright/test');
  const running = $derived(app.jobs['playwright']?.state === 'running');

  // When the download step (or the check after it) failed, the installed Edge is the way out.
  const downloadFailed = $derived.by(() => {
    const job = app.jobs['playwright'];
    return !!job && job.state === 'failed' && job.failedIndex >= job.steps.length - 2 && !!pw?.edgeFound && !isLibrary;
  });

  async function install() {
    if (!project) return;
    if (!hasPlaywright) {
      const text = project.hasPackageJson ? t('playwright.addConfirm', { path: project.dir }) : t('playwright.addConfirmInit', { path: project.dir });
      if (!(await confirm(t('playwright.add'), text, t('playwright.add')))) return;
    }
    await run(() => api('playwright.install', { dir: project.dir, browsers: mode, add: !hasPlaywright }));
  }

  async function useEdge() {
    if (!project) return;
    const setup = await run(() => api<PlaywrightEdgeSetup>('playwright.useEdge', { dir: project.dir }));
    if (!setup) return;
    toast('ok', t(setup.created ? 'playwright.edgeCreated' : 'playwright.edgeExisting', { file: setup.file, command: setup.command }));
    mode = 'msedge';
    await load();
  }

  const actionLabel = $derived(!hasPlaywright ? t('playwright.add') : mode === 'msedge' ? t('playwright.setupEdge') : t('playwright.install'));
</script>

<section class="card" data-tour="pw-project">
  <div class="card-head">
    <Icon name="folder" />
    <div class="grow"><h2>{t('playwright.project')}</h2></div>
  </div>

  <div class="row pick">
    <code class="path">{project?.dir ?? '–'}</code>
    <button class="btn small" onclick={pick}><Icon name="folder" size={14} />{t('playwright.pick')}</button>
    {#if project}
      <button class="btn small ghost icon-only" title={t('playwright.openProject')} aria-label={t('playwright.openProject')} onclick={() => run(() => api('app.openFolder', { key: 'playwrightProject' }))}><Icon name="external" size={15} /></button>
      {#if pw?.vsCodeFound}
        <button class="btn small ghost" onclick={() => run(() => api('playwright.openInCode', { dir: project.dir }))}><Icon name="terminal" size={14} />{t('playwright.openInCode')}</button>
      {/if}
    {/if}
  </div>

  {#if !project}
    <div class="note"><Calcifer /><p>{t('playwright.noProject')}</p></div>
  {:else if !project.hasPackageJson}
    <div class="note"><Calcifer /><p>{t('playwright.noPackageJson')}</p></div>
  {:else if !project.package}
    <div class="note"><Calcifer /><p>{t('playwright.notInProject')}</p></div>
  {:else}
    <p class="row small">
      <span class="badge ok"><Icon name="check" size={12} />{project.version ? t('playwright.found', { package: project.package, version: project.version }) : t('playwright.foundWanted', { package: project.package, wanted: project.wanted ?? '?' })}</span>
      {#if project.config}<span class="badge">{t('playwright.config', { file: project.config })}</span>{/if}
      {#if project.usesEdge}<span class="badge info">{t('playwright.usesEdge')}</span>{/if}
      {#if pw?.downloadHost}<span class="badge info">{t('playwright.mirror', { host: pw.downloadHost })}</span>{/if}
    </p>
  {/if}

  {#if pw && !pw.nodeFound}
    <div class="note warn"><Calcifer /><p>{t('errors.playwrightNoNode')}</p></div>
  {:else if pw && !pw.nodeCerts && mode !== 'msedge'}
    <div class="note"><Icon name="info" /><p class="small">{t('playwright.certsMissing')}</p></div>
  {/if}

  <BrowserChoice bind:mode edgeFound={pw?.edgeFound} name="pw-browsers" />

  <div class="row actions">
    <span class="spacer"></span>
    <button class="btn primary small" disabled={!project || running || !pw?.nodeFound} onclick={install}>
      <Icon name="download" size={14} />{actionLabel}
    </button>
  </div>
  <JobView id="playwright" />

  {#if downloadFailed}
    <div class="note warn fallback" transition:slide={{ duration: 180 }}>
      <Calcifer />
      <div class="grow">
        <p><strong>{t('playwright.edgeFallbackTitle')}</strong> {t('playwright.edgeFallback')}</p>
        <p><button class="btn small primary" onclick={useEdge}><Icon name="check" size={14} />{t('playwright.useEdge')}</button></p>
      </div>
    </div>
  {/if}

  {#if isLibrary}
    <div class="note edge">
      <Icon name="info" />
      <div>
        <p class="small"><strong>{t('playwright.edgeTitle')}</strong> · {t('playwright.edgeText')}</p>
        <p class="row"><code>{librarySnippet}</code>
          <button class="btn ghost small" aria-label={t('common.copy')} onclick={async () => { await copyText(librarySnippet); toast('ok', t('common.copied')); }}><Icon name="copy" size={13} /></button></p>
      </div>
    </div>
  {/if}

  <div class="cache">
    <div class="row between">
      <h3>{t('playwright.cache')}</h3>
      <button class="btn ghost small" onclick={() => run(() => api('app.openFolder', { key: 'playwright' }))}><Icon name="folder" size={14} />{t('playwright.openCache')}</button>
    </div>
    {#if pw?.browsers.length}
      <p class="row">
        {#each [...new Set(pw.browsers.filter((b) => b.label).map((b) => b.label))] as label (label)}
          <span class="badge" title={pw.browsers.filter((b) => b.label === label).map((b) => b.name).join(', ')}>{label}</span>
        {/each}
      </p>
      {#if pw.browsers.some((b) => !b.label)}<p class="small muted">{t('playwright.helpers')}</p>{/if}
    {:else}
      <p class="small muted">{t('playwright.cacheEmpty')}</p>
    {/if}
  </div>
</section>

<style>
  .grow { flex: 1; min-width: 240px; display: flex; flex-direction: column; gap: 3px; }
  .pick { margin-bottom: 10px; }
  .path { flex: 1; min-width: 200px; padding: 6px 10px; background: var(--surface-2); border-radius: var(--radius-small); overflow-wrap: anywhere; }
  .icon-only { padding: 3px 7px; }
  .actions { margin: 8px 0 6px; }
  .fallback { margin-top: 10px; }
  .fallback p + p { margin-top: 8px; }
  .cache { margin-top: 14px; padding-top: 12px; border-top: 1px solid var(--border); display: flex; flex-direction: column; gap: 6px; }
  .edge { margin-top: 12px; }
</style>

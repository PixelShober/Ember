<script lang="ts">
  // Playwright for one project: browsers through px and with the company certificates, Playwright
  // added to a project that has none yet, a new project from the Ember template, and the installed
  // Edge as the way out when the browser download is blocked.
  import { onMount } from 'svelte';
  import { slide } from 'svelte/transition';
  import { api, app, confirm, copyText, run, toast } from '../app.svelte';
  import { t } from '../i18n.svelte';
  import type { PlaywrightEdgeSetup, PlaywrightMode, PlaywrightState } from '../types';
  import Icon from './Icon.svelte';
  import Calcifer from './Calcifer.svelte';
  import JobView from './JobView.svelte';

  let pw = $state<PlaywrightState | null>(null);
  let mode = $state<PlaywrightMode>('chromium');
  let creating = $state(false);
  let parent = $state('');
  let rawName = $state('');
  const librarySnippet = "chromium.launch({ channel: 'msedge' })";

  async function load() {
    pw = (await run(() => api<PlaywrightState>('playwright.state'))) ?? pw;
    if (pw && !parent) parent = pw.defaultParent;
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

  async function pickParent() {
    const dir = await api<string | null>('app.pickFolder', { title: t('playwright.create.parent'), initial: parent || null });
    if (dir) parent = dir;
  }

  /** npm names: lower case, umlauts spelled out, everything else becomes a hyphen. */
  const slug = (text: string) =>
    text.trim().toLowerCase()
      .replace(/ä/g, 'ae').replace(/ö/g, 'oe').replace(/ü/g, 'ue').replace(/ß/g, 'ss')
      .normalize('NFD').replace(/[̀-ͯ]/g, '')
      .replace(/[^a-z0-9._-]+/g, '-').replace(/-{2,}/g, '-')
      .replace(/^[-._]+/, '').replace(/[-.]+$/, '')
      .slice(0, 214);
  const name = $derived(slug(rawName));
  const target = $derived(parent && name ? `${parent.replace(/[\\/]+$/, '')}\\${name}` : '');

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

  async function create() {
    if (!name || !parent) return;
    const job = await run(() => api('playwright.create', { parent, name, browsers: mode }));
    if (job !== undefined) {
      creating = false;
      rawName = '';
    }
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

<section class="card">
  <div class="card-head">
    <Icon name="play" />
    <div class="grow"><h2>{t('playwright.title')}</h2><p class="small muted">{t('playwright.subtitle')}</p></div>
    {#if !creating}
      <button class="btn small" disabled={running} onclick={() => (creating = true)}><Icon name="plus" size={14} />{t('playwright.newProject')}</button>
    {/if}
  </div>

  {#if creating}
    <div class="create" transition:slide={{ duration: 180 }}>
      <h3>{t('playwright.create.title')}</h3>
      <div class="field">
        <span class="small muted">{t('playwright.create.parent')}</span>
        <div class="row pick">
          <code class="path">{parent || '–'}</code>
          <button class="btn small" onclick={pickParent}><Icon name="folder" size={14} />{t('playwright.pick')}</button>
        </div>
      </div>
      <label class="field">
        <span class="small muted">{t('playwright.create.name')}</span>
        <input type="text" bind:value={rawName} placeholder={t('playwright.create.namePlaceholder')} spellcheck="false" autocomplete="off" />
      </label>
      <p class="small muted">{target ? t('playwright.create.target', { path: target }) : t('playwright.create.nameRule')}</p>
      <p class="small muted">{t('playwright.create.files')}</p>
    </div>
  {:else}
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
  {/if}

  {#if pw && !pw.nodeFound}
    <div class="note warn"><Calcifer /><p>{t('errors.playwrightNoNode')}</p></div>
  {:else if pw && !pw.nodeCerts && mode !== 'msedge'}
    <div class="note"><Icon name="info" /><p class="small">{t('playwright.certsMissing')}</p></div>
  {/if}

  <fieldset class="options">
    <legend class="small muted">{t('playwright.browsersLabel')}</legend>
    <label class="check"><input type="radio" name="pw-browsers" value="chromium" bind:group={mode} />{t('playwright.browsersChromium')}</label>
    <label class="check"><input type="radio" name="pw-browsers" value="all" bind:group={mode} />{t('playwright.browsersAll')}</label>
    <label class="check" title={pw?.edgeFound === false ? t('playwright.edgeMissing') : undefined}>
      <input type="radio" name="pw-browsers" value="msedge" bind:group={mode} disabled={pw?.edgeFound === false} />{t('playwright.browsersEdge')}
    </label>
  </fieldset>

  <div class="row actions">
    <span class="spacer"></span>
    {#if creating}
      <button class="btn small ghost" onclick={() => (creating = false)}>{t('common.cancel')}</button>
      <button class="btn primary small" disabled={!name || !parent || running || !pw?.nodeFound} onclick={create}><Icon name="plus" size={14} />{t('playwright.create.submit')}</button>
    {:else}
      <button class="btn primary small" disabled={!project || running || !pw?.nodeFound} onclick={install}>
        <Icon name="download" size={14} />{actionLabel}
      </button>
    {/if}
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
      <p class="row">{#each pw.browsers as b (b.name)}<span class="badge">{b.name}</span>{/each}</p>
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
  .create { display: flex; flex-direction: column; gap: 8px; padding: 12px 14px; margin-bottom: 4px; border-radius: var(--radius-small); background: var(--surface-2); }
  .create .path { background: var(--surface); }
  .create .pick { margin-bottom: 0; }
  .field { display: flex; flex-direction: column; gap: 4px; }
  .field input { max-width: 420px; }
  .options { border: 0; padding: 0; margin: 12px 0 0; display: flex; flex-wrap: wrap; gap: 6px 18px; }
  .options legend { padding: 0; margin-bottom: 4px; width: 100%; }
  .actions { margin: 8px 0 6px; }
  .fallback { margin-top: 10px; }
  .fallback p + p { margin-top: 8px; }
  .cache { margin-top: 14px; padding-top: 12px; border-top: 1px solid var(--border); display: flex; flex-direction: column; gap: 6px; }
  .edge { margin-top: 12px; }
</style>

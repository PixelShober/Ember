<script lang="ts">
  // A new Playwright project from the template, the way Visual Studio does it: folder and name,
  // a copy of the template with $projectname$ filled in, then the Playwright package and the browsers.
  import { api, app, run } from '../app.svelte';
  import { t } from '../i18n.svelte';
  import type { PlaywrightMode, PlaywrightState, PlaywrightTemplateInfo } from '../types';
  import Icon from './Icon.svelte';
  import Calcifer from './Calcifer.svelte';
  import JobView from './JobView.svelte';
  import BrowserChoice from './BrowserChoice.svelte';

  let { pw, template, onopen, onchange }: {
    pw: PlaywrightState | null;
    template: PlaywrightTemplateInfo | null;
    /** Go to the Projekt tab with the new project. */
    onopen: () => void;
    /** Go to the Vorlage tab. */
    onchange: () => void;
  } = $props();

  let mode = $state<PlaywrightMode>('chromium');
  let parent = $state('');
  let rawName = $state('');
  let builtinOnce = $state(false);
  let created = $state<string | null>(null);

  $effect(() => {
    if (pw && !parent) parent = pw.defaultParent;
  });

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

  const job = $derived(app.jobs['playwright']);
  const running = $derived(job?.state === 'running');
  const usable = $derived(!!template && template.source !== 'builtin' && !template.problem);
  const fromBuiltin = $derived(!usable || builtinOnce);
  const builtinFiles = ['package.json', 'playwright.config.ts', 'tests/example.spec.ts', '.gitignore', '.vscode/extensions.json', 'README.md'];
  const files = $derived(fromBuiltin ? builtinFiles : (template?.sample ?? []));
  const more = $derived(fromBuiltin ? 0 : Math.max(0, (template?.files ?? 0) - files.length));

  async function create() {
    if (!name || !parent) return;
    const started = await run(() => api('playwright.create', { parent, name, browsers: mode, template: fromBuiltin ? 'builtin' : null }));
    if (started !== undefined) {
      created = target;
      rawName = '';
    }
  }

  const label = (source: PlaywrightTemplateInfo['source']) => t(`playwright.template.sources.${source}`);
</script>

<section class="card stack">
  <div class="source" class:muted-source={fromBuiltin}>
    <Icon name="software" size={18} />
    <div class="grow">
      <span class="small muted">{t('playwright.newTab.from')}</span>
      <strong>{fromBuiltin ? label('builtin') : template?.name || label(template!.source)}</strong>
      {#if !fromBuiltin && template?.path}<code class="small">{template.path}</code>{/if}
    </div>
    {#if !fromBuiltin && template}<span class="badge">{label(template.source)}</span>{/if}
    <button class="btn small ghost" onclick={onchange}>{t('playwright.template.change')}</button>
  </div>
  {#if template?.problem}
    <div class="note warn"><Calcifer /><p class="small">{t('playwright.template.problem', { problem: template.problem })}</p></div>
  {/if}

  <div class="form">
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
      <span class="small muted">{target ? t('playwright.create.target', { path: target }) : t('playwright.create.nameRule')}</span>
    </label>
  </div>

  <div class="what">
    <span class="small muted">{t('playwright.newTab.what')}</span>
    <ul class="files">
      {#each files as f (f)}<li><Icon name={f.includes('.') ? 'log' : 'folder'} size={13} /><span>{f}</span></li>{/each}
      {#if more}<li class="more small muted">{t('playwright.newTab.andMore', { count: more })}</li>{/if}
    </ul>
    {#if !fromBuiltin}<p class="small muted">{t('playwright.newTab.placeholders')}</p>{/if}
    {#if usable}
      <label class="check small"><input type="checkbox" bind:checked={builtinOnce} />{t('playwright.newTab.builtinOnce')}</label>
    {/if}
  </div>

  {#if pw && !pw.nodeFound}
    <div class="note warn"><Calcifer /><p>{t('errors.playwrightNoNode')}</p></div>
  {/if}

  <BrowserChoice bind:mode edgeFound={pw?.edgeFound} name="pw-new-browsers" />

  <div class="row actions">
    {#if created && job?.state === 'done'}
      <span class="badge ok"><Icon name="check" size={12} />{t('playwright.newTab.done')}</span>
      <button class="btn small ghost" onclick={onopen}>{t('playwright.newTab.openProject')}</button>
    {/if}
    <span class="spacer"></span>
    <button class="btn primary small" disabled={!name || !parent || running || !pw?.nodeFound} onclick={create}><Icon name="plus" size={14} />{t('playwright.create.submit')}</button>
  </div>
  <JobView id="playwright" />
</section>

<style>
  .source {
    display: flex; align-items: center; gap: 12px; padding: 10px 12px;
    border-radius: var(--radius-small); background: var(--accent-soft);
  }
  .source > :global(svg) { color: var(--accent-fg); flex: none; }
  .source.muted-source { background: var(--surface-2); }
  .source.muted-source > :global(svg) { color: var(--text-muted); }
  .source .grow { flex: 1; min-width: 0; display: flex; flex-wrap: wrap; align-items: baseline; gap: 2px 8px; }
  .source code { flex-basis: 100%; overflow-wrap: anywhere; color: var(--text-muted); }
  .form { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 12px 20px; }
  .field { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
  .pick { flex-wrap: nowrap; }
  .path { flex: 1; min-width: 0; padding: 6px 10px; background: var(--surface-2); border-radius: var(--radius-small); overflow-wrap: anywhere; }
  .what { display: flex; flex-direction: column; gap: 6px; }
  .files {
    list-style: none; margin: 0; padding: 8px 10px; display: grid; grid-template-columns: repeat(auto-fill, minmax(min(220px, 100%), 1fr)); gap: 2px 16px;
    border: 1px dashed var(--border-strong); border-radius: var(--radius-small);
  }
  .files li { display: flex; align-items: center; gap: 6px; font-family: var(--mono); font-size: 12.5px; min-width: 0; }
  .files li span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .files li :global(svg) { color: var(--text-muted); flex: none; }
  .files .more { font-family: var(--font); }
  .actions { margin-top: 4px; }
  .spacer { flex: 1; }
  @media (max-width: 760px) { .form { grid-template-columns: 1fr; } }
</style>

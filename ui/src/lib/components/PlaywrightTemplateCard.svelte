<script lang="ts">
  // The project template: which one counts (team.json, own folder or Ember's), and a new one made
  // from an existing project. Ember suggests per file what goes in; tests, data and credentials stay
  // out, their folders stay as structure. Saving replaces the template after a backup.
  import { slide } from 'svelte/transition';
  import { api, confirm, run, toast } from '../app.svelte';
  import { t } from '../i18n.svelte';
  import { formatDate } from '../format';
  import type { PlaywrightState, PlaywrightTemplateInfo, TemplateEntry } from '../types';
  import Icon from './Icon.svelte';
  import Calcifer from './Calcifer.svelte';

  let { pw, template = $bindable() }: { pw: PlaywrightState | null; template: PlaywrightTemplateInfo | null } = $props();

  let project = $state('');
  let entries = $state<TemplateEntry[] | null>(null);
  let keep = $state<Set<string>>(new Set());
  let name = $state('');
  let description = $state('');
  let target = $state('');
  let busy = $state(false);

  $effect(() => {
    if (!project && pw?.project?.hasPackageJson) void preview(pw.project.dir);
  });

  const fromTeam = $derived(template?.source === 'team');
  const label = (source: PlaywrightTemplateInfo['source']) => t(`playwright.template.sources.${source}`);

  async function choose() {
    const path = await api<string | null>('app.pickFolder', { title: t('playwright.template.pickTitle'), initial: template?.path ?? null });
    if (!path) return;
    template = (await run(() => api<PlaywrightTemplateInfo>('playwright.template.use', { path }))) ?? template;
  }

  async function useBuiltin() {
    template = (await run(() => api<PlaywrightTemplateInfo>('playwright.template.use', { path: null }))) ?? template;
  }

  async function pickProject() {
    const dir = await api<string | null>('app.pickFolder', { title: t('playwright.template.pickProject'), initial: project || pw?.project?.dir || null });
    if (dir) await preview(dir);
  }

  async function preview(dir: string) {
    project = dir;
    const list = await run(() => api<TemplateEntry[]>('playwright.template.preview', { dir }));
    entries = list ?? null;
    keep = new Set((list ?? []).filter((e) => !e.dir && e.action === 'keep').map((e) => e.path));
    if (!name) name = dir.split(/[\\/]/).filter(Boolean).pop() ?? '';
  }

  async function pickTarget() {
    const dir = await api<string | null>('app.pickFolder', { title: t('playwright.template.pickTitle'), initial: target || null });
    if (dir) target = dir;
  }

  // ---- the file list: folders as structure, files with a checkbox, never-ever items folded away
  const listed = $derived((entries ?? []).filter((e) => e.action !== 'skip'));
  const skipped = $derived((entries ?? []).filter((e) => e.action === 'skip' && (e.dir || !e.path.includes('/'))).map((e) => e.path));
  const files = $derived(listed.filter((e) => !e.dir));
  const keptCount = $derived(files.filter((e) => keep.has(e.path)).length);
  const depth = (path: string) => path.split('/').length - 1;
  const leaf = (path: string) => path.split('/').pop() ?? path;
  const under = (dir: string) => files.filter((f) => f.path.startsWith(dir + '/'));

  function toggle(path: string, on: boolean) {
    const next = new Set(keep);
    if (on) next.add(path); else next.delete(path);
    keep = next;
  }

  function toggleDir(dir: string, on: boolean) {
    const next = new Set(keep);
    for (const f of under(dir)) if (on) next.add(f.path); else next.delete(f.path);
    keep = next;
  }

  const dirState = (dir: string) => {
    const inside = under(dir);
    const n = inside.filter((f) => keep.has(f.path)).length;
    return { all: inside.length > 0 && n === inside.length, some: n > 0 && n < inside.length, empty: inside.length === 0 };
  };

  const destination = $derived(fromTeam || template?.source === 'user' ? template?.path ?? '' : target);
  const replacing = $derived(!!destination && destination === template?.path && template.files > 0);

  async function save() {
    if (!project || !destination) return;
    if (replacing && !(await confirm(t('playwright.template.replace'), t('playwright.template.replaceConfirm', { path: destination }), t('playwright.template.replace')))) return;
    busy = true;
    const r = await run(() => api<{ count: number; template: PlaywrightTemplateInfo }>('playwright.template.save', {
      dir: project, keep: [...keep], name: name.trim() || null, description: description.trim() || null, target: fromTeam ? null : destination,
    }));
    busy = false;
    if (!r) return;
    template = r.template;
    toast('ok', t('playwright.template.savedToast', { count: r.count }));
  }
</script>

<!-- current template -->
<section class="card stack tight">
  <div class="card-head">
    <Icon name="software" />
    <div class="grow"><h2>{t('playwright.template.current')}</h2></div>
    {#if template}<span class="badge {template.source === 'builtin' ? '' : 'info'}">{label(template.source)}</span>{/if}
  </div>
  {#if template}
    {#if template.source === 'builtin'}
      <p class="small muted">{t('playwright.template.builtinText')}</p>
    {:else}
      <div class="current">
        <strong>{template.name || template.path?.split(/[\\/]/).pop()}</strong>
        {#if template.description}<p class="small">{template.description}</p>{/if}
        <code class="path">{template.path}</code>
        <p class="small muted">
          {t('playwright.template.files', { count: template.files })}
          {#if template.createdBy && template.created} · {t('playwright.template.by', { user: template.createdBy, date: formatDate(template.created) })}{/if}
        </p>
      </div>
      {#if fromTeam}<p class="small muted">{t('playwright.template.fromTeam')}</p>{/if}
      {#if template.problem}<div class="note warn"><Calcifer /><p class="small">{t('playwright.template.problem', { problem: template.problem })}</p></div>{/if}
    {/if}
    <div class="row">
      {#if !fromTeam}
        <button class="btn small" onclick={choose}><Icon name="folder" size={14} />{t('playwright.template.choose')}</button>
        {#if template.source === 'user'}<button class="btn small ghost" onclick={useBuiltin}>{t('playwright.template.useBuiltin')}</button>{/if}
      {/if}
      {#if template.path && !template.problem}
        <button class="btn small ghost" onclick={() => run(() => api('app.openFolder', { key: 'playwrightTemplate' }))}><Icon name="external" size={14} />{t('playwright.template.open')}</button>
      {/if}
    </div>
  {/if}
</section>

<!-- a template from a project -->
<section class="card stack">
  <div class="card-head">
    <Icon name="plus" />
    <div class="grow"><h2>{t('playwright.template.make')}</h2><p class="small muted">{t('playwright.template.makeHint')}</p></div>
  </div>

  <div class="field">
    <span class="small muted">{t('playwright.template.source')}</span>
    <div class="row pick">
      <code class="path">{project || '–'}</code>
      <button class="btn small" onclick={pickProject}><Icon name="folder" size={14} />{t('playwright.template.pickProject')}</button>
    </div>
  </div>

  {#if entries}
    <div class="tree-head small">
      <span class="muted">{t('playwright.template.kept', { kept: keptCount, dropped: files.length - keptCount })}</span>
    </div>
    <ul class="tree" transition:slide={{ duration: 160 }}>
      {#each listed as e (e.path)}
        {#if e.dir}
          {@const st = dirState(e.path)}
          <li class="dir" style={`--depth: ${depth(e.path)}`}>
            <label>
              <input type="checkbox" checked={st.all} indeterminate={st.some} disabled={st.empty} onchange={(ev) => toggleDir(e.path, ev.currentTarget.checked)} />
              <Icon name="folder" size={14} /><span class="name">{leaf(e.path)}/</span>
            </label>
            {#if e.reason}<span class="reason">{t(`playwright.template.reasons.${e.reason}`)}</span>{/if}
          </li>
        {:else}
          <li class:off={!keep.has(e.path)} style={`--depth: ${depth(e.path)}`}>
            <label>
              <input type="checkbox" checked={keep.has(e.path)} onchange={(ev) => toggle(e.path, ev.currentTarget.checked)} />
              <span class="name">{leaf(e.path)}</span>
            </label>
            {#if e.reason}<span class="reason {e.reason}">{t(`playwright.template.reasons.${e.reason}`)}</span>{/if}
          </li>
        {/if}
      {/each}
    </ul>
    {#if skipped.length}<p class="small muted">{t('playwright.template.always', { list: skipped.join(', ') })}</p>{/if}
    <p class="small muted">{t('playwright.template.scrubbed')}</p>

    <div class="form">
      <label class="field"><span class="small muted">{t('playwright.template.name')}</span><input bind:value={name} placeholder={t('playwright.template.namePlaceholder')} /></label>
      <label class="field"><span class="small muted">{t('playwright.template.description')}</span><input bind:value={description} placeholder={t('playwright.template.descriptionPlaceholder')} /></label>
    </div>

    <div class="field">
      <span class="small muted">{t('playwright.template.target')}</span>
      {#if fromTeam || template?.source === 'user'}
        <code class="path">{destination}</code>
      {:else}
        <div class="row pick">
          <code class="path">{target || '–'}</code>
          <button class="btn small" onclick={pickTarget}><Icon name="folder" size={14} />{t('playwright.template.choose')}</button>
        </div>
        {#if !target}<span class="small muted">{t('playwright.template.targetNone')}</span>{/if}
      {/if}
    </div>

    <div class="row actions">
      <span class="spacer"></span>
      <button class="btn primary small" disabled={busy || !destination || keptCount === 0 || (fromTeam && template?.writable === false)} onclick={save}>
        <Icon name={replacing ? 'refresh' : 'check'} size={14} />{replacing ? t('playwright.template.replace') : t('playwright.template.create')}
      </button>
    </div>
  {:else}
    <p class="small muted">{t('playwright.template.emptyPreview')}</p>
  {/if}
</section>

<style>
  section + section { margin-top: 16px; }
  .grow { flex: 1; min-width: 200px; display: flex; flex-direction: column; gap: 3px; }
  .current { display: flex; flex-direction: column; gap: 4px; }
  .current p { margin: 0; }
  .field { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
  .pick { flex-wrap: nowrap; }
  .path { flex: 1; min-width: 0; padding: 6px 10px; background: var(--surface-2); border-radius: var(--radius-small); overflow-wrap: anywhere; }
  .tree-head { display: flex; justify-content: space-between; }
  .tree {
    list-style: none; margin: 0; padding: 6px; max-height: 360px; overflow-y: auto;
    border: 1px solid var(--border); border-radius: var(--radius-small); background: var(--surface);
  }
  .tree li {
    display: flex; align-items: center; gap: 8px; padding: 3px 8px 3px calc(8px + var(--depth) * 18px);
    border-radius: 6px; font-family: var(--mono); font-size: 12.5px;
  }
  .tree li:hover { background: var(--surface-2); }
  .tree label { display: flex; align-items: center; gap: 7px; flex: 1; min-width: 0; cursor: pointer; }
  .tree .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .tree .dir { color: var(--text); font-weight: 600; }
  .tree .dir :global(svg) { color: var(--text-muted); flex: none; }
  .tree li.off .name { color: var(--text-muted); text-decoration: line-through; text-decoration-color: color-mix(in srgb, var(--text-muted) 60%, transparent); }
  .reason {
    flex: none; font-family: var(--font); font-size: 11px; font-weight: 500; padding: 1px 7px; border-radius: 999px;
    background: var(--surface-2); color: var(--text-muted);
  }
  .reason.secret { background: var(--warn-soft); color: var(--warn); }
  .reason.test { background: var(--info-soft); color: var(--info); }
  .form { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 2fr); gap: 12px 20px; }
  .spacer { flex: 1; }
  @media (max-width: 760px) { .form { grid-template-columns: 1fr; } }
</style>

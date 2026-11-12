<script lang="ts">
  // The team file, edited only here: roles, block list, team settings, the rest as JSON. Saving
  // signs the file with the admins' key; Ember accepts no team file without a valid signature,
  // so nobody gets further by writing one of their own.
  import { onMount } from 'svelte';
  import { fade, scale } from 'svelte/transition';
  import { api, copyText, navigate, run, toast } from '../lib/app.svelte';
  import { t } from '../lib/i18n.svelte';
  import { formatDate } from '../lib/format';
  import type { TeamDoc, TeamFileView, TeamKeyCreated } from '../lib/types';
  import Icon from '../lib/components/Icon.svelte';
  import Calcifer from '../lib/components/Calcifer.svelte';
  import Tabs from '../lib/components/Tabs.svelte';
  import AccessRoles from '../lib/components/AccessRoles.svelte';
  import AccessBlocked from '../lib/components/AccessBlocked.svelte';
  import AccessTeam from '../lib/components/AccessTeam.svelte';
  import { modal } from '../lib/modal';

  const tabs = ['roles', 'blocked', 'team', 'file'] as const;
  let tab = $state<(typeof tabs)[number]>('roles');

  let view = $state<TeamFileView | null>(null);
  let doc = $state<TeamDoc | null>(null);
  let extra = $state('');

  // ---- signing
  let signing = $state(false);
  let keyFile = $state('');
  let password = $state('');
  let busy = $state(false);

  function take(v: TeamFileView) {
    view = v;
    doc = structuredClone(v.doc);
    extra = v.extra;
    keyFile = v.keyFile ?? '';
  }

  async function load() {
    const v = await run(() => api<TeamFileView>('team.admin'));
    if (v) take(v);
  }
  onMount(load);

  const editable = $derived(!!view?.writable);
  const changed = $derived.by(() => {
    if (!view || !doc) return { roles: 0, blocked: 0, team: 0, file: 0 };
    const roles = JSON.stringify(doc.access.roles) !== JSON.stringify(view.doc.access.roles) ? 1 : 0;
    const blocked = JSON.stringify(doc.access.blocked) !== JSON.stringify(view.doc.access.blocked) ? 1 : 0;
    const { access: _a, ...rest } = doc;
    const { access: _b, ...was } = view.doc;
    const team = JSON.stringify(rest) !== JSON.stringify(was) ? 1 : 0;
    const file = extra.trim() !== view.extra.trim() ? 1 : 0;
    return { roles, blocked, team, file };
  });
  const dirty = $derived(Object.values(changed).some(Boolean));

  const extraError = $derived.by(() => {
    if (!extra.trim()) return null;
    try {
      const v = JSON.parse(extra);
      return v && typeof v === 'object' && !Array.isArray(v) ? null : t('access.teamfile.extraObject');
    } catch (e) {
      return String((e as Error).message);
    }
  });

  // ---- a new signing key (once, by the admins); its public half goes into the Ember build
  let creating = $state(false);
  let newFolder = $state('');
  let newPassword = $state('');
  let newPassword2 = $state('');
  let created = $state<TeamKeyCreated | null>(null);
  const passwordProblem = $derived(
    !newPassword ? null : newPassword.length < 12 ? t('access.newKey.short') : newPassword2 && newPassword !== newPassword2 ? t('access.newKey.mismatch') : null,
  );

  async function pickFolder() {
    const dir = await api<string | null>('app.pickFolder', { title: t('access.newKey.folder'), initial: newFolder || null });
    if (dir) newFolder = dir;
  }

  async function createKey() {
    busy = true;
    const r = await run(() => api<TeamKeyCreated>('team.newKey', { folder: newFolder, password: newPassword }));
    busy = false;
    newPassword = newPassword2 = '';
    if (!r) return;
    created = r;
    creating = false;
    keyFile = r.path;
  }

  const unsigned = $derived(!!view && !view.trusted && !!view.path && !view.path.toLowerCase().endsWith('.ember'));

  async function pickKey() {
    const path = await api<string | null>('app.pickFile', { title: t('access.sign.keyFile'), filter: `${t('access.sign.keyFilter')}|*.pem;*.key|*.*|*.*` });
    if (path) keyFile = path;
  }

  async function sign() {
    if (!view || !doc || extraError) return;
    busy = true;
    const v = await run(() => api<TeamFileView>('team.save', { doc, extra, version: view!.version, keyFile, password }));
    busy = false;
    password = '';
    if (!v) return;
    signing = false;
    take(v);
    toast('ok', t('access.sign.saved', { version: v.version }));
  }
</script>

<header class="page-head">
  <h1>{t('access.title')}</h1>
</header>

{#if view && doc}
  <!-- the team file at a glance -->
  <div class="status" class:bad={!view.trusted || !!view.problem}>
    <Icon name={view.trusted ? 'shield' : 'alert'} size={18} />
    <div class="what">
      <strong>{view.trusted ? t('access.status.signed') : view.signatureRequired ? t('access.status.unsigned') : t('access.status.noCheck')}</strong>
      <span class="small muted">
        {t('access.status.version', { version: view.version })}
        {#if view.signedBy && view.signedAt} · {t('access.status.by', { user: view.signedBy, date: formatDate(view.signedAt) })}{/if}
      </span>
    </div>
    <code class="small path">{view.path ?? '–'}</code>
  </div>
  {#if view.problem}
    <div class="note warn"><Calcifer /><p>{t(`access.problems.${view.problem}`, { path: view.path ?? '' })}</p>
      {#if view.problem === 'noTeamFile'}<button class="btn small" onclick={() => navigate('settings')}>{t('nav.settings')}</button>{/if}
      {#if view.problem === 'teamOffline'}<button class="btn small" onclick={load}><Icon name="refresh" size={14} />{t('common.refresh')}</button>{/if}
    </div>
  {/if}

  <Tabs {tabs} bind:value={tab} labelKey="access.tabs" id="access" counts={changed} />

  <div id="access-panel" role="tabpanel" aria-labelledby="access-tab-{tab}">
    {#if tab === 'roles'}
      <AccessRoles bind:roles={doc.access.roles} modules={view.modules} myGroups={view.myGroups} {editable} />
    {:else if tab === 'blocked'}
      <AccessBlocked bind:blocked={doc.access.blocked} {editable} />
    {:else if tab === 'team'}
      <AccessTeam bind:doc {editable} />
    {:else}
      <section class="card stack tight">
        <div class="card-head"><Icon name="shield" /><h2>{t('access.teamfile.title')}</h2></div>
        <dl class="facts">
          <dt>{t('access.teamfile.path')}</dt><dd><code>{view.path ?? '–'}</code></dd>
          <dt>{t('access.teamfile.version')}</dt><dd>{view.version}</dd>
          <dt>{t('access.teamfile.signedBy')}</dt><dd>{view.signedBy ? `${view.signedBy}, ${formatDate(view.signedAt)}` : '–'}</dd>
          <dt>{t('access.teamfile.key')}</dt><dd><code>{view.keyId ?? '–'}</code> {#if view.trusted}<span class="badge ok"><Icon name="check" size={12} />{t('access.teamfile.trusted')}</span>{/if}</dd>
        </dl>
        {#if unsigned}<div class="note info"><Icon name="info" /><p class="small">{t('access.teamfile.convert')}</p></div>{/if}
        {#if !view.signatureRequired}<div class="note warn"><Calcifer /><p class="small">{t('access.teamfile.noCheck')}</p></div>{/if}
      </section>
      <section class="card stack tight">
        <div class="card-head"><Icon name="key" /><div class="grow"><h2>{t('access.newKey.title')}</h2></div></div>
        {#if keyFile}<p class="small">{t('access.newKey.current')} <code>{keyFile}</code></p>{/if}
        {#if created}
          <div class="created stack tight">
            <p class="small"><strong>{t('access.newKey.done', { path: created.path })}</strong> {t('access.newKey.next')}</p>
            <pre class="pem">{created.publicKey}</pre>
            <div class="row"><button class="btn small" onclick={async () => { await copyText(created!.publicKey); toast('ok', t('common.copied')); }}><Icon name="copy" size={14} />{t('access.newKey.copy')}</button>
              <span class="small muted">{t('access.newKey.id', { id: created.keyId })}</span></div>
          </div>
        {/if}
        {#if creating}
          <form class="stack tight" onsubmit={(e) => { e.preventDefault(); void createKey(); }}>
            <div class="field">
              <span>{t('access.newKey.folder')}</span>
              <div class="row nowrap"><code class="path">{newFolder || '–'}</code><button class="btn small" type="button" onclick={pickFolder}><Icon name="folder" size={14} />{t('playwright.pick')}</button></div>
              <span class="small muted">{t('access.newKey.folderHint')}</span>
            </div>
            <label class="field"><span>{t('access.sign.password')}</span><input type="password" bind:value={newPassword} autocomplete="new-password" /></label>
            <label class="field"><span>{t('access.newKey.repeat')}</span><input type="password" bind:value={newPassword2} autocomplete="new-password" /></label>
            {#if passwordProblem}<p class="small error">{passwordProblem}</p>{/if}
            <div class="row end">
              <button class="btn small" type="button" onclick={() => (creating = false)}>{t('common.cancel')}</button>
              <button class="btn primary small" type="submit" disabled={busy || !newFolder || newPassword.length < 12 || newPassword !== newPassword2}><Icon name="key" size={14} />{t('access.newKey.create')}</button>
            </div>
          </form>
        {:else}
          <div class="row"><button class="btn small" onclick={() => (creating = true)}><Icon name="plus" size={14} />{t('access.newKey.start')}</button></div>
        {/if}
      </section>
      <section class="card stack tight">
        <div class="card-head"><Icon name="settings" /><div class="grow"><h2 id="extra-title">{t('access.teamfile.extra')}</h2></div></div>
        <textarea class="json" rows="14" bind:value={extra} disabled={!editable} spellcheck="false" aria-labelledby="extra-title"
          aria-invalid={!!extraError} aria-describedby={extraError ? 'extra-error' : undefined}></textarea>
        {#if extraError}<p id="extra-error" class="field-error"><Icon name="alert" size={14} />{extraError}</p>{/if}
      </section>
    {/if}
  </div>

  <div class="savebar" class:dirty>
    <span class="small muted grow">{dirty ? t('access.unsaved') : t('access.savedVersion', { version: view.version })}</span>
    {#if dirty}<button class="btn small ghost" onclick={() => view && take(view)}>{t('access.discard')}</button>{/if}
    <button class="btn primary small" disabled={!dirty || !editable || !!extraError} onclick={() => (signing = true)}><Icon name="shield" size={14} />{t('access.sign.button')}</button>
  </div>
{:else}
  <p class="muted" role="status">{t('common.loading')}</p>
{/if}

{#if signing && view}
  <div class="layer" transition:fade={{ duration: 120 }}>
    <div class="backdrop" onclick={() => (signing = false)} role="presentation"></div>
    <div class="dialog" role="dialog" aria-modal="true" aria-labelledby="sign-title" use:modal={() => (signing = false)} transition:scale={{ start: 0.96, duration: 150 }}>
    <form class="stack tight" onsubmit={(e) => { e.preventDefault(); void sign(); }}>
      <div class="row"><Icon name="shield" /><h2 id="sign-title">{t('access.sign.title')}</h2></div>
      <p class="small muted">{t('access.sign.text', { from: view.version, to: view.version + 1 })}</p>
      <div class="field">
        <span>{t('access.sign.keyFile')}</span>
        <div class="row nowrap">
          <code class="path">{keyFile || '–'}</code>
          <button class="btn small" type="button" onclick={pickKey}><Icon name="key" size={14} />{t('access.sign.pick')}</button>
        </div>
      </div>
      {#if !keyFile}<p class="small muted">{t('access.sign.noKey')}</p>{/if}
      <label class="field"><span>{t('access.sign.password')}</span><input type="password" bind:value={password} autocomplete="off" /></label>
      <div class="row end">
        <button class="btn small" type="button" onclick={() => (signing = false)}>{t('common.cancel')}</button>
        <button class="btn primary small" type="submit" disabled={busy || !keyFile || !password}><Icon name="check" size={14} />{t('access.sign.confirm')}</button>
      </div>
    </form>
    </div>
  </div>
{/if}

<style>
  .status {
    display: flex; align-items: center; gap: 12px; padding: 10px 14px; margin-bottom: 14px;
    border-radius: var(--radius-small); background: var(--ok-soft); color: var(--text);
  }
  .status > :global(svg) { color: var(--ok); flex: none; }
  .status.bad { background: var(--warn-soft); }
  .status.bad > :global(svg) { color: var(--warn); }
  .status .what { display: flex; flex-direction: column; gap: 1px; }
  .status .path { margin-left: auto; color: var(--text-muted); overflow-wrap: anywhere; text-align: right; background: none; padding: 0; }
  .note { margin-bottom: 14px; }
  .grow { flex: 1; min-width: min(200px, 100%); display: flex; flex-direction: column; gap: 3px; }
  .facts { display: grid; grid-template-columns: max-content 1fr; gap: 6px 16px; margin: 0; font-size: 13px; }
  .facts dt { color: var(--text-muted); }
  .facts dd { margin: 0; display: flex; align-items: center; gap: 8px; flex-wrap: wrap; overflow-wrap: anywhere; }
  section + section { margin-top: 16px; }
  .json { font-family: var(--mono); font-size: 12.5px; resize: vertical; tab-size: 2; }
  .error { color: var(--error); }
  .savebar {
    margin-top: 18px; display: flex; flex-wrap: wrap; align-items: center; gap: 10px;
    padding: 10px 14px; border-radius: var(--radius); background: var(--surface); border: 1px solid var(--border);
  }
  .savebar.dirty { position: sticky; bottom: 0; border-color: var(--accent); box-shadow: 0 -6px 18px -12px color-mix(in srgb, var(--accent) 60%, transparent); }
  .layer { position: fixed; inset: 0; z-index: 40; display: flex; align-items: flex-start; justify-content: center; padding-top: 12vh; }
  .backdrop { position: absolute; inset: 0; background: var(--backdrop); }
  .dialog {
    position: relative; width: min(520px, calc(100vw - 40px)); padding: 22px 24px;
    background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius);
    box-shadow: var(--shadow-lift), 0 24px 60px -12px rgb(0 0 0 / 0.3);
  }
  .dialog h2 { margin: 0; }
  .field { display: flex; flex-direction: column; gap: 4px; font-size: 13px; color: var(--text-muted); }
  .nowrap { flex-wrap: nowrap; }
  .path { flex: 1; min-width: 0; padding: 6px 10px; background: var(--surface-2); border-radius: var(--radius-small); overflow-wrap: anywhere; }
  .end { justify-content: flex-end; margin-top: 6px; }
  .created { padding: 12px 14px; border-radius: var(--radius-small); background: var(--ok-soft); }
  .pem { margin: 0; padding: 10px 12px; border-radius: var(--radius-small); background: var(--surface); font-family: var(--mono); font-size: 12px; white-space: pre-wrap; overflow-wrap: anywhere; user-select: all; }
</style>

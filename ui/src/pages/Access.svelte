<script lang="ts">
  // Who sees which module: roles in team.json, each with modules and members (users, AD groups).
  // The host only rewrites "access" there and refuses when the file changed in between.
  import { onMount } from 'svelte';
  import { flip } from 'svelte/animate';
  import { fade, slide } from 'svelte/transition';
  import { advancedModules, api, app, confirm, copyText, navigate, run, toast } from '../lib/app.svelte';
  import { t } from '../lib/i18n.svelte';
  import type { AccessEntry, AccessLookup, AccessRole, AccessView, ModuleId } from '../lib/types';
  import Icon from '../lib/components/Icon.svelte';
  import Calcifer from '../lib/components/Calcifer.svelte';

  /** A role while it is edited; id keeps the cards stable while names change. */
  type Role = AccessRole & { id: number };

  let view = $state<AccessView | null>(null);
  let roles = $state<Role[]>([]);
  let contact = $state('');
  let input = $state<Record<number, string>>({});
  let kind = $state<Record<number, AccessEntry['kind']>>({});
  let target = $state<Record<string, number>>({});
  let groupFilter = $state('');
  let saving = $state(false);
  /** Roles whose key is shown in plain text (by card id). */
  let revealed = $state<Record<number, boolean>>({});
  let nextId = 1;

  function take(v: AccessView) {
    view = v;
    roles = v.roles.map((r) => ({ id: nextId++, name: r.name, modules: [...r.modules], members: r.members.map((e) => ({ ...e })), key: r.key ?? null, hideTools: [...(r.hideTools ?? [])] }));
    revealed = {};
    contact = v.contact ?? '';
    input = {};
    kind = {};
  }

  async function load() {
    const v = await run(() => api<AccessView>('access.get'));
    if (v) take(v);
  }
  onMount(load);

  const key = (e: AccessEntry) => `${e.kind}:${e.name.toLowerCase()}`;
  const plain = (list: AccessRole[]) =>
    JSON.stringify(list.map((r) => ({ name: r.name.trim(), modules: [...r.modules].sort(), members: r.members.map(key), key: r.key ?? null, hide: [...(r.hideTools ?? [])].sort() })));
  const dirty = $derived(!!view && (view.legacy || contact.trim() !== (view.contact ?? '') || plain(roles) !== plain(view.roles)));
  const editable = $derived(!!view?.writable);

  function addRole() {
    const role: Role = { id: nextId++, name: t('access.newRole'), modules: [], members: [] };
    roles = [...roles, role];
    // The new card's name field gets the focus, ready to be renamed.
    queueMicrotask(() => document.querySelector<HTMLInputElement>(`#role-${role.id}`)?.select());
  }

  function removeRole(role: Role) {
    roles = roles.filter((r) => r !== role);
  }

  const all = (role: Role) => role.modules.includes('*');

  function toggleAll(role: Role, on: boolean) {
    role.modules = on ? ['*'] : [];
  }

  function toggleModule(role: Role, module: ModuleId, on: boolean) {
    role.modules = on ? [...role.modules, module] : role.modules.filter((m) => m !== module);
  }

  function push(role: Role, entry: AccessEntry) {
    if (role.members.some((e) => e.name.toLowerCase() === entry.name.toLowerCase())) {
      toast('info', t('access.duplicate', { name: entry.name }));
      return;
    }
    role.members = [...role.members, entry];
  }

  async function add(role: Role) {
    const name = (input[role.id] ?? '').trim();
    if (!name) return;
    const found = await run(() => api<AccessLookup>('access.lookup', { name }));
    if (!found) return;
    if (found.found === false) {
      toast('error', t('access.notFound', { name }));
      return;
    }
    const entry: AccessEntry = { name: found.name, kind: found.kind ?? kind[role.id] ?? 'group' };
    if (found.found === null) toast('info', t('access.unchecked', { name: entry.name, kind: t(`access.${entry.kind}`) }));
    push(role, entry);
    input[role.id] = '';
  }

  // ---- role keys: whoever enters one gets the role; renewing locks out everyone with the old one.
  async function newKey(role: Role) {
    if (role.key && !(await confirm(t('access.key.rotate'), t('access.key.rotateConfirm', { role: role.name }), t('access.key.rotate')))) return;
    const k = await run(() => api<string>('access.newKey'));
    if (!k) return;
    role.key = k;
    revealed[role.id] = true;
    toast('info', t('access.key.rotated', { role: role.name }));
  }

  async function copyKey(role: Role) {
    if (!role.key) return;
    await copyText(role.key);
    toast('ok', t('access.key.copied'));
  }

  /** EMBER-7QK2M-•••••-•••••-•••••: enough to tell keys apart, not enough to use one. */
  const masked = (k: string) => k.split('-').map((p, i) => (i < 2 ? p : '•'.repeat(p.length))).join('-');

  const offersSoftware = (role: Role) => all(role) || role.modules.includes('software');

  function toggleBruno(role: Role, on: boolean) {
    const rest = (role.hideTools ?? []).filter((x) => x !== 'bruno');
    role.hideTools = on ? rest : [...rest, 'bruno'];
  }

  function remove(role: Role, entry: AccessEntry) {
    role.members = role.members.filter((e) => e !== entry);
  }

  function addGroup(group: string) {
    const role = roles.find((r) => r.id === (target[group] ?? roles[0]?.id));
    if (role) push(role, { name: group, kind: 'group' });
  }

  async function save() {
    if (!view?.version) return;
    saving = true;
    const change = {
      roles: roles.map((r) => ({ name: r.name.trim(), modules: r.modules, members: r.members, key: r.key || null, hideTools: r.hideTools ?? [] })),
      contact: contact.trim() || null,
      version: view.version,
    };
    const v = await run(() => api<AccessView>('access.save', { change }), t('access.savedToast'));
    saving = false;
    if (v) take(v);
  }

  const groups = $derived.by(() => {
    const q = groupFilter.trim().toLowerCase();
    return (view?.myGroups ?? []).filter((g) => !q || g.toLowerCase().includes(q));
  });
</script>

<header class="page-head">
  <h1>{t('access.title')}</h1>
  <p>{t('access.subtitle')}</p>
</header>

{#if view}
  {#if view.problem}
    <div class="note warn problem">
      <Calcifer />
      <p>{t(`access.problems.${view.problem}`, { path: view.teamPath ?? '' })}</p>
      {#if view.problem === 'noTeamFile'}<button class="btn small" onclick={() => navigate('settings')}>{t('nav.settings')}</button>{/if}
      {#if view.problem === 'teamOffline'}<button class="btn small" onclick={load}><Icon name="refresh" size={14} />{t('common.refresh')}</button>{/if}
    </div>
  {/if}
  {#if view.legacy}
    <div class="note problem"><Icon name="info" /><p class="small">{t('access.legacy')}</p></div>
  {/if}

  <div class="section-head">
    <div class="grow"><h2>{t('access.roles')}</h2><p class="small muted">{t('access.rolesHint')}</p></div>
    <button class="btn small" disabled={!editable} onclick={addRole}><Icon name="plus" size={14} />{t('access.addRole')}</button>
  </div>

  {#if !roles.length}
    <div class="note warn"><Calcifer /><p>{t('access.noRoles')}</p></div>
  {/if}

  {#each roles as role (role.id)}
    <section class="card stack role" animate:flip={{ duration: 180 }} transition:slide={{ duration: 180 }}>
      <div class="card-head">
        <Icon name="shield" />
        <input id="role-{role.id}" class="role-name" type="text" bind:value={role.name} disabled={!editable} aria-label={t('access.roleName')} spellcheck="false" />
        <span class="small muted">{t('access.memberCount', { count: role.members.length })}</span>
        <span class="spacer"></span>
        <button class="btn small ghost icon-only" disabled={!editable} title={t('access.removeRole', { name: role.name })} aria-label={t('access.removeRole', { name: role.name })} onclick={() => removeRole(role)}><Icon name="trash" size={14} /></button>
      </div>

      <fieldset class="modules" disabled={!editable}>
        <legend class="small muted">{t('access.modules')}</legend>
        <label class="chip all" class:on={all(role)}>
          <input type="checkbox" checked={all(role)} onchange={(e) => toggleAll(role, e.currentTarget.checked)} />
          <Icon name="check" size={13} />{t('access.allModules')}<span class="hint">{t('access.allModulesHint')}</span>
        </label>
        {#each view.modules as m (m)}
          {@const on = all(role) || role.modules.includes(m)}
          <label class="chip" class:on class:advanced={advancedModules.includes(m)} title={advancedModules.includes(m) ? t('access.advancedHint') : undefined}>
            <input type="checkbox" checked={on} disabled={all(role)} onchange={(e) => toggleModule(role, m, e.currentTarget.checked)} />
            <Icon name="check" size={13} />{t(`nav.${m}`)}
          </label>
        {/each}
      </fieldset>

      <div class="key-row">
        <Icon name="key" size={15} />
        {#if role.key}
          <code class="key" class:revealed={revealed[role.id]}>{revealed[role.id] ? role.key : masked(role.key)}</code>
          <button class="btn small ghost icon-only" title={revealed[role.id] ? t('access.key.hide') : t('access.key.show')} aria-label={revealed[role.id] ? t('access.key.hide') : t('access.key.show')} onclick={() => (revealed[role.id] = !revealed[role.id])}><Icon name="eye" size={14} /></button>
          <button class="btn small ghost icon-only" title={t('access.key.copy')} aria-label={t('access.key.copy')} onclick={() => copyKey(role)}><Icon name="copy" size={14} /></button>
          <span class="spacer"></span>
          <button class="btn small" disabled={!editable} onclick={() => newKey(role)}><Icon name="refresh" size={13} />{t('access.key.rotate')}</button>
          <button class="btn small ghost icon-only" disabled={!editable} title={t('access.key.remove')} aria-label={t('access.key.remove')} onclick={() => (role.key = null)}><Icon name="x" size={14} /></button>
        {:else}
          <span class="small muted grow-text">{t('access.key.none')}</span>
          <button class="btn small" disabled={!editable} onclick={() => newKey(role)}><Icon name="plus" size={13} />{t('access.key.create')}</button>
        {/if}
      </div>
      {#if role.key}<p class="small muted key-hint">{t('access.key.hint')}</p>{/if}

      {#if offersSoftware(role)}
        <label class="check small"><input type="checkbox" checked={!(role.hideTools ?? []).includes('bruno')} disabled={!editable} onchange={(e) => toggleBruno(role, e.currentTarget.checked)} />{t('access.hideBruno')}</label>
      {/if}

      <div class="members">
        {#if role.members.length}
          <ul class="entries">
            {#each role.members as e (key(e))}
              <li animate:flip={{ duration: 160 }} in:fade={{ duration: 120 }}>
                <Icon name={e.kind === 'group' ? 'users' : 'user'} size={14} />
                <span class="name">{e.name}</span>
                <span class="badge">{t(`access.${e.kind}`)}</span>
                <button class="btn small ghost icon-only" disabled={!editable} title={t('access.remove', { name: e.name })} aria-label={t('access.remove', { name: e.name })} onclick={() => remove(role, e)}><Icon name="x" size={14} /></button>
              </li>
            {/each}
          </ul>
        {:else}
          <p class="small muted">{t('access.membersEmpty')}</p>
        {/if}
        <form class="row add" onsubmit={(ev) => { ev.preventDefault(); void add(role); }}>
          <input type="text" bind:value={input[role.id]} placeholder={t('access.addPlaceholder')} disabled={!editable} spellcheck="false" autocomplete="off" aria-label={t('access.addPlaceholder')} />
          <select value={kind[role.id] ?? 'group'} onchange={(e) => (kind[role.id] = e.currentTarget.value as AccessEntry['kind'])} disabled={!editable} aria-label={t('access.group')}>
            <option value="user">{t('access.user')}</option>
            <option value="group">{t('access.group')}</option>
          </select>
          <button class="btn small" type="submit" disabled={!editable || !(input[role.id] ?? '').trim()}><Icon name="plus" size={14} />{t('access.add')}</button>
        </form>
      </div>
    </section>
  {/each}

  <section class="card stack tight">
    <label class="field contact">
      <span class="small muted">{t('access.contact')}</span>
      <input type="text" inputmode="email" bind:value={contact} disabled={!editable} placeholder={app.info?.serviceCenter ?? ''} spellcheck="false" />
      <span class="small muted">{t('access.contactHint', { email: app.info?.serviceCenter ?? '–' })}</span>
    </label>
  </section>

  <section class="card stack tight">
    <div class="card-head">
      <Icon name="user" />
      <div class="grow"><h2>{t('access.me')}</h2><p class="small muted">{t('access.meHint')}</p></div>
      <code>{view.account}</code>
    </div>
    {#if view.myGroups.length > 8}
      <input class="filter" type="search" bind:value={groupFilter} placeholder={t('access.groupsFilter')} aria-label={t('access.groupsFilter')} />
    {/if}
    {#if view.myGroups.length}
      <ul class="groups">
        {#each groups as g (g)}
          <li>
            <span class="name">{g}</span>
            {#if roles.length}
              <select value={target[g] ?? roles[0].id} onchange={(e) => (target[g] = Number(e.currentTarget.value))} disabled={!editable} aria-label={t('access.addToRole', { name: g })}>
                {#each roles as r (r.id)}<option value={r.id}>{r.name}</option>{/each}
              </select>
              <button class="btn small ghost" disabled={!editable} onclick={() => addGroup(g)}><Icon name="plus" size={13} />{t('access.add')}</button>
            {/if}
          </li>
        {/each}
      </ul>
    {:else}
      <p class="small muted">{t('access.noGroups')}</p>
    {/if}
  </section>

  <div class="savebar" class:dirty>
    <span class="small muted grow">{dirty ? t('access.unsaved') : view.teamPath ? t('access.file', { path: view.teamPath }) : ''}</span>
    {#if dirty && !view.legacy}<button class="btn small ghost" onclick={() => view && take(view)}>{t('access.discard')}</button>{/if}
    <button class="btn primary small" disabled={!dirty || !editable || saving} onclick={save}><Icon name="check" size={14} />{t('access.save')}</button>
  </div>
{:else}
  <p class="muted">{t('common.loading')}</p>
{/if}

<style>
  .problem { margin-bottom: 16px; }
  .section-head { display: flex; align-items: flex-end; gap: 12px; flex-wrap: wrap; margin-bottom: 12px; }
  .grow { flex: 1; min-width: 200px; display: flex; flex-direction: column; gap: 3px; }
  section + section, .role + section { margin-top: 16px; }
  .role-name { font: inherit; font-family: var(--font-display); font-size: 16px; font-weight: 600; min-width: 0; flex: 0 1 280px; }
  .modules { border: 0; padding: 0; margin: 0; display: flex; flex-wrap: wrap; gap: 6px; }
  .modules legend { padding: 0; margin-bottom: 6px; width: 100%; }
  .chip {
    display: inline-flex; align-items: center; gap: 6px; padding: 4px 11px 4px 9px; border-radius: 999px; cursor: pointer;
    border: 1px solid var(--border-strong); background: var(--surface); font-size: 13px; user-select: none;
  }
  .chip input { position: absolute; opacity: 0; pointer-events: none; }
  .chip :global(svg) { opacity: 0; transition: opacity 0.12s; }
  .chip.on { background: var(--accent-soft); border-color: color-mix(in srgb, var(--accent) 45%, var(--border-strong)); }
  .chip.on :global(svg) { opacity: 1; color: var(--accent-fg); }
  .chip:has(input:focus-visible) { outline: 2px solid var(--accent); outline-offset: 2px; }
  .chip:has(input:disabled) { cursor: default; }
  .chip.advanced { border-style: dashed; }
  .chip.all { font-weight: 600; }
  .chip .hint { font-weight: 400; color: var(--text-muted); font-size: 12px; }
  .members { display: flex; flex-direction: column; gap: 4px; }
  .key-row {
    display: flex; align-items: center; gap: 6px; flex-wrap: wrap;
    padding: 6px 8px 6px 10px; border-radius: var(--radius-small); background: var(--surface-2);
  }
  .key-row > :global(svg) { color: var(--text-muted); flex: none; }
  .key { font-family: var(--mono); font-size: 13px; letter-spacing: 0.04em; color: var(--text-muted); user-select: all; }
  .key.revealed { color: var(--text); }
  .grow-text { flex: 1; }
  .key-hint { margin: -6px 0 0 2px; }
  .entries, .groups { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 2px; }
  .entries li, .groups li { display: flex; align-items: center; gap: 8px; padding: 5px 8px; border-radius: var(--radius-small); }
  .entries li { background: var(--surface-2); }
  .entries li + li { margin-top: 2px; }
  .entries :global(svg) { color: var(--text-muted); flex: none; }
  .name { flex: 1; min-width: 0; overflow-wrap: anywhere; font-family: var(--mono); font-size: 12.5px; }
  .groups { max-height: 280px; overflow-y: auto; }
  .groups li:hover { background: var(--surface-2); }
  .groups select { width: auto; max-width: 180px; }
  .icon-only { padding: 3px 7px; }
  .add { margin-top: 8px; flex-wrap: wrap; }
  .add input { flex: 1 1 240px; width: auto; min-width: 0; }
  .add select { flex: none; width: auto; }
  .contact { display: flex; flex-direction: column; gap: 4px; max-width: 420px; }
  .filter { max-width: 280px; }
  .savebar {
    margin-top: 18px; display: flex; align-items: center; gap: 10px;
    padding: 10px 14px; border-radius: var(--radius); background: var(--surface); border: 1px solid var(--border);
  }
  /* Only unsaved changes follow the scrolling, otherwise the bar would cover the group list. */
  .savebar.dirty { position: sticky; bottom: 0; border-color: var(--accent); box-shadow: 0 -6px 18px -12px color-mix(in srgb, var(--accent) 60%, transparent); }
</style>

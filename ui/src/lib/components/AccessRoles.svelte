<script lang="ts">
  // Roles as a list with one open role beside it: on the left what exists, on the right what the
  // selected role may see and who is in it. Members are looked up in the directory before they go in.
  import { flip } from 'svelte/animate';
  import { fade } from 'svelte/transition';
  import { advancedModules, api, run, toast } from '../app.svelte';
  import { t } from '../i18n.svelte';
  import type { AccessEntry, AccessLookup, AccessRole, ModuleId } from '../types';
  import Icon from './Icon.svelte';
  import Calcifer from './Calcifer.svelte';
  import DirectoryPicker from './DirectoryPicker.svelte';

  let { roles = $bindable(), modules, myGroups, editable }: {
    roles: AccessRole[];
    modules: ModuleId[];
    myGroups: string[];
    editable: boolean;
  } = $props();

  let selected = $state(0);
  let groupFilter = $state('');

  const role = $derived(roles[Math.min(selected, roles.length - 1)] ?? null);

  const icons: Record<ModuleId, string> = {
    proxy: 'proxy', git: 'git', software: 'software', playwright: 'browser', system: 'system',
    power: 'power', ai: 'ai', access: 'users', settings: 'settings', log: 'log',
  };
  const groups = $derived([
    { id: 'daily', items: modules.filter((m) => !advancedModules.includes(m) && m !== 'settings' && m !== 'log') },
    { id: 'advanced', items: modules.filter((m) => advancedModules.includes(m)) },
    { id: 'more', items: modules.filter((m) => m === 'settings' || m === 'log') },
  ]);

  const all = (r: AccessRole) => r.modules.includes('*');
  const has = (r: AccessRole, m: ModuleId) => all(r) || r.modules.includes(m);
  const summary = (r: AccessRole) => (all(r) ? t('access.allModules') : r.modules.map((m) => t(`nav.${m}`)).join(' · ') || t('access.noModules'));

  function toggle(m: ModuleId) {
    if (!role || all(role)) return;
    role.modules = role.modules.includes(m) ? role.modules.filter((x) => x !== m) : [...role.modules, m];
  }

  function addRole() {
    roles = [...roles, { name: t('access.newRole'), modules: [], members: [], hideTools: [] }];
    selected = roles.length - 1;
    queueMicrotask(() => document.querySelector<HTMLInputElement>('#role-name')?.select());
  }

  function removeRole() {
    if (!role) return;
    roles = roles.filter((r) => r !== role);
    selected = Math.max(0, selected - 1);
  }

  /** A member from the directory picker. */
  function addEntry(entry: AccessEntry) {
    if (!role) return;
    if (role.members.some((e) => e.name.toLowerCase() === entry.name.toLowerCase())) {
      toast('info', t('access.duplicate', { name: entry.name }));
      return;
    }
    role.members = [...role.members, entry];
  }

  /** One of the own groups, by name (checked in the directory). */
  async function add(name: string, as: AccessEntry['kind'] = 'group') {
    if (!role || !name) return;
    const found = await run(() => api<AccessLookup>('access.lookup', { name }));
    if (!found) return;
    if (found.found === false) {
      toast('error', t('access.notFound', { name }));
      return;
    }
    // Only what exists in the Active Directory goes in: no answer (no VPN) is no.
    if (found.found === null) {
      toast('error', t('access.unchecked', { name }));
      return;
    }
    const entry: AccessEntry = { name: found.name, kind: found.kind ?? as };
    if (role.members.some((e) => e.name.toLowerCase() === entry.name.toLowerCase())) {
      toast('info', t('access.duplicate', { name: entry.name }));
      return;
    }
    role.members = [...role.members, entry];
  }

  // Developer tools Ember installs straight from the vendor; Software Center packages are always there.
  const tools = [
    ['nodejs', 'Node.js'], ['git', 'Git'], ['vscode', 'VS Code'], ['python', 'Python'],
    ['visualstudio', 'Visual Studio'], ['dotnet', '.NET SDK'], ['pwsh', 'PowerShell 7'], ['bruno', 'Bruno'],
  ] as const;
  const offers = (id: string) => !!role && !(role.hideTools ?? []).includes(id);
  function toggleTool(id: string) {
    if (!role) return;
    const rest = (role.hideTools ?? []).filter((x) => x !== id);
    role.hideTools = offers(id) ? [...rest, id] : rest;
  }

  const mine = $derived.by(() => {
    const q = groupFilter.trim().toLowerCase();
    return myGroups.filter((g) => !q || g.toLowerCase().includes(q));
  });
</script>

<div class="split">
  <!-- the roles -->
  <nav class="list" aria-label={t('access.roles')}>
    {#each roles as r, i (i)}
      <button class="item" class:active={r === role} aria-current={r === role ? 'true' : undefined} onclick={() => (selected = i)} animate:flip={{ duration: 160 }}>
        <span class="name">{r.name || '–'}</span>
        <span class="small muted sum">{summary(r)}</span>
        <span class="small muted count"><Icon name="users" size={12} />{r.members.length}</span>
      </button>
    {/each}
    <button class="btn small ghost add-role" disabled={!editable} onclick={addRole}><Icon name="plus" size={14} />{t('access.addRole')}</button>
  </nav>

  <!-- the open role -->
  {#if role}
    <section class="card detail stack" aria-labelledby="role-heading">
      <h2 id="role-heading" class="visually-hidden">{t('access.roleHeading', { name: role.name || '–' })}</h2>
      <div class="head">
        <input id="role-name" class="role-name" type="text" bind:value={role.name} disabled={!editable} aria-label={t('access.roleName')} spellcheck="false" />
        <button class="btn small ghost" disabled={!editable} onclick={removeRole}><Icon name="trash" size={14} />{t('access.deleteRole')}</button>
      </div>

      <div class="block">
        <div class="row between">
          <h3>{t('access.modules')}</h3>
          <label class="check small"><input type="checkbox" checked={all(role)} disabled={!editable} onchange={(e) => (role.modules = e.currentTarget.checked ? ['*'] : [])} />{t('access.allModules')}</label>
        </div>
        {#each groups as g (g.id)}
          <div class="group">
            <span class="small muted label">{t(`access.moduleGroups.${g.id}`)}</span>
            <div class="tiles">
              {#each g.items as m (m)}
                <button class="tile" class:on={has(role, m)} disabled={!editable || all(role)} aria-pressed={has(role, m)} onclick={() => toggle(m)}>
                  <Icon name={icons[m]} size={16} /><span>{t(`nav.${m}`)}</span>
                  <span class="tick"><Icon name="check" size={12} /></span>
                </button>
              {/each}
            </div>
          </div>
        {/each}
        {#if has(role, 'software')}
          <div class="group">
            <span class="small muted label">{t('access.tools')}</span>
            <div class="tool-row">
              {#each tools as [id, label] (id)}
                <button class="tool" class:on={offers(id)} disabled={!editable} aria-pressed={offers(id)} onclick={() => toggleTool(id)}>
                  <span class="tick"><Icon name="check" size={11} /></span>{label}
                </button>
              {/each}
            </div>
          </div>
        {/if}
      </div>

      <div class="block">
        <h3>{t('access.members')}</h3>
        {#if role.members.length}
          <ul class="members">
            {#each role.members as e (e.kind + e.name)}
              <li in:fade={{ duration: 120 }}>
                <Icon name={e.kind === 'group' ? 'users' : 'user'} size={14} />
                <span class="mono-name">{e.name}</span>
                <span class="small muted">{t(`access.${e.kind}`)}</span>
                <button class="btn small ghost icon-only" disabled={!editable} title={t('access.remove', { name: e.name })} aria-label={t('access.remove', { name: e.name })} onclick={() => (role.members = role.members.filter((x) => x !== e))}><Icon name="x" size={14} /></button>
              </li>
            {/each}
          </ul>
        {:else}
          <div class="note"><Calcifer /><p class="small">{t('access.membersEmpty')}</p></div>
        {/if}
        <DirectoryPicker onpick={addEntry} disabled={!editable} label={t('access.addPlaceholder')} actionLabel={t('access.add')} />
        {#if myGroups.length}
          <details class="mine">
            <summary class="small">{t('access.myGroups', { count: myGroups.length })}</summary>
            {#if myGroups.length > 8}<input class="filter" type="search" bind:value={groupFilter} placeholder={t('access.groupsFilter')} aria-label={t('access.groupsFilter')} />{/if}
            <ul>
              {#each mine as g (g)}
                <li><span class="mono-name">{g}</span><button class="btn small ghost" disabled={!editable} onclick={() => add(g, 'group')}><Icon name="plus" size={13} />{t('access.add')}</button></li>
              {/each}
            </ul>
          </details>
        {/if}
      </div>
    </section>
  {:else}
    <div class="note warn"><Calcifer /><p>{t('access.noRoles')}</p></div>
  {/if}
</div>

<style>
  .split { display: grid; grid-template-columns: 260px minmax(0, 1fr); gap: 16px; align-items: start; }
  .list { display: flex; flex-direction: column; gap: 4px; position: sticky; top: 0; }
  .item {
    display: grid; grid-template-columns: 1fr auto; gap: 2px 8px; padding: 10px 12px; text-align: left; cursor: pointer;
    font: inherit; color: inherit; background: transparent; border: 1px solid transparent; border-radius: var(--radius-small);
    transition: background-color 150ms ease-out, border-color 150ms ease-out;
  }
  .item:hover { background: var(--surface-2); }
  .item.active { background: var(--surface); border-color: var(--border); box-shadow: inset 3px 0 0 var(--accent), var(--shadow); }
  .item .name { font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .item .count { display: inline-flex; align-items: center; gap: 4px; grid-row: 1; grid-column: 2; }
  .item .sum { grid-column: 1 / -1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .add-role { justify-content: flex-start; margin-top: 4px; }
  .head { display: flex; align-items: center; gap: 10px; }
  .role-name { flex: 1; min-width: 0; font: inherit; font-family: var(--font-display); font-size: 18px; font-weight: 600; }
  h3 { margin: 0; font-size: 14px; }
  .block { display: flex; flex-direction: column; gap: 10px; }
  .block + .block { padding-top: 16px; border-top: 1px solid var(--border); }
  .group { display: grid; grid-template-columns: 96px 1fr; gap: 8px; align-items: start; }
  .group .label { padding-top: 8px; }
  .tiles { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(140px, 100%), 1fr)); gap: 6px; }
  .tile {
    position: relative; display: flex; align-items: center; gap: 8px; padding: 8px 10px; cursor: pointer; text-align: left;
    font: inherit; font-size: 13px; color: var(--text-muted); background: var(--surface); border: 1px solid var(--border-strong); border-radius: var(--radius-small);
    transition: background-color 150ms ease-out, border-color 150ms ease-out, color 150ms ease-out;
  }
  .tile:hover:not(:disabled) { border-color: color-mix(in srgb, var(--accent) 40%, var(--border-strong)); color: var(--text); }
  .tile.on { background: var(--accent-soft); border-color: color-mix(in srgb, var(--accent) 45%, var(--border-strong)); color: var(--text); }
  .tile > :global(svg) { flex: none; }
  .tile > span:not(.tick) { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .tile.on > :global(svg) { color: var(--accent-fg); }
  .tile:disabled { cursor: default; }
  .tile .tick {
    margin-left: auto; display: grid; place-items: center; width: 16px; height: 16px; border-radius: 50%;
    background: var(--accent); color: var(--accent-text); opacity: 0; transform: scale(0.6);
    transition: opacity 150ms ease-out, transform 200ms cubic-bezier(0.16, 1, 0.3, 1);
  }
  .tile.on .tick { opacity: 1; transform: scale(1); }
  .tool-row { display: flex; flex-wrap: wrap; gap: 6px; }
  .tool {
    display: inline-flex; align-items: center; gap: 6px; padding: 4px 11px 4px 6px; border-radius: 999px; cursor: pointer;
    font: inherit; font-size: 13px; color: var(--text-muted); background: var(--surface); border: 1px solid var(--border-strong);
    transition: background-color 150ms ease-out, border-color 150ms ease-out, color 150ms ease-out;
  }
  .tool .tick { display: grid; place-items: center; width: 16px; height: 16px; border-radius: 50%; border: 1px solid var(--border-strong); color: transparent; }
  .tool.on { color: var(--text); background: var(--accent-soft); border-color: color-mix(in srgb, var(--accent) 45%, var(--border-strong)); }
  .tool.on .tick { background: var(--accent); border-color: var(--accent); color: var(--accent-text); }
  .tool:hover:not(:disabled) { color: var(--text); }
  .tool:disabled { cursor: default; }
  .members { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 2px; }
  .members li { display: flex; align-items: center; gap: 8px; padding: 6px 8px; border-radius: var(--radius-small); background: var(--surface-2); }
  .members li :global(svg) { color: var(--text-muted); flex: none; }
  .mono-name { flex: 1; min-width: 0; overflow-wrap: anywhere; font-family: var(--mono); font-size: 12.5px; }
  .icon-only { padding: 3px 7px; }
  .mine summary { cursor: pointer; color: var(--text-muted); }
  .mine ul { list-style: none; margin: 8px 0 0; padding: 0; max-height: 220px; overflow-y: auto; }
  .mine li { display: flex; align-items: center; gap: 8px; padding: 3px 8px; border-radius: 6px; }
  .mine li:hover { background: var(--surface-2); }
  .filter { margin-top: 8px; max-width: 280px; }
  @media (max-width: 820px) { .split { grid-template-columns: minmax(0, 1fr); } .list { position: static; } }
  @media (prefers-reduced-motion: reduce) { .tile, .tile .tick { transition: none; } }
</style>

<script lang="ts">
  // Admins: who may use Ember and who is an admin. Both lists live in team.json; the host only
  // rewrites "access" and "admins" there and refuses when the file changed in between.
  import { onMount } from 'svelte';
  import { flip } from 'svelte/animate';
  import { fade } from 'svelte/transition';
  import { api, app, navigate, run, toast } from '../lib/app.svelte';
  import { t } from '../lib/i18n.svelte';
  import type { AccessEntry, AccessLookup, AccessView } from '../lib/types';
  import Icon from '../lib/components/Icon.svelte';
  import Calcifer from '../lib/components/Calcifer.svelte';

  type ListKey = 'members' | 'admins';

  let view = $state<AccessView | null>(null);
  let restricted = $state(false);
  let members = $state<AccessEntry[]>([]);
  let admins = $state<AccessEntry[]>([]);
  let contact = $state('');
  let input = $state<Record<ListKey, string>>({ members: '', admins: '' });
  let kind = $state<Record<ListKey, AccessEntry['kind']>>({ members: 'group', admins: 'user' });
  let groupFilter = $state('');
  let saving = $state(false);

  function take(v: AccessView) {
    view = v;
    restricted = v.restricted;
    members = v.members.map((e) => ({ ...e }));
    admins = v.admins.map((e) => ({ ...e }));
    contact = v.contact ?? '';
  }

  async function load() {
    const v = await run(() => api<AccessView>('access.get'));
    if (v) take(v);
  }
  onMount(load);

  const key = (e: AccessEntry) => `${e.kind}:${e.name.toLowerCase()}`;
  const same = (a: AccessEntry[], b: AccessEntry[]) => a.length === b.length && a.every((e, i) => key(e) === key(b[i]));
  const dirty = $derived(
    !!view && (restricted !== view.restricted || contact.trim() !== (view.contact ?? '') || !same(members, view.members) || !same(admins, view.admins)),
  );
  const editable = $derived(!!view?.writable);

  function listOf(which: ListKey) {
    return which === 'members' ? members : admins;
  }

  function push(which: ListKey, entry: AccessEntry) {
    const list = listOf(which);
    if (list.some((e) => e.name.toLowerCase() === entry.name.toLowerCase())) {
      toast('info', t('access.duplicate', { name: entry.name }));
      return;
    }
    if (which === 'members') members = [...members, entry];
    else admins = [...admins, entry];
  }

  async function add(which: ListKey) {
    const name = input[which].trim();
    if (!name) return;
    const found = await run(() => api<AccessLookup>('access.lookup', { name }));
    if (!found) return;
    if (found.found === false) {
      toast('error', t('access.notFound', { name }));
      return;
    }
    const entry: AccessEntry = { name: found.name, kind: found.kind ?? kind[which] };
    if (found.found === null) toast('info', t('access.unchecked', { name: entry.name, kind: t(`access.${entry.kind}`) }));
    push(which, entry);
    input[which] = '';
  }

  function remove(which: ListKey, entry: AccessEntry) {
    if (which === 'members') members = members.filter((e) => e !== entry);
    else admins = admins.filter((e) => e !== entry);
  }

  async function save() {
    if (!view?.version) return;
    saving = true;
    const change = { restricted, members, admins, contact: contact.trim() || null, version: view.version };
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

  <section class="card stack">
    <div class="card-head">
      <Icon name="users" />
      <div class="grow"><h2>{t('access.members')}</h2><p class="small muted">{t('access.membersHint')}</p></div>
    </div>
    <label class="check restrict"><input type="checkbox" bind:checked={restricted} disabled={!editable} />{t('access.restrict')}</label>
    {#if !restricted}<p class="small muted">{t('access.everyone')}</p>{/if}

    <div class:dim={!restricted}>
      {@render list('members', members, t('access.membersEmpty'))}
    </div>

    <label class="field contact">
      <span class="small muted">{t('access.contact')}</span>
      <input type="text" inputmode="email" bind:value={contact} disabled={!editable} placeholder={app.info?.serviceCenter ?? ''} spellcheck="false" />
      <span class="small muted">{t('access.contactHint', { email: app.info?.serviceCenter ?? '–' })}</span>
    </label>
  </section>

  <section class="card stack">
    <div class="card-head">
      <Icon name="shield" />
      <div class="grow"><h2>{t('access.admins')}</h2><p class="small muted">{t('access.adminsHint')}</p></div>
    </div>
    {@render list('admins', admins, t('access.adminsEmpty'))}
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
            <button class="btn small ghost" disabled={!editable} onclick={() => push('members', { name: g, kind: 'group' })}>{t('access.toMembers')}</button>
            <button class="btn small ghost" disabled={!editable} onclick={() => push('admins', { name: g, kind: 'group' })}>{t('access.toAdmins')}</button>
          </li>
        {/each}
      </ul>
    {:else}
      <p class="small muted">{t('access.noGroups')}</p>
    {/if}
  </section>

  <div class="savebar" class:dirty>
    <span class="small muted grow">{dirty ? t('access.unsaved') : view.teamPath ? t('access.file', { path: view.teamPath }) : ''}</span>
    {#if dirty}<button class="btn small ghost" onclick={() => view && take(view)}>{t('access.discard')}</button>{/if}
    <button class="btn primary small" disabled={!dirty || !editable || saving} onclick={save}><Icon name="check" size={14} />{t('access.save')}</button>
  </div>
{:else}
  <p class="muted">{t('common.loading')}</p>
{/if}

{#snippet list(which: ListKey, entries: AccessEntry[], empty: string)}
  {#if entries.length}
    <ul class="entries">
      {#each entries as e (key(e))}
        <li animate:flip={{ duration: 160 }} in:fade={{ duration: 120 }}>
          <Icon name={e.kind === 'group' ? 'users' : 'user'} size={14} />
          <span class="name">{e.name}</span>
          <span class="badge">{t(`access.${e.kind}`)}</span>
          <button class="btn small ghost icon-only" disabled={!editable} title={t('access.remove', { name: e.name })} aria-label={t('access.remove', { name: e.name })} onclick={() => remove(which, e)}><Icon name="x" size={14} /></button>
        </li>
      {/each}
    </ul>
  {:else}
    <p class="small muted">{empty}</p>
  {/if}
  <form class="row add" onsubmit={(ev) => { ev.preventDefault(); void add(which); }}>
    <input type="text" bind:value={input[which]} placeholder={t('access.addPlaceholder')} disabled={!editable} spellcheck="false" autocomplete="off" aria-label={t('access.addPlaceholder')} />
    <select bind:value={kind[which]} disabled={!editable} aria-label={t('access.group')}>
      <option value="user">{t('access.user')}</option>
      <option value="group">{t('access.group')}</option>
    </select>
    <button class="btn small" type="submit" disabled={!editable || !input[which].trim()}><Icon name="plus" size={14} />{t('access.add')}</button>
  </form>
{/snippet}

<style>
  .problem { margin-bottom: 16px; }
  section + section { margin-top: 16px; }
  .grow { flex: 1; min-width: 200px; display: flex; flex-direction: column; gap: 3px; }
  .restrict { font-weight: 600; }
  .dim { opacity: 0.55; }
  .entries, .groups { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 2px; }
  .entries li, .groups li {
    display: flex; align-items: center; gap: 8px; padding: 5px 8px; border-radius: var(--radius-small);
  }
  .entries li { background: var(--surface-2); }
  .entries li + li { margin-top: 2px; }
  .entries :global(svg) { color: var(--text-muted); flex: none; }
  .name { flex: 1; min-width: 0; overflow-wrap: anywhere; font-family: var(--font-mono, ui-monospace, monospace); font-size: 12.5px; }
  .groups { max-height: 260px; overflow-y: auto; }
  .groups li:hover { background: var(--surface-2); }
  .icon-only { padding: 3px 7px; }
  .add { margin-top: 10px; flex-wrap: wrap; }
  .add input { flex: 1 1 240px; width: auto; min-width: 0; }
  .add select { flex: none; width: auto; }
  .contact { margin-top: 6px; display: flex; flex-direction: column; gap: 4px; max-width: 420px; }
  .filter { max-width: 280px; }
  .savebar {
    margin-top: 18px; display: flex; align-items: center; gap: 10px;
    padding: 10px 14px; border-radius: var(--radius); background: var(--surface); border: 1px solid var(--border);
  }
  /* Only unsaved changes follow the scrolling, otherwise the bar would cover the group list. */
  .savebar.dirty { position: sticky; bottom: 0; border-color: var(--accent); box-shadow: 0 -6px 18px -12px color-mix(in srgb, var(--accent) 60%, transparent); }
</style>

<script lang="ts">
  // The block list: whoever is on it gets nothing, whatever role or group says otherwise.
  import { fade } from 'svelte/transition';
  import { api, run, toast } from '../app.svelte';
  import { t } from '../i18n.svelte';
  import type { AccessEntry, AccessLookup } from '../types';
  import Icon from './Icon.svelte';

  let { blocked = $bindable(), editable }: { blocked: AccessEntry[]; editable: boolean } = $props();

  let input = $state('');
  let kind = $state<AccessEntry['kind']>('user');

  async function add() {
    const name = input.trim();
    if (!name) return;
    const found = await run(() => api<AccessLookup>('access.lookup', { name }));
    if (!found) return;
    if (found.found === false) {
      toast('error', t('access.notFound', { name }));
      return;
    }
    const entry: AccessEntry = { name: found.name, kind: found.kind ?? kind };
    if (blocked.some((e) => e.name.toLowerCase() === entry.name.toLowerCase())) return;
    blocked = [...blocked, entry];
    input = '';
  }
</script>

<section class="card stack">
  <div class="card-head">
    <Icon name="shield" />
    <div class="grow"><h2>{t('access.blocked.title')}</h2><p class="small muted">{t('access.blocked.hint')}</p></div>
  </div>
  {#if blocked.length}
    <ul class="list">
      {#each blocked as e (e.kind + e.name)}
        <li in:fade={{ duration: 120 }}>
          <Icon name={e.kind === 'group' ? 'users' : 'user'} size={14} />
          <span class="name">{e.name}</span>
          <span class="small muted">{t(`access.${e.kind}`)}</span>
          <button class="btn small ghost" disabled={!editable} onclick={() => (blocked = blocked.filter((x) => x !== e))}>{t('access.blocked.unblock')}</button>
        </li>
      {/each}
    </ul>
  {:else}
    <p class="small muted">{t('access.blocked.empty')}</p>
  {/if}
  <form class="row add" onsubmit={(ev) => { ev.preventDefault(); void add(); }}>
    <input type="text" bind:value={input} placeholder={t('access.addPlaceholder')} disabled={!editable} spellcheck="false" autocomplete="off" aria-label={t('access.addPlaceholder')} />
    <select bind:value={kind} disabled={!editable} aria-label={t('access.user')}>
      <option value="user">{t('access.user')}</option>
      <option value="group">{t('access.group')}</option>
    </select>
    <button class="btn small danger" type="submit" disabled={!editable || !input.trim()}><Icon name="plus" size={14} />{t('access.blocked.add')}</button>
  </form>
</section>

<style>
  .grow { flex: 1; min-width: 200px; display: flex; flex-direction: column; gap: 3px; }
  .list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 2px; }
  .list li { display: flex; align-items: center; gap: 8px; padding: 6px 8px; border-radius: var(--radius-small); background: var(--error-soft); }
  .list li :global(svg) { color: var(--error); flex: none; }
  .name { flex: 1; min-width: 0; overflow-wrap: anywhere; font-family: var(--mono); font-size: 12.5px; }
  .add { flex-wrap: nowrap; }
  .add input { flex: 1; min-width: 0; }
  .add select { width: auto; }
</style>

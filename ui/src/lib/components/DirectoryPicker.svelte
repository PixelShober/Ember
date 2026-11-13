<script lang="ts">
  // Pick users and groups from the Active Directory: type two letters, Ember searches the directory
  // with your own login and suggests matches. Arrow keys and Enter pick; a name typed in full still
  // works without the list (checked in the directory when it goes in).
  import { api, run, toast } from '../app.svelte';
  import { t } from '../i18n.svelte';
  import type { AccessEntry, AccessLookup } from '../types';
  import Icon from './Icon.svelte';

  type Hit = { name: string; kind: AccessEntry['kind']; display: string | null; detail: string | null };

  let { onpick, disabled = false, defaultKind = 'group', label, actionLabel, danger = false }: {
    onpick: (entry: AccessEntry) => void;
    disabled?: boolean;
    /** For a name typed by hand that the directory cannot check right now (no VPN). */
    defaultKind?: AccessEntry['kind'];
    label: string;
    actionLabel: string;
    danger?: boolean;
  } = $props();

  let query = $state('');
  let hits = $state<Hit[]>([]);
  let open = $state(false);
  let active = $state(-1);
  let searching = $state(false);
  let timer: ReturnType<typeof setTimeout> | undefined;
  let seq = 0;
  const id = `dir-${Math.random().toString(36).slice(2, 8)}`;

  function search() {
    clearTimeout(timer);
    const q = query.trim();
    if (q.length < 2) {
      hits = [];
      open = false;
      return;
    }
    timer = setTimeout(async () => {
      const mine = ++seq;
      searching = true;
      const r = await api<Hit[]>('access.search', { query: q }).catch(() => []);
      if (mine !== seq) return;
      searching = false;
      hits = r;
      active = r.length ? 0 : -1;
      open = true;
    }, 250);
  }

  function pick(hit: Hit) {
    onpick({ name: hit.name, kind: hit.kind });
    reset();
  }

  function reset() {
    query = '';
    hits = [];
    open = false;
    active = -1;
  }

  /** Enter without a suggestion: the name as typed, checked in the directory. */
  async function typed() {
    const name = query.trim();
    if (!name) return;
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
    const entry: AccessEntry = { name: found.name, kind: found.kind ?? defaultKind };
    onpick(entry);
    reset();
  }

  function onKey(e: KeyboardEvent) {
    if (e.key === 'ArrowDown' && hits.length) {
      e.preventDefault();
      open = true;
      active = (active + 1) % hits.length;
    } else if (e.key === 'ArrowUp' && hits.length) {
      e.preventDefault();
      active = (active - 1 + hits.length) % hits.length;
    } else if (e.key === 'Enter') {
      e.preventDefault();
      if (open && active >= 0 && hits[active]) pick(hits[active]);
      else void typed();
    } else if (e.key === 'Escape') {
      open = false;
    }
  }
</script>

<div class="picker">
  <div class="row field-row">
    <div class="combo">
      <Icon name="users" size={14} />
      <input type="text" bind:value={query} oninput={search} onkeydown={onKey} onfocus={() => (open = hits.length > 0)} onblur={() => setTimeout(() => (open = false), 150)}
        placeholder={label} {disabled} spellcheck="false" autocomplete="off" aria-label={label}
        role="combobox" aria-expanded={open} aria-controls={id} aria-autocomplete="list" aria-activedescendant={active >= 0 ? `${id}-${active}` : undefined} />
      {#if searching}<span class="spinner" aria-hidden="true"></span>{/if}
    </div>
    <button class="btn small {danger ? 'danger' : ''}" type="button" disabled={disabled || !query.trim()} onclick={() => (open && active >= 0 && hits[active] ? pick(hits[active]) : typed())}>
      <Icon name="plus" size={14} />{actionLabel}
    </button>
  </div>
  {#if open}
    <ul class="list" id={id} role="listbox">
      {#each hits as hit, i (hit.name)}
        <li id="{id}-{i}" role="option" aria-selected={i === active} class:active={i === active}
          onmousedown={(e) => { e.preventDefault(); pick(hit); }} onmouseenter={() => (active = i)}>
          <Icon name={hit.kind === 'group' ? 'users' : 'user'} size={15} />
          <span class="text">
            <span class="main">{hit.display ?? hit.name}</span>
            <span class="sub">{hit.name}{#if hit.detail} · {hit.detail}{/if}</span>
          </span>
          <span class="kind">{t(`access.${hit.kind}`)}</span>
        </li>
      {:else}
        <li class="empty">{t('access.searchEmpty', { query: query.trim() })}</li>
      {/each}
    </ul>
  {/if}
</div>

<style>
  .picker { position: relative; }
  .field-row { flex-wrap: nowrap; }
  .combo { position: relative; flex: 1; min-width: 0; display: flex; align-items: center; }
  .combo > :global(svg) { position: absolute; left: 10px; color: var(--text-muted); pointer-events: none; }
  .combo input { width: 100%; padding-left: 30px; }
  .spinner {
    position: absolute; right: 10px; width: 12px; height: 12px; border-radius: 50%;
    border: 2px solid var(--border-strong); border-top-color: var(--accent); animation: spin 700ms linear infinite;
  }
  @keyframes spin { to { transform: rotate(360deg); } }
  .list {
    position: absolute; z-index: 20; left: 0; right: 0; top: calc(100% + 4px); margin: 0; padding: 4px; list-style: none;
    max-height: 300px; overflow-y: auto; background: var(--surface); border: 1px solid var(--border);
    border-radius: var(--radius-small); box-shadow: var(--shadow-lift);
  }
  .list li { display: flex; align-items: center; gap: 10px; padding: 7px 10px; border-radius: 6px; cursor: pointer; }
  .list li > :global(svg) { color: var(--text-muted); flex: none; }
  .list li.active { background: var(--accent-soft); }
  .list li.active > :global(svg) { color: var(--accent-fg); }
  .text { flex: 1; min-width: 0; display: flex; flex-direction: column; }
  .main { font-size: 13.5px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .sub { font-family: var(--mono); font-size: 11.5px; color: var(--text-muted); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .kind { font-size: 11.5px; color: var(--text-muted); }
  .empty { color: var(--text-muted); font-size: 13px; cursor: default; }
  @media (prefers-reduced-motion: reduce) { .spinner { animation-duration: 2s; } }
</style>

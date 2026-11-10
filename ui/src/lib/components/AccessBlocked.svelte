<script lang="ts">
  // The block list: whoever is on it gets nothing, whatever role or group says otherwise.
  import { fade } from 'svelte/transition';
  import { t } from '../i18n.svelte';
  import type { AccessEntry } from '../types';
  import Icon from './Icon.svelte';
  import DirectoryPicker from './DirectoryPicker.svelte';

  let { blocked = $bindable(), editable }: { blocked: AccessEntry[]; editable: boolean } = $props();

  function block(entry: AccessEntry) {
    if (blocked.some((e) => e.name.toLowerCase() === entry.name.toLowerCase())) return;
    blocked = [...blocked, entry];
  }
</script>

<section class="card stack">
  <div class="card-head">
    <Icon name="shield" />
    <div class="grow"><h2>{t('access.blocked.title')}</h2></div>
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
  <DirectoryPicker onpick={block} disabled={!editable} defaultKind="user" label={t('access.addPlaceholder')} actionLabel={t('access.blocked.add')} danger />
</section>

<style>
  .grow { flex: 1; min-width: 200px; display: flex; flex-direction: column; gap: 3px; }
  .list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 2px; }
  .list li { display: flex; align-items: center; gap: 8px; padding: 6px 8px; border-radius: var(--radius-small); background: var(--error-soft); }
  .list li :global(svg) { color: var(--error); flex: none; }
  .name { flex: 1; min-width: 0; overflow-wrap: anywhere; font-family: var(--mono); font-size: 12.5px; }
</style>

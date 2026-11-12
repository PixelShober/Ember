<script lang="ts">
  // A long list of short values (domains, IP ranges) that starts folded: the first few and how
  // many more there are; one click shows all.
  import { t } from '../i18n.svelte';
  import Icon from './Icon.svelte';

  let { items, tone = '', preview = 3 }: { items: string[]; tone?: string; preview?: number } = $props();
  let open = $state(false);
  const shown = $derived(open ? items : items.slice(0, preview));
  const rest = $derived(items.length - preview);
</script>

<span class="chips">
  {#each shown as item (item)}<span class="badge {tone}">{item}</span>{/each}
  {#if rest > 0}
    <button class="more" aria-expanded={open} onclick={() => (open = !open)}>
      {open ? t('common.showLess') : t('common.showMore', { count: rest })}
      <Icon name="chevron" size={12} />
    </button>
  {/if}
</span>

<style>
  .chips { display: inline-flex; flex-wrap: wrap; gap: 4px; align-items: center; }
  .more {
    display: inline-flex; align-items: center; gap: 4px; padding: 1px 8px; border: 1px dashed var(--border-strong); border-radius: 999px;
    background: none; color: var(--text-muted); font: inherit; font-size: 12px; cursor: pointer;
    transition: color 150ms ease-out, border-color 150ms ease-out;
  }
  .more:hover { color: var(--text); border-color: var(--accent); }
  .more :global(svg) { transform: rotate(90deg); transition: transform 150ms ease-out; }
  .more[aria-expanded='true'] :global(svg) { transform: rotate(-90deg); }
  @media (prefers-reduced-motion: reduce) { .more :global(svg) { transition: none; } }
</style>

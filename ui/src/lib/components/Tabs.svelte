<script lang="ts" generics="T extends string">
  // A row of tabs under the page title (WAI-ARIA tabs: arrow keys move, the panel follows).
  // The choice lives in sessionStorage per page, so going away and back keeps it.
  import { t } from '../i18n.svelte';

  let { tabs, value = $bindable(), labelKey, id, counts = {} }: {
    tabs: readonly T[];
    value: T;
    /** Locale prefix, e.g. "playwright.tabs" → t("playwright.tabs.project"). */
    labelKey: string;
    id: string;
    /** Small number next to a tab label (e.g. unsaved changes). */
    counts?: Partial<Record<T, number>>;
  } = $props();

  const key = () => `ember.tab.${id}`;
  // Once, when the page opens: the tab from the last visit.
  $effect.pre(() => {
    try {
      const stored = sessionStorage.getItem(key()) as T | null;
      if (stored && tabs.includes(stored)) value = stored;
    } catch {
      // storage blocked: start with the first tab
    }
    return () => {};
  });

  function show(next: T) {
    value = next;
    try {
      sessionStorage.setItem(key(), next);
    } catch {
      // private mode: the tab is not remembered
    }
  }

  function onKey(e: KeyboardEvent) {
    const i = tabs.indexOf(value);
    const next = e.key === 'ArrowRight' ? tabs[(i + 1) % tabs.length] : e.key === 'ArrowLeft' ? tabs[(i + tabs.length - 1) % tabs.length] : null;
    if (!next) return;
    e.preventDefault();
    show(next);
    document.getElementById(`${id}-tab-${next}`)?.focus();
  }
</script>

<div class="tabs" role="tablist" tabindex="-1" onkeydown={onKey}>
  {#each tabs as tab (tab)}
    <button id="{id}-tab-{tab}" role="tab" aria-selected={value === tab} aria-controls="{id}-panel" tabindex={value === tab ? 0 : -1} class:active={value === tab} onclick={() => show(tab)}>
      {t(`${labelKey}.${tab}`)}
      {#if counts[tab]}<span class="count">{counts[tab]}</span>{/if}
    </button>
  {/each}
</div>

<style>
  .tabs { display: flex; gap: 4px; margin: -4px 0 16px; border-bottom: 1px solid var(--border); overflow-x: auto; }
  .tabs button {
    position: relative; display: inline-flex; align-items: center; gap: 6px; padding: 8px 14px 10px; border: 0; background: none; cursor: pointer;
    font: inherit; font-size: 14px; color: var(--text-muted); white-space: nowrap; border-radius: var(--radius-small) var(--radius-small) 0 0;
    transition: color 150ms ease-out, background-color 150ms ease-out;
  }
  .tabs button:hover { color: var(--text); background: var(--surface-2); }
  .tabs button.active { color: var(--text); font-weight: 600; }
  .tabs button::after {
    content: ''; position: absolute; left: 10px; right: 10px; bottom: -1px; height: 2px; border-radius: 2px;
    background: var(--accent); transform: scaleX(0); transition: transform 200ms cubic-bezier(0.16, 1, 0.3, 1);
  }
  .tabs button.active::after { transform: scaleX(1); }
  .tabs button:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }
  .count {
    min-width: 18px; padding: 0 5px; border-radius: 999px; font-size: 11px; font-weight: 600; line-height: 18px; text-align: center;
    background: var(--accent-soft); color: var(--accent-fg);
  }
  @media (prefers-reduced-motion: reduce) { .tabs button::after { transition: none; } }
</style>

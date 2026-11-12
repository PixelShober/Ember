<script lang="ts" module>
  import type { Route } from '../types';

  /** Which handbook page belongs to which tab. */
  const pages: Record<Route, string> = {
    start: 'handbuch/erste-schritte',
    proxy: 'handbuch/proxy',
    git: 'handbuch/git',
    software: 'handbuch/software',
    playwright: 'handbuch/playwright',
    system: 'handbuch/system',
    power: 'handbuch/energie',
    ai: 'handbuch/ki',
    access: 'handbuch/berechtigungen',
    settings: 'handbuch/einstellungen',
    log: 'handbuch/fehlerbehebung',
  };

  export const handbook = $state<{ open: boolean; page: string | null }>({ open: false, page: null });

  /** Opens the handbook at the page of a tab (or a page like "handbuch/team-konfiguration"). */
  export function openHandbook(target: Route | string) {
    handbook.page = (pages as Record<string, string>)[target] ?? target;
    handbook.open = true;
  }
</script>

<script lang="ts">
  // The Starlight handbook, built into the UI under /Ember/ (scripts/embed-docs.mjs), in a frame over
  // the app. Ctrl+K opens and closes it; inside, Ctrl+K is Starlight's own search. It always comes
  // from Ember itself (works offline); the Vite dev server has no handbook and says so.
  import { fade, scale } from 'svelte/transition';
  import { t } from '../i18n.svelte';
  import Icon from './Icon.svelte';
  import { modal } from '../modal';

  let local = $state<boolean | null>(null);
  let frame = $state<HTMLIFrameElement>();

  $effect(() => {
    if (!handbook.open || local !== null) return;
    fetch('/Ember/index.html', { method: 'HEAD' }).then((r) => (local = r.ok)).catch(() => (local = false));
  });

  const src = $derived(`/Ember/${handbook.page ?? 'handbuch/erste-schritte'}/`);

  // Starlight keeps its colour scheme in localStorage; same origin, so it follows Ember.
  $effect(() => {
    if (!handbook.open) return;
    try {
      localStorage.setItem('starlight-theme', document.documentElement.classList.contains('dark') ? 'dark' : 'light');
    } catch {
      // storage blocked: Starlight picks its own scheme
    }
  });

  function close() {
    handbook.open = false;
  }

  /** Esc and Ctrl+K inside the frame (same origin) close it too, unless Starlight's search is open. */
  function wire() {
    const doc = frame?.contentDocument;
    if (!doc) return;
    doc.addEventListener('keydown', (e) => {
      if (e.key === 'Escape' && !doc.querySelector('dialog[open]')) close();
    });
  }

</script>

{#if handbook.open}
  <div class="layer" transition:fade={{ duration: 120 }}>
    <div class="backdrop" onclick={close} role="presentation"></div>
    <div class="sheet" role="dialog" aria-modal="true" aria-label={t('handbook.title')} use:modal={close} transition:scale={{ start: 0.97, duration: 160 }}>
      <header>
        <Icon name="book" size={17} />
        <strong>{t('handbook.title')}</strong>
        <span class="small muted hint">{t('handbook.hint')}</span>
        <span class="spacer"></span>
        <button class="btn small ghost icon-only" onclick={close} title={t('common.close')} aria-label={t('common.close')}><Icon name="x" size={16} /></button>
      </header>
      {#if local === null}
        <div class="wait muted small">{t('common.loading')}</div>
      {:else if !local}
        <div class="wait muted small">{t('handbook.missing')}</div>
      {:else}
        <iframe bind:this={frame} {src} title={t('handbook.title')} onload={wire}></iframe>
      {/if}
    </div>
  </div>
{/if}

<style>
  .layer { position: fixed; inset: 0; z-index: 45; display: grid; place-items: center; padding: 20px; }
  .backdrop { position: absolute; inset: 0; background: var(--backdrop); }
  .sheet {
    position: relative; width: min(1180px, 100%); height: min(900px, 100%);
    display: flex; flex-direction: column; overflow: hidden;
    background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius);
    box-shadow: var(--shadow-lift), 0 24px 60px -12px rgb(0 0 0 / 0.3);
  }
  header { display: flex; align-items: center; gap: 10px; padding: 8px 10px 8px 16px; border-bottom: 1px solid var(--border); }
  header :global(svg) { color: var(--accent-fg, var(--accent)); }
  .hint { margin-left: 4px; }
  .spacer { flex: 1; }
  .icon-only { padding: 3px 7px; }
  iframe { flex: 1; width: 100%; border: 0; background: var(--surface); }
  .wait { flex: 1; display: grid; place-items: center; }
  @media (max-width: 640px) { .hint { display: none; } }
</style>

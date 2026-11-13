<script lang="ts" module>
  export const tour = $state({ open: false, index: 0 });

  /** Starts the guided tour at its first step (the button on the start page, and the very first start). */
  export function startTour() {
    tour.index = 0;
    tour.open = true;
  }
</script>

<script lang="ts">
  // The guided tour: the page dims, one spot stays bright, and a card next to it says what you do
  // there. The tour changes tabs by itself. It is a dialog like the others (lib/modal.ts): focus stays
  // in the card, Esc ends it, and the arrow keys turn the pages. The steps are in lib/tour.ts.
  import { tick, untrack } from 'svelte';
  import { fade, scale } from 'svelte/transition';
  import { app, canOpen, navigate } from '../app.svelte';
  import { t } from '../i18n.svelte';
  import { modal } from '../modal';
  import { isTourDone, markTourDone, placeCard, visibleSteps, type Box } from '../tour';
  import Icon from './Icon.svelte';

  const steps = $derived(tour.open ? visibleSteps(canOpen) : []);
  const index = $derived(Math.min(tour.index, Math.max(0, steps.length - 1)));
  const step = $derived(steps[index]);
  const first = $derived(index === 0);
  const last = $derived(index === steps.length - 1);

  let view = $state({ width: 0, height: 0 });
  let size = $state({ width: 0, height: 0 });
  /** The bright spot, in window coordinates. null: nothing to point at, the card sits in the middle. */
  let box = $state<Box | null>(null);
  /** For screen readers: the step that just opened (the focus stays on the button). */
  let spoken = $state('');
  let card = $state<HTMLElement>();

  const reduced = () => matchMedia('(prefers-reduced-motion: reduce)').matches;
  const narrow = $derived(view.width <= 720);
  const beside = $derived(box && !narrow && size.width ? placeCard(box, size, view) : null);
  const docked = $derived(narrow || (!!box && !beside));
  const at = $derived(beside ?? { left: (view.width - size.width) / 2, top: Math.max(12, (view.height - size.height) / 2) });
  const spot = $derived(box ?? { left: view.width / 2, top: view.height / 2, width: 0, height: 0 });

  // The first start: once the app is open and this account has access.
  let checked = false;
  $effect(() => {
    if (checked || !app.ready || !app.info?.access) return;
    checked = true;
    if (!isTourDone()) startTour();
  });

  let target: HTMLElement | null = null;
  let fresh = true;
  let waitingSince = 0;

  /** A step opens: go to its tab, and tell a screen reader. */
  function enter() {
    const s = steps[index];
    if (!s) return;
    target = null;
    fresh = true;
    waitingSince = performance.now();
    if (app.route !== s.route) navigate(s.route);
    spoken = '';
    setTimeout(() => (spoken = `${t('tour.progress', { n: index + 1, total: steps.length })}: ${t(s.title)}. ${t(s.text)}`), 80);
    void keepFocus();
  }

  /** A button that went away (Zurück on the first step) must not take the focus with it. */
  async function keepFocus() {
    await tick();
    if (card && !card.contains(document.activeElement)) card.querySelector<HTMLElement>('.next')?.focus();
  }

  function go(next: number) {
    if (next < 0 || next >= steps.length || next === index) return;
    tour.index = next;
    enter();
  }

  function finish() {
    if (!tour.open) return;
    tour.open = false;
    markTourDone();
    navigate('start');
  }

  /** After the tour the focus goes to the button that starts it again. */
  function restoreFocus(tries = 20) {
    const button = document.querySelector<HTMLElement>('[data-tour="start-tour"]');
    if (button) button.focus();
    else if (tries > 0) setTimeout(() => restoreFocus(tries - 1), 50);
  }

  /** What of the target is inside the window, with a little air around it. */
  function visible(r: DOMRect): Box | null {
    const pad = 6;
    const edge = 4;
    const left = Math.max(r.left - pad, edge);
    const top = Math.max(r.top - pad, edge);
    const right = Math.min(r.right + pad, view.width - edge);
    const bottom = Math.min(r.bottom + pad, view.height - edge);
    return right - left > 8 && bottom - top > 8 ? { left, top, width: right - left, height: bottom - top } : null;
  }

  const same = (a: Box | null, b: Box | null) =>
    a === b || (!!a && !!b && Math.abs(a.left - b.left) < 0.5 && Math.abs(a.top - b.top) < 0.5 && Math.abs(a.width - b.width) < 0.5 && Math.abs(a.height - b.height) < 0.5);

  // Pages are rebuilt on every tab change and fill in as their data arrives, so the spot follows its
  // target frame by frame instead of being measured once.
  function follow() {
    const s = step;
    if (!s) return;
    const width = document.documentElement.clientWidth;
    const height = window.innerHeight;
    if (width !== view.width || height !== view.height) view = { width, height };

    let next: Box | null = null;
    if (s.target && app.route === s.route) {
      if (s.select) {
        const tab = document.querySelector<HTMLElement>(s.select);
        if (tab?.getAttribute('aria-selected') === 'false') tab.click();
      }
      if (!target?.isConnected) target = document.querySelector<HTMLElement>(s.target);
      if (target) {
        if (fresh) {
          fresh = false;
          const tall = target.getBoundingClientRect().height > height * 0.6;
          target.scrollIntoView({ block: tall || width <= 720 ? 'start' : 'center', inline: 'nearest' });
        }
        next = visible(target.getBoundingClientRect());
      }
    }
    // The page may still be loading: keep the old spot for a moment instead of jumping to the middle and back.
    if (!next && s.target && performance.now() - waitingSince < 600) return;
    if (!same(next, box)) box = next;
  }

  $effect(() => {
    if (!tour.open) return;
    untrack(enter);
    // Not followed here: this effect must only run when the tour opens, not on every step.
    let frame = requestAnimationFrame(function loop() {
      follow();
      frame = requestAnimationFrame(loop);
    });
    return () => cancelAnimationFrame(frame);
  });

  function onKey(e: KeyboardEvent) {
    if (!tour.open || e.ctrlKey || e.altKey || e.metaKey) return;
    if (e.key === 'ArrowRight') go(index + 1);
    else if (e.key === 'ArrowLeft') go(index - 1);
    else return;
    e.preventDefault();
  }
</script>

<svelte:window onkeydown={onKey} />

{#if tour.open && step}
  <div class="layer" transition:fade={{ duration: reduced() ? 0 : 140 }} onoutroend={() => setTimeout(restoreFocus)}>
    <div class="spot" class:none={!box} style:left="{spot.left}px" style:top="{spot.top}px" style:width="{spot.width}px" style:height="{spot.height}px"></div>

    <div class="anchor" class:docked class:measured={size.width > 0} style:transform={docked ? undefined : `translate(${Math.round(at.left)}px, ${Math.round(at.top)}px)`} bind:offsetWidth={size.width} bind:offsetHeight={size.height}>
      <div class="tour-card" bind:this={card} role="dialog" aria-modal="true" aria-labelledby="tour-title" aria-describedby="tour-text" use:modal={finish} transition:scale={{ start: 0.96, duration: reduced() ? 0 : 160 }}>
        <div class="flame"></div>
        <div class="body">
          <div class="head" class:hero={!step.icon}>
            {#if step.icon}
              <span class="chip"><Icon name={step.icon} size={20} /></span>
            {:else}
              <img src="./favicon.svg" alt="" width="52" height="52" />
            {/if}
            <div>
              {#if step.icon}<span class="where small">{t(`nav.${step.route}`)}</span>{/if}
              <h2 id="tour-title">{t(step.title)}</h2>
            </div>
          </div>
          <p id="tour-text">{t(step.text)}</p>
          {#if first}<p class="small muted">{t('tour.keys')}</p>{/if}

          <div class="progress-row">
            <span class="dots" aria-hidden="true">
              {#each steps as s, i (s.id)}<span class:now={i === index} class:seen={i < index}></span>{/each}
            </span>
            <span class="small muted">{t('tour.progress', { n: index + 1, total: steps.length })}</span>
          </div>

          <div class="buttons">
            {#if !last}<button class="btn ghost" onclick={finish}>{t('tour.skip')}</button>{/if}
            <span class="spacer"></span>
            {#if !first}<button class="btn" onclick={() => go(index - 1)}>{t('tour.back')}</button>{/if}
            <!-- Focus starts on the way forward, not on "skip". -->
            <!-- svelte-ignore a11y_autofocus -->
            <button class="btn primary next" autofocus onclick={() => (last ? finish() : go(index + 1))}>
              {last ? t('tour.finish') : first ? t('tour.begin') : t('tour.next')}
              {#if !last}<Icon name="chevron" size={15} />{/if}
            </button>
          </div>
          <p class="visually-hidden" aria-live="polite" aria-atomic="true">{spoken}</p>
        </div>
      </div>
    </div>
  </div>
{/if}

<style>
  /* Over the dialogs and the handbook (40, 45), under the toasts (50). It catches every click, so the
     page behind cannot be used by accident. Own layer in a page change, so it does not fade with the page. */
  .layer { position: fixed; inset: 0; z-index: 48; overflow: hidden; view-transition-name: tour; }

  /* The bright spot: everything around it is its shadow. Without a target it shrinks to nothing in the
     middle, which leaves the whole window dimmed. */
  .spot {
    position: absolute; border-radius: var(--radius); pointer-events: none;
    box-shadow: 0 0 0 2px var(--accent), 0 0 0 6px color-mix(in srgb, var(--accent) 30%, transparent), 0 0 0 200vmax var(--backdrop);
    transition: left 280ms cubic-bezier(0.16, 1, 0.3, 1), top 280ms cubic-bezier(0.16, 1, 0.3, 1), width 280ms cubic-bezier(0.16, 1, 0.3, 1), height 280ms cubic-bezier(0.16, 1, 0.3, 1);
  }
  .spot.none { box-shadow: 0 0 0 200vmax var(--backdrop); }

  .anchor { position: absolute; left: 0; top: 0; width: min(380px, calc(100vw - 24px)); opacity: 0; }
  /* Not visibility: a hidden button cannot take the focus. */
  .anchor.measured { opacity: 1; transition: transform 280ms cubic-bezier(0.16, 1, 0.3, 1); }
  /* Narrow window, large zoom, or no room next to the target: a bar at the bottom. */
  .anchor.docked { left: 12px; right: 12px; top: auto; bottom: 12px; width: auto; max-width: 560px; margin-inline: auto; transition: none; }

  .tour-card {
    background: var(--surface); color: var(--text); border: 1px solid var(--border); border-radius: var(--radius);
    box-shadow: var(--shadow-lift), 0 24px 60px -12px rgb(0 0 0 / 0.35);
    max-height: calc(100vh - 24px); overflow: hidden auto;
  }
  .tour-card:focus { outline: none; }
  .flame { height: 4px; background: var(--ember); }
  .body { padding: 18px 20px 16px; display: flex; flex-direction: column; gap: 12px; }
  .head { display: flex; align-items: center; gap: 12px; }
  .head.hero { flex-direction: column; align-items: flex-start; gap: 10px; }
  .head.hero h2 { font-size: 20px; }
  .head > div { display: flex; flex-direction: column; gap: 1px; min-width: 0; }
  .chip { flex: none; width: 38px; height: 38px; border-radius: 10px; display: grid; place-items: center; background: var(--accent-soft); color: var(--accent-fg); }
  .where { color: var(--accent-fg); font-weight: 600; }
  .body p { line-height: 1.5; }

  .progress-row { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; margin-top: 2px; }
  .dots { display: inline-flex; gap: 5px; }
  .dots span { width: 7px; height: 7px; border-radius: 99px; background: var(--border-strong); transition: width 200ms cubic-bezier(0.16, 1, 0.3, 1), background-color 200ms; }
  .dots .seen { background: color-mix(in srgb, var(--accent) 55%, var(--border-strong)); }
  .dots .now { width: 20px; background: var(--accent); }

  .buttons { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
  .buttons .btn { min-height: 44px; padding-inline: 16px; }
  .buttons .ghost { margin-left: -10px; color: var(--text-muted); }
  .spacer { flex: 1; }
</style>

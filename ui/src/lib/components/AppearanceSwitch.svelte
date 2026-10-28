<script lang="ts">
  // Colour scheme (System / Light / Dark) and design (Efeu / Glut) as native radio groups:
  // arrow keys move within a group, Tab leaves it. The new look grows from the chosen option.
  import { onMount } from 'svelte';
  import { api } from '../app.svelte';
  import { t } from '../i18n.svelte';
  import { applyWithReveal, current, type Appearance, type Design, type Scheme } from '../appearance';
  import Icon from './Icon.svelte';

  /** rail: icons only (sidebar); card: with labels (settings). */
  let { variant = 'rail' }: { variant?: 'rail' | 'card' } = $props();
  const id = $props.id();

  const schemes: { value: Scheme; icon: string }[] = [
    { value: 'system', icon: 'monitor' },
    { value: 'light', icon: 'sun' },
    { value: 'dark', icon: 'moon' },
  ];
  const designs: { value: Design; icon: string }[] = [
    { value: 'efeu', icon: 'leaf' },
    { value: 'glut', icon: 'flame' },
  ];

  let appearance = $state<Appearance>({ scheme: 'system', design: 'efeu' });
  onMount(() => {
    appearance = current();
    const sync = (e: Event) => (appearance = (e as CustomEvent<Appearance>).detail);
    window.addEventListener('ember:appearance', sync);
    return () => window.removeEventListener('ember:appearance', sync);
  });

  // Ember.exe stores it as well (title bar, window background, survives a cache wipe).
  const tellHost = (a: Appearance) => void api('app.setAppearance', { ...a }).catch(() => {});

  function choose(next: Partial<Appearance>, origin: Element) {
    applyWithReveal({ ...appearance, ...next }, origin, tellHost);
  }
</script>

<div class="appearance {variant}">
  <fieldset>
    <legend>{t('appearance.scheme')}</legend>
    {#each schemes as o (o.value)}
      <label title={t(`appearance.schemes.${o.value}`)}>
        <input type="radio" name="scheme-{id}" value={o.value} checked={appearance.scheme === o.value}
          onchange={(e) => choose({ scheme: o.value }, e.currentTarget)} />
        <span><Icon name={o.icon} size={16} />{#if variant === 'card'}<span class="text">{t(`appearance.schemes.${o.value}`)}</span>{/if}</span>
      </label>
    {/each}
  </fieldset>
  <fieldset>
    <legend>{t('appearance.design')}</legend>
    {#each designs as o (o.value)}
      <label title={t(`appearance.designs.${o.value}`)}>
        <input type="radio" name="design-{id}" value={o.value} checked={appearance.design === o.value}
          onchange={(e) => choose({ design: o.value }, e.currentTarget)} />
        <span><Icon name={o.icon} size={16} />{#if variant === 'card'}<span class="text">{t(`appearance.designs.${o.value}`)}</span>{/if}</span>
      </label>
    {/each}
  </fieldset>
</div>

<style>
  .appearance { display: flex; gap: 6px; flex-wrap: wrap; }
  fieldset {
    display: inline-flex; gap: 2px; margin: 0; padding: 3px; min-width: 0;
    border: 1px solid var(--border); border-radius: var(--radius-small); background: var(--surface);
  }
  legend { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
  label { position: relative; }
  input { position: absolute; inset: 0; margin: 0; opacity: 0; cursor: pointer; }
  label > span {
    display: flex; align-items: center; gap: 6px; min-height: 28px; padding: 0 7px; border-radius: 6px;
    color: var(--text-muted); white-space: nowrap; font-size: 12.5px; font-weight: 500;
    transition: background-color 150ms ease-out, color 150ms ease-out;
  }
  input:checked + span { background: var(--accent-soft); color: var(--accent-fg); }
  input:hover + span { color: var(--text); }
  input:focus-visible + span { outline: 2px solid var(--accent); outline-offset: 1px; }
  .rail label > span { padding: 0 6px; min-height: 26px; }
  .card label > span { min-height: 34px; padding: 0 12px; font-size: 13px; }
</style>

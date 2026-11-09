<script lang="ts">
  // Which browsers Playwright gets: Chromium (enough for most tests), all three, or the Edge that is
  // already installed (no download at all).
  import { t } from '../i18n.svelte';
  import type { PlaywrightMode } from '../types';

  let { mode = $bindable(), edgeFound, name }: { mode: PlaywrightMode; edgeFound: boolean | undefined; name: string } = $props();
</script>

<fieldset class="options">
  <legend class="small muted">{t('playwright.browsersLabel')}</legend>
  <label class="check"><input type="radio" {name} value="chromium" bind:group={mode} />{t('playwright.browsersChromium')}</label>
  <label class="check"><input type="radio" {name} value="all" bind:group={mode} />{t('playwright.browsersAll')}</label>
  <label class="check" title={edgeFound === false ? t('playwright.edgeMissing') : undefined}>
    <input type="radio" {name} value="msedge" bind:group={mode} disabled={edgeFound === false} />{t('playwright.browsersEdge')}
  </label>
</fieldset>

<style>
  .options { border: 0; padding: 0; margin: 12px 0 0; display: flex; flex-wrap: wrap; gap: 6px 18px; }
  .options legend { padding: 0; margin-bottom: 4px; width: 100%; }
</style>

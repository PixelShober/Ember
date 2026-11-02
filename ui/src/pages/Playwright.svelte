<script lang="ts">
  // Playwright has its own tab: browsers for a project, Playwright added to a project, new projects.
  import { t } from '../lib/i18n.svelte';
  import PlaywrightCard from '../lib/components/PlaywrightCard.svelte';
</script>

<header class="page-head">
  <h1>{t('playwright.title')}</h1>
  <p>{t('playwright.subtitle')}</p>
</header>

<PlaywrightCard />

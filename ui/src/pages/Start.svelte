<script lang="ts">
  import { onMount } from 'svelte';
  import { api, app, copyText, navigate, openMail, run, toast } from '../lib/app.svelte';
  import { t } from '../lib/i18n.svelte';
  import { firstName, formatDate } from '../lib/format';
  import type { GitIdentityInfo, ProxyRow, Route } from '../lib/types';
  import Icon from '../lib/components/Icon.svelte';

  let git = $state<GitIdentityInfo | null>(null);
  let rows = $state<ProxyRow[] | null>(null);

  onMount(async () => {
    [git, rows] = await Promise.all([
      api<GitIdentityInfo>('git.get').catch(() => null),
      api<ProxyRow[]>('proxy.diagnose').catch(() => null),
    ]);
  });

  const name = $derived(firstName(git?.name ?? git?.suggestedName, app.info?.user ?? ''));
  const relevant = $derived(rows?.filter((r) => r.state !== 'info' && r.state !== 'missing') ?? []);
  const okCount = $derived(relevant.filter((r) => r.state === 'px').length);
  const team = $derived(app.info?.team);

  type Card = { route: Route; icon: string; title: string; text: string; tone: 'ok' | 'warn' | 'muted' };
  const cards = $derived<Card[]>([
    {
      route: 'proxy', icon: 'proxy', title: t('start.cards.px'),
      text: app.px?.portOpen ? t('start.pxRunning', { url: app.px.url }) : app.px?.installed ? t('start.pxStopped') : t('start.pxMissing'),
      tone: app.px?.portOpen ? 'ok' : 'warn',
    },
    {
      route: 'proxy', icon: 'check', title: t('start.cards.proxy'),
      text: rows ? t('start.proxySummary', { ok: okCount, total: relevant.length }) : t('common.loading'),
      tone: !rows ? 'muted' : okCount === relevant.length ? 'ok' : 'warn',
    },
    {
      route: 'git', icon: 'git', title: t('start.cards.git'),
      text: git?.name && git?.email ? t('start.gitSet', { name: git.name, email: git.email }) : t('start.gitMissing'),
      tone: git?.name && git?.email ? 'ok' : 'warn',
    },
    {
      route: 'settings', icon: 'folder', title: t('start.cards.team'),
      text: !team?.path ? t('start.teamNone') : team.fromCache ? t('start.teamCache', { date: formatDate(team.loadedAt) }) : t('start.teamOk', { date: formatDate(team.loadedAt) }),
      tone: !team?.path ? 'warn' : team.fromCache || team.error ? 'warn' : 'ok',
    },
    {
      route: 'settings', icon: 'sparkle', title: t('start.cards.update'),
      text: app.update?.uiAvailable || app.update?.exeAvailable ? t('start.updateAvailable', { version: app.update?.latest }) : t('start.updateCurrent', { version: app.info?.version }),
      tone: app.update?.uiAvailable || app.update?.exeAvailable ? 'warn' : 'ok',
    },
  ]);

  async function report() {
    const r = await run(() => api<{ text: string; path: string }>('diagnostics.report'));
    if (r) {
      await copyText(r.text);
      toast('ok', t('start.reportSaved', { path: r.path }));
    }
  }
</script>

<header class="page-head">
  <h1>{t('start.title', { name })}</h1>
  <p>{t('start.subtitle')}</p>
</header>

<section class="grid">
  {#each cards as card, i (i)}
    <button class="card status {card.tone}" onclick={() => navigate(card.route)}>
      <div class="row">
        <span class="icon"><Icon name={card.icon} /></span>
        <h3>{card.title}</h3>
      </div>
      <p class="break">{card.text}</p>
      <span class="go small">{t('start.goTo')} <Icon name="chevron" size={14} /></span>
    </button>
  {/each}
</section>

<section class="stack quick">
  <h2>{t('start.quick')}</h2>
  <div class="row">
    <button class="btn" onclick={report}><Icon name="report" size={16} />{t('start.report')}</button>
    <button class="btn" onclick={() => openMail('problem')}><Icon name="mail" size={16} />{t('start.contact')}</button>
  </div>
</section>

<style>
  .status {
    text-align: left; font: inherit; color: inherit; cursor: pointer;
    display: flex; flex-direction: column; gap: 8px; border-top: 3px solid var(--border-strong);
    transition: transform 0.15s, box-shadow 0.15s;
  }
  .status:hover { transform: translateY(-2px); box-shadow: 0 8px 24px rgb(0 0 0 / 0.08); }
  .status.ok { border-top-color: var(--ok); }
  .status.warn { border-top-color: var(--warn); }
  .icon { width: 30px; height: 30px; border-radius: 8px; display: grid; place-items: center; background: var(--surface-2); color: var(--text-muted); }
  .status.ok .icon { background: var(--ok-soft); color: var(--ok); }
  .status.warn .icon { background: var(--warn-soft); color: var(--warn); }
  .go { margin-top: auto; display: inline-flex; align-items: center; gap: 2px; color: var(--accent-fg); font-weight: 500; }
  .quick { margin-top: 28px; }
</style>

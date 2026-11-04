<script lang="ts">
  import { onMount } from 'svelte';
  import { slide } from 'svelte/transition';
  import { api, confirm, run, toast } from '../lib/app.svelte';
  import { host, t } from '../lib/i18n.svelte';
  import { formatPowerValue } from '../lib/format';
  import type { PowerApplyResult, PowerProfileState, PowerSettingState, PowerState, PowerValue } from '../lib/types';
  import Icon from '../lib/components/Icon.svelte';
  import Calcifer from '../lib/components/Calcifer.svelte';

  let power = $state<PowerState | null>(null);
  let busy = $state<string | null>(null);
  let open = $state<Record<string, boolean>>({});

  async function load() {
    power = (await run(() => api<PowerState>('power.state'))) ?? power;
  }
  onMount(load);

  const statusTone: Record<string, string> = { applied: 'ok', partial: 'warn', notApplied: '', unsupported: '' };

  function fmt(s: PowerSettingState, v: number | null) {
    return formatPowerValue(s.alias, v, s.unit, s.options);
  }

  function sideTouched(v: PowerValue) {
    return v.target !== null;
  }

  function adminDetails(p: PowerProfileState) {
    return p.settings
      .filter((s) => s.supported && !s.locked)
      .flatMap((s) => [
        s.ac.target !== null ? `powercfg /setacvalueindex ${power?.schemeGuid} ${s.subGuid} ${s.guid} ${s.ac.target}` : null,
        s.dc.target !== null ? `powercfg /setdcvalueindex ${power?.schemeGuid} ${s.subGuid} ${s.guid} ${s.dc.target}` : null,
      ])
      .filter(Boolean)
      .join('\n');
  }

  async function change(p: PowerProfileState, method: 'power.apply' | 'power.defaults' | 'power.restore') {
    const text = method === 'power.apply' ? t('power.confirmApply', { name: p.name }) : t('power.confirmDefaults', { name: p.name });
    const label = method === 'power.apply' ? t('power.applyProfile') : method === 'power.defaults' ? t('power.resetDefaults') : t('power.restoreBackup');
    if (!(await confirm(p.name, text, label))) return;
    busy = p.id;
    const r = await run(() => api<PowerApplyResult>(method, { profile: p.id }), undefined, adminDetails(p));
    busy = null;
    if (r) {
      toast(r.errors.length ? 'error' : 'ok', [t('power.applied', { count: r.changed }), r.elevated ? t('power.elevatedNote') : '', ...r.errors].filter(Boolean).join(' · '));
      await load();
    }
  }
</script>

<header class="page-head">
  <h1>{t('power.title')}</h1>
  <p>{t('power.subtitle', { scheme: power?.schemeName ?? t('power.schemes.balanced') })}</p>
</header>

{#if power}
  <div class="row meta">
    <span class="badge {power.activeIsTarget ? 'ok' : 'warn'}"><Icon name="power" size={13} />{t('power.activeScheme', { name: power.activeName || '–' })}</span>
    {#if power.activeSchemeByPolicy}<span class="badge info">{t('power.schemeByPolicy')}</span>{/if}
    <span class="spacer"></span>
    <button class="btn small" onclick={load}><Icon name="refresh" size={14} />{t('common.refresh')}</button>
  </div>
  {#if !power.activeIsTarget}
    <div class="note warn"><Calcifer /><p>{t('power.notActive', { scheme: power.schemeName })}</p></div>
  {/if}

  <div class="stack">
    {#each power.profiles as p (p.id)}
      <section class="card">
        <div class="card-head">
          <h2>{host(p.name)}</h2>
          <span class="badge {statusTone[p.status]}">{t(`power.status.${p.status}`)}</span>
          {#if p.atDefault}<span class="badge">{t('power.atDefault')}</span>{/if}
          <span class="spacer"></span>
          <button class="btn primary small" disabled={busy !== null || p.status === 'applied' || p.status === 'unsupported'} onclick={() => change(p, 'power.apply')}>{t('power.applyProfile')}</button>
          <button class="btn small" disabled={busy !== null || p.atDefault} onclick={() => change(p, 'power.defaults')}><Icon name="undo" size={14} />{t('power.resetDefaults')}</button>
          {#if p.hasBackup}
            <button class="btn ghost small" disabled={busy !== null} onclick={() => change(p, 'power.restore')}>{t('power.restoreBackup')}</button>
          {/if}
        </div>
        {#if p.description}<p class="muted desc">{host(p.description)}</p>{/if}

        <button class="btn ghost small toggle" onclick={() => (open[p.id] = !open[p.id])} aria-expanded={!!open[p.id]}>
          <span class="chev" class:open={open[p.id]}><Icon name="chevron" size={14} /></span>{t('common.details')}
        </button>
        {#if open[p.id]}
          <div transition:slide={{ duration: 180 }}>
            <table class="table">
              <thead>
                <tr>
                  <th>{t('power.setting')}</th>
                  <th colspan="3">{t('power.ac')}</th>
                  <th colspan="3">{t('power.dc')}</th>
                </tr>
                <tr class="sub">
                  <th></th>
                  <th>{t('power.currentValue')}</th><th>{t('power.target')}</th><th>{t('power.factory')}</th>
                  <th>{t('power.currentValue')}</th><th>{t('power.target')}</th><th>{t('power.factory')}</th>
                </tr>
              </thead>
              <tbody>
                {#each p.settings as s (s.guid)}
                  <tr class:dim={!s.supported}>
                    <td>
                      <span class="name">{s.name}</span>
                      <span class="small muted mono">{s.alias}</span>
                      {#if !s.supported}<span class="badge">{t('power.unsupported')}</span>{/if}
                      {#if s.locked}<span class="badge info">{t('power.locked')}</span>{/if}
                    </td>
                    {#each [s.ac, s.dc] as v, i (i)}
                      <td class:hit={sideTouched(v) && v.current === v.target} class:miss={sideTouched(v) && v.current !== v.target}>{fmt(s, v.current)}</td>
                      <td class="target">{sideTouched(v) ? fmt(s, v.target) : '·'}</td>
                      <td class="muted">{fmt(s, v.default)}</td>
                    {/each}
                  </tr>
                {/each}
              </tbody>
            </table>
          </div>
        {/if}
      </section>
    {/each}
  </div>
{:else}
  <p class="muted">{t('common.loading')}</p>
{/if}


<style>
  .meta { margin-bottom: 14px; }
  .desc { margin-bottom: 8px; max-width: 820px; }
  .toggle { margin: 2px 0 6px -8px; }
  .chev { display: inline-flex; transition: transform 0.15s; }
  .chev.open { transform: rotate(90deg); }
  .sub th { font-size: 11.5px; padding-top: 0; }
  .name { display: block; font-weight: 500; }
  td.hit { color: var(--ok); font-weight: 600; }
  td.miss { color: var(--warn); font-weight: 600; }
  td.target { font-weight: 500; }
  tr.dim { opacity: 0.55; }
</style>

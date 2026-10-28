<script lang="ts">
  import { onMount } from 'svelte';
  import { slide } from 'svelte/transition';
  import { api, confirm, copyText, run, toast } from '../lib/app.svelte';
  import { host, t } from '../lib/i18n.svelte';
  import { formatPowerValue } from '../lib/format';
  import type { PowerApplyResult, PowerProfileState, PowerSettingState, PowerState, PowerValue, WolLocalState } from '../lib/types';
  import Icon from '../lib/components/Icon.svelte';

  let power = $state<PowerState | null>(null);
  let wol = $state<WolLocalState | null>(null);
  let busy = $state<string | null>(null);
  let open = $state<Record<string, boolean>>({});

  async function load() {
    [power, wol] = await Promise.all([
      run(() => api<PowerState>('power.state')).then((r) => r ?? power),
      run(() => api<WolLocalState>('wol.local')).then((r) => r ?? wol),
    ]);
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

  async function wolChange(method: 'wol.apply' | 'wol.reset') {
    const ok = await confirm(t('wol.title'), method === 'wol.apply' ? t('wol.confirmApply') : t('wol.confirmReset'), method === 'wol.apply' ? t('wol.apply') : t('wol.reset'));
    if (!ok) return;
    busy = 'wol';
    const details = 'Set-NetAdapterAdvancedProperty *WakeOnMagicPacket=1, S5WakeOnLan/WakeOnMagicPacketFromS5=1; powercfg /deviceenablewake; HiberbootEnabled=0';
    const r = await run(() => api<{ errors: string[]; state: WolLocalState }>(method), undefined, details);
    busy = null;
    if (r) {
      wol = r.state;
      toast(r.errors.length ? 'error' : 'ok', r.errors.length ? r.errors.join(' · ') : t('common.saved'));
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
    <div class="note warn"><Icon name="alert" /><p>{t('power.notActive', { scheme: power.schemeName })}</p></div>
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

<section class="card wol">
  <div class="card-head">
    <h2>{t('wol.title')}</h2>
    {#if wol}<span class="badge {wol.ready ? 'ok' : 'warn'}">{wol.ready ? t('wol.ready') : t('wol.notReady')}</span>{/if}
    <span class="spacer"></span>
    <button class="btn primary small" disabled={busy !== null} onclick={() => wolChange('wol.apply')}>{t('wol.apply')}</button>
    <button class="btn small" disabled={busy !== null} onclick={() => wolChange('wol.reset')}><Icon name="undo" size={14} />{t('wol.reset')}</button>
  </div>
  <p class="muted desc">{t('wol.subtitle')}</p>
  <p class="small muted">{t('common.needsAdmin')}</p>

  {#if wol}
    {#if wol.adapters.length === 0}
      <div class="note warn"><Icon name="alert" /><p>{t('wol.noAdapters')}</p></div>
    {/if}
    <div class="adapters">
      {#each wol.adapters as a (a.name)}
        <div class="adapter">
          <div class="row between">
            <div>
              <h3>{a.name}</h3>
              <p class="small muted">{a.description} · {a.status}</p>
            </div>
            <span class="badge {a.ready ? 'ok' : 'warn'}">{a.ready ? t('wol.ready') : t('wol.notReady')}</span>
          </div>
          <div class="row">
            <span class="small">{t('wol.mac')}:</span>
            <code>{a.mac}</code>
            <button class="btn ghost small" title={t('wol.macHint')} onclick={async () => { await copyText(a.mac); toast('ok', t('common.copied')); }}><Icon name="copy" size={13} /></button>
            <span class="badge {a.armed ? 'ok' : 'warn'}">{a.armed ? t('wol.armed') : t('wol.notArmed')}</span>
          </div>
          <ul class="props">
            {#each a.props as prop (prop.keyword)}
              <li>
                <span class="state" class:ok={prop.ok}><Icon name={prop.ok ? 'check' : 'x'} size={13} /></span>
                <span>{prop.name}</span>
                <span class="small muted">{prop.display ?? prop.value}</span>
                <span class="badge {prop.required ? 'accent' : ''}">{prop.required ? t('wol.required') : t('wol.optional')}</span>
              </li>
            {/each}
          </ul>
        </div>
      {/each}
    </div>
    <p class="row small">
      <span class="badge {wol.fastStartupOff ? 'ok' : 'warn'}">{t('wol.fastStartup')}</span>
      {wol.fastStartupOff ? t('wol.fastStartupOff') : t('wol.fastStartupOn')}
    </p>
    <div class="note"><Icon name="info" /><p class="small">{t('wol.biosHint')}</p></div>
  {/if}
</section>

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
  .wol { margin-top: 16px; }
  .adapters { display: grid; grid-template-columns: repeat(auto-fill, minmax(340px, 1fr)); gap: 12px; margin: 12px 0; }
  .adapter { border: 1px solid var(--border); border-radius: var(--radius-small); padding: 12px 14px; display: flex; flex-direction: column; gap: 8px; }
  .props { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 4px; }
  .props li { display: grid; grid-template-columns: 20px 1fr auto auto; gap: 8px; align-items: center; }
  .state { width: 18px; height: 18px; border-radius: 50%; display: grid; place-items: center; background: var(--warn-soft); color: var(--warn); }
  .state.ok { background: var(--ok-soft); color: var(--ok); }
</style>

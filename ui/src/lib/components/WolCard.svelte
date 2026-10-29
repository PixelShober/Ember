<script lang="ts">
  // Wake-on-LAN for this machine: the target state (derived from a machine that wakes reliably from
  // "shut down") and the way back to the driver defaults. Lives in the KI tab.
  import { onMount } from 'svelte';
  import { api, confirm, copyText, run, toast } from '../app.svelte';
  import { t } from '../i18n.svelte';
  import type { WolLocalState } from '../types';
  import Icon from './Icon.svelte';
  import Calcifer from './Calcifer.svelte';

  let wol = $state<WolLocalState | null>(null);
  let busy = $state(false);

  onMount(async () => {
    wol = (await run(() => api<WolLocalState>('wol.local'))) ?? null;
  });

  async function wolChange(method: 'wol.apply' | 'wol.reset') {
    const ok = await confirm(t('wol.title'), method === 'wol.apply' ? t('wol.confirmApply') : t('wol.confirmReset'), method === 'wol.apply' ? t('wol.apply') : t('wol.reset'));
    if (!ok) return;
    busy = true;
    const details = 'Set-NetAdapterAdvancedProperty *WakeOnMagicPacket=1, S5WakeOnLan/WakeOnMagicPacketFromS5=1; powercfg /deviceenablewake; HiberbootEnabled=0';
    const r = await run(() => api<{ errors: string[]; state: WolLocalState }>(method), undefined, details);
    busy = false;
    if (r) {
      wol = r.state;
      toast(r.errors.length ? 'error' : 'ok', r.errors.length ? r.errors.join(' · ') : t('common.saved'));
    }
  }
</script>

<section class="card">
  <div class="card-head">
    <Icon name="wake" />
    <h2>{t('wol.title')}</h2>
    {#if wol}<span class="badge {wol.ready ? 'ok' : 'warn'}">{wol.ready ? t('wol.ready') : t('wol.notReady')}</span>{/if}
    <span class="spacer"></span>
    <button class="btn primary small" disabled={busy} onclick={() => wolChange('wol.apply')}>{t('wol.apply')}</button>
    <button class="btn small" disabled={busy} onclick={() => wolChange('wol.reset')}><Icon name="undo" size={14} />{t('wol.reset')}</button>
  </div>
  <p class="muted desc">{t('wol.subtitle')}</p>
  <p class="small muted">{t('common.needsAdmin')}</p>

  {#if wol}
    {#if wol.adapters.length === 0}
      <div class="note warn"><Calcifer /><p>{t('wol.noAdapters')}</p></div>
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
  .desc { margin-bottom: 8px; max-width: 820px; }
  .adapters { display: grid; grid-template-columns: repeat(auto-fill, minmax(340px, 1fr)); gap: 12px; margin: 12px 0; }
  .adapter { border: 1px solid var(--border); border-radius: var(--radius-small); padding: 12px 14px; display: flex; flex-direction: column; gap: 8px; }
  .props { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 4px; }
  .props li { display: grid; grid-template-columns: 20px 1fr auto auto; gap: 8px; align-items: center; }
  .state { width: 18px; height: 18px; border-radius: 50%; display: grid; place-items: center; background: var(--warn-soft); color: var(--warn); }
  .state.ok { background: var(--ok-soft); color: var(--ok); }
</style>

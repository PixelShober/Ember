<script lang="ts">
  import { fade, scale } from 'svelte/transition';
  import { api, app, copyText, run, toast } from '../app.svelte';
  import { host, t } from '../i18n.svelte';
  import type { MailRequest } from '../app.svelte';
  import type { MailResult } from '../types';
  import Icon from './Icon.svelte';
  import Calcifer from './Calcifer.svelte';

  const templates: MailRequest['template'][] = ['softwareRequest', 'adminAction', 'problem'];

  let template = $state<MailRequest['template']>('problem');
  let details = $state('');
  let attach = $state(true);
  let app_ = $state('');
  let preview = $state<{ to: string | null; subject: string; body: string } | null>(null);
  let busy = $state(false);

  // Take over the request when the dialog opens.
  $effect(() => {
    const m = app.mail;
    if (!m) return;
    template = m.template;
    details = m.vars.details ?? '';
    app_ = m.vars.app ?? '';
    attach = m.attachReport;
  });

  const vars = $derived({ details, app: app_ });

  $effect(() => {
    if (!app.mail) return;
    const p = { template, vars };
    void api<typeof preview>('mail.preview', p).then((r) => (preview = r)).catch(() => (preview = null));
  });

  function close() {
    app.mail = null;
  }

  async function compose() {
    busy = true;
    const result = await run(() => api<MailResult>('mail.compose', { template, vars, attachReport: attach }));
    busy = false;
    if (!result) return;
    if (result.method === 'mailto' && result.reportText) await copyText(result.reportText);
    toast('ok', result.method === 'outlook' ? t('mail.resultOutlook') : t('mail.resultMailto'));
    close();
  }
</script>

{#if app.mail}
  <div class="layer" transition:fade={{ duration: 120 }}>
    <div class="backdrop" onclick={close} role="presentation"></div>
    <div class="dialog" role="dialog" aria-modal="true" aria-labelledby="mail-title" transition:scale={{ start: 0.96, duration: 150 }}>
      <div class="row">
        <Icon name="mail" />
        <h2 id="mail-title">{t('mail.title')}</h2>
      </div>
      <p class="muted">{t('mail.subtitle')}</p>

      {#if !app.info?.serviceCenter}
        <div class="note warn"><Calcifer /><p>{t('mail.noServiceCenter')}</p></div>
      {/if}

      <label class="field">
        <span>{t('mail.template')}</span>
        <select bind:value={template}>
          {#each templates as tpl (tpl)}<option value={tpl}>{t(`mail.templateNames.${tpl}`)}</option>{/each}
        </select>
      </label>
      {#if template === 'softwareRequest'}
        <label class="field"><span>{t('nav.software')}</span><input bind:value={app_} /></label>
      {/if}
      <label class="field">
        <span>{t('mail.details')}</span>
        <textarea bind:value={details} rows="4"></textarea>
      </label>
      <label class="check"><input type="checkbox" bind:checked={attach} />{t('mail.attachReport')}</label>

      {#if preview}
        <div class="preview">
          <p class="small muted">{t('mail.to')}: {preview.to ?? '–'}</p>
          <p class="subject">{host(preview.subject)}</p>
          <pre>{host(preview.body)}</pre>
        </div>
      {/if}

      <div class="row actions">
        <button class="btn" onclick={close}>{t('common.cancel')}</button>
        <button class="btn primary" disabled={busy || !app.info?.serviceCenter} onclick={compose}><Icon name="mail" size={16} />{t('mail.compose')}</button>
      </div>
    </div>
  </div>
{/if}

<style>
  .layer { position: fixed; inset: 0; z-index: 40; display: flex; align-items: flex-start; justify-content: center; padding-top: 8vh; }
  .backdrop { position: absolute; inset: 0; background: rgb(0 0 0 / 0.35); }
  .dialog {
    position: relative; width: min(620px, calc(100vw - 40px)); max-height: 84vh; overflow: auto;
    background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius);
    box-shadow: 0 24px 60px rgb(0 0 0 / 0.3); padding: 22px 24px;
    display: flex; flex-direction: column; gap: 12px;
  }
  .preview { background: var(--surface-2); border-radius: var(--radius-small); padding: 10px 14px; }
  .subject { font-weight: 600; margin: 2px 0 6px; }
  pre { margin: 0; white-space: pre-wrap; font-family: var(--font); font-size: 13px; color: var(--text-muted); }
  .actions { justify-content: flex-end; }
</style>

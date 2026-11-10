<script lang="ts">
  // The team settings that every team file has: service center, supervisor, updates, Playwright
  // and the mail texts. Plain fields, grouped like the sections of the file.
  import { t } from '../i18n.svelte';
  import type { TeamDoc } from '../types';
  import Icon from './Icon.svelte';

  let { doc = $bindable(), editable }: { doc: TeamDoc; editable: boolean } = $props();

  const templates = ['softwareRequest', 'problem', 'adminAction'] as const;
  let open = $state<(typeof templates)[number] | null>(null);
  const placeholders: Record<(typeof templates)[number], string[]> = {
    softwareRequest: ['{app}', '{user}', '{computer}', '{sender}'],
    problem: ['{problemIntro}', '{details}', '{user}', '{computer}', '{sender}'],
    adminAction: ['{supervisor}', '{details}', '{computer}', '{sender}'],
  };
</script>

<div class="grid">
  <section class="card stack tight">
    <div class="card-head"><Icon name="mail" /><h2>{t('access.team.serviceCenter')}</h2></div>
    <label class="field"><span>{t('access.team.email')}</span><input type="email" bind:value={doc.serviceCenter.email} disabled={!editable} placeholder="servicecenter@firma.de" /></label>
    <label class="field"><span>{t('access.team.name')}</span><input bind:value={doc.serviceCenter.name} disabled={!editable} placeholder="IT-Service-Center" /></label>
    <label class="field"><span>{t('access.team.subjectPrefix')}</span><input bind:value={doc.serviceCenter.subjectPrefix} disabled={!editable} placeholder="[Ember]" /></label>
    <label class="field"><span>{t('access.team.problemIntro')}</span><textarea rows="2" bind:value={doc.serviceCenter.problemIntro} disabled={!editable} placeholder={t('mail.problemIntro')}></textarea></label>
  </section>

  <section class="card stack tight">
    <div class="card-head"><Icon name="user" /><h2>{t('access.team.supervisor')}</h2></div>
    <label class="field"><span>{t('access.team.name')}</span><input bind:value={doc.supervisor.name} disabled={!editable} placeholder="Petra Lindner" /></label>
    <label class="field"><span>{t('access.team.firstName')}</span><input bind:value={doc.supervisor.firstName} disabled={!editable} placeholder={doc.supervisor.name.split(/[ ,]+/)[0] || 'Petra'} /></label>
    <label class="field"><span>{t('access.team.email')}</span><input type="email" bind:value={doc.supervisor.email} disabled={!editable} placeholder="petra.lindner@firma.de" /></label>
  </section>

  <section class="card stack tight">
    <div class="card-head"><Icon name="browser" /><h2>Playwright</h2></div>
    <label class="field"><span>{t('access.team.template')}</span><input class="mono" bind:value={doc.playwright.template} disabled={!editable} placeholder="\\fs01\entwicklung\vorlagen\playwright" spellcheck="false" /></label>
    <label class="field"><span>{t('access.team.downloadHost')}</span><input class="mono" bind:value={doc.playwright.downloadHost} disabled={!editable} placeholder="https://artifactory.firma.de/playwright" spellcheck="false" /></label>
  </section>

  <section class="card stack tight">
    <div class="card-head"><Icon name="sparkle" /><h2>{t('access.team.update')}</h2></div>
    <label class="field"><span>{t('access.team.mirror')}</span><input class="mono" bind:value={doc.update.mirror} disabled={!editable} placeholder="\\fs01\entwicklung\ember\releases" spellcheck="false" /></label>
  </section>

  <section class="card stack tight wide">
    <div class="card-head"><Icon name="log" /><h2>{t('access.team.mails')}</h2></div>
    <ul class="mails">
      {#each templates as id (id)}
        <li class:open={open === id}>
          <button class="mail-head" aria-expanded={open === id} onclick={() => (open = open === id ? null : id)}>
            <Icon name="chevron" size={14} />
            <strong>{t(`mail.templateNames.${id}`)}</strong>
            <span class="small muted subject">{doc.mailTemplates[id].subject || t(`mail.templates.${id}.subject`)}</span>
            {#if doc.mailTemplates[id].subject || doc.mailTemplates[id].body}<span class="badge info">{t('access.team.custom')}</span>{/if}
          </button>
          {#if open === id}
            <div class="mail-body stack tight">
              <label class="field"><span>{t('access.team.subject')}</span><input bind:value={doc.mailTemplates[id].subject} disabled={!editable} placeholder={t(`mail.templates.${id}.subject`)} /></label>
              <label class="field"><span>{t('access.team.body')}</span><textarea rows="8" bind:value={doc.mailTemplates[id].body} disabled={!editable} placeholder={t(`mail.templates.${id}.body`)}></textarea></label>
              <p class="small muted">{t('access.team.placeholders')} {#each placeholders[id] as p (p)}<code>{p}</code> {/each}</p>
            </div>
          {/if}
        </li>
      {/each}
    </ul>
  </section>
</div>

<style>
  .grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; align-items: start; }
  .wide { grid-column: 1 / -1; }
  .field { display: flex; flex-direction: column; gap: 4px; font-size: 13px; color: var(--text-muted); }
  .field input, .field textarea { color: var(--text); }
  textarea { resize: vertical; font: inherit; }
  .mono { font-family: var(--mono); font-size: 12.5px; }
  .mails { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 6px; }
  .mails li { border: 1px solid var(--border); border-radius: var(--radius-small); }
  .mail-head {
    width: 100%; display: flex; align-items: center; gap: 10px; padding: 10px 12px; cursor: pointer; text-align: left;
    font: inherit; color: inherit; background: none; border: 0; border-radius: var(--radius-small);
  }
  .mail-head:hover { background: var(--surface-2); }
  .mail-head > :global(svg) { color: var(--text-muted); transition: transform 150ms ease-out; flex: none; }
  .open .mail-head > :global(svg) { transform: rotate(90deg); }
  .subject { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .mail-body { padding: 4px 12px 12px; }
  code { font-size: 12px; }
  @media (max-width: 900px) { .grid { grid-template-columns: 1fr; } }
</style>

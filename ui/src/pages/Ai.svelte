<script lang="ts">
  import { onMount } from 'svelte';
  import { api, app, confirm, run, toast } from '../lib/app.svelte';
  import { t } from '../lib/i18n.svelte';
  import { formatBytes } from '../lib/format';
  import type { AgentToolState, AiStatus, SettingsView, UserSettings, WolDevice } from '../lib/types';
  import Icon from '../lib/components/Icon.svelte';
  import Calcifer from '../lib/components/Calcifer.svelte';
  import JobView from '../lib/components/JobView.svelte';

  let status = $state<AiStatus | null>(null);
  let agents = $state<AgentToolState[] | null>(null);
  let settings = $state<UserSettings | null>(null);
  let serverModel = $state('');
  let newModel = $state({ url: '', name: '' });
  let newAgent = $state('');

  async function load() {
    const [s, a, v] = await Promise.all([
      run(() => api<AiStatus>('ai.status')),
      run(() => api<AgentToolState[]>('ai.agents.list')),
      run(() => api<SettingsView>('settings.get')),
    ]);
    status = s ?? status;
    agents = a ?? agents;
    settings = v?.user ?? settings;
    if (!serverModel) serverModel = status?.models.find((m) => m.downloaded)?.id ?? '';
  }
  onMount(load);

  // Reload the lists when a download or installation finishes.
  $effect(() => {
    const finished = Object.values(app.jobs).filter((j) => ['llama', 'model', 'agent'].includes(j.kind) && j.state === 'done').length;
    if (finished) void load();
  });

  async function saveSettings(okText?: string) {
    if (!settings) return;
    const r = await run(() => api<SettingsView>('settings.save', { settings: $state.snapshot(settings) }), okText);
    if (r) settings = r.user;
  }

  function addDevice() {
    settings?.wolDevices.push({ id: crypto.randomUUID().slice(0, 8), name: '', mac: '', broadcast: '', port: 9, host: '' });
  }

  function removeDevice(d: WolDevice) {
    if (!settings) return;
    settings.wolDevices = settings.wolDevices.filter((x) => x.id !== d.id);
  }

  async function addModel() {
    if (!settings || !newModel.url.trim()) return;
    const file = newModel.url.split('?')[0].split('/').pop() ?? newModel.url;
    settings.models.push({ id: file, name: newModel.name.trim() || file, url: newModel.url.trim() });
    newModel = { url: '', name: '' };
    await saveSettings(t('common.saved'));
    await load();
  }

  async function deleteModel(id: string, name: string) {
    if (!(await confirm(name, t('ai.models.confirmDelete', { name }), t('common.delete'), true))) return;
    await run(() => api('ai.models.delete', { id }));
    await load();
  }

  async function addAgent() {
    if (!settings || !newAgent.trim()) return;
    settings.agentTools.push(newAgent.trim());
    newAgent = '';
    await saveSettings(t('common.saved'));
    await load();
  }

  async function startServer() {
    if (await run(() => api('ai.server.start', { model: serverModel }))) await load();
    else await load();
  }
</script>

<header class="page-head">
  <h1>{t('ai.title')}</h1>
  <p>{t('ai.subtitle')}</p>
</header>

<div class="stack">
  <!-- Wake other machines -->
  <section class="card">
    <div class="card-head">
      <Icon name="wake" />
      <h2>{t('ai.devices.title')}</h2>
      <span class="spacer"></span>
      <button class="btn small" onclick={addDevice}><Icon name="plus" size={14} />{t('ai.devices.add')}</button>
      <button class="btn small primary" onclick={() => saveSettings(t('ai.devices.saved'))}>{t('common.save')}</button>
    </div>
    {#if settings && settings.wolDevices.length === 0}<div class="note"><Calcifer /><p>{t('ai.devices.empty')}</p></div>{/if}
    {#each settings?.wolDevices ?? [] as d (d.id)}
      <div class="device">
        <label class="field"><span>{t('ai.devices.name')}</span><input bind:value={d.name} /></label>
        <label class="field"><span>{t('ai.devices.mac')}</span><input bind:value={d.mac} class="mono" placeholder="02-00-5E-10-00-01" spellcheck="false" /></label>
        <label class="field"><span>{t('ai.devices.host')}</span><input bind:value={d.host} spellcheck="false" /></label>
        <label class="field narrow"><span>{t('ai.devices.port')}</span><input type="number" bind:value={d.port} min="1" max="65535" /></label>
        <div class="row end">
          <button class="btn primary small" onclick={() => run(() => api('ai.wake', { id: d.id }))}><Icon name="wake" size={14} />{t('ai.devices.wake')}</button>
          <button class="btn ghost small" aria-label={t('common.remove')} onclick={() => removeDevice(d)}><Icon name="trash" size={14} /></button>
        </div>
        <div class="full"><JobView id={'wake:' + d.id} compact /></div>
      </div>
    {/each}
  </section>

  <!-- GPU -->
  <section class="card">
    <div class="card-head"><Icon name="gpu" /><h2>{t('ai.gpu.title')}</h2></div>
    {#if status?.gpus.length}
      {#each status.gpus as g (g.name)}
        <div class="gpu">
          <strong>{g.name}</strong>
          <span class="small muted">{t('ai.gpu.driver', { driver: g.driver ?? '?', cuda: g.cuda ?? '?' })}</span>
          <div class="row">
            {#if g.memoryTotalMb}<span class="badge">{t('ai.gpu.memory', { used: g.memoryUsedMb ?? 0, total: g.memoryTotalMb })}</span>{/if}
            {#if g.powerLimit}<span class="badge">{t('ai.gpu.power', { draw: g.powerDraw ?? 0, limit: g.powerLimit })}</span>{/if}
            {#if g.temperature != null}<span class="badge">{t('ai.gpu.temperature', { value: g.temperature })}</span>{/if}
            {#if g.utilization != null}<span class="badge">{t('ai.gpu.utilization', { value: g.utilization })}</span>{/if}
          </div>
        </div>
      {/each}
      <p class="small muted">{t('ai.gpu.readOnly')}</p>
    {:else if status}
      <p class="muted">{t('ai.gpu.none')}</p>
    {/if}
  </section>

  <!-- llama.cpp -->
  <section class="card">
    <div class="card-head">
      <Icon name="terminal" />
      <h2>{t('ai.llama.title')}</h2>
      {#if status}<span class="badge info">{t('ai.llama.recommended', { variant: t(`ai.llama.variants.${status.llama.recommendedVariant}`) })}</span>{/if}
      <span class="spacer"></span>
      {#if settings}
        <select bind:value={settings.llamaVariant} onchange={() => saveSettings()} class="variant" aria-label={t('ai.llama.variant', { variant: '' })}>
          {#each ['auto', 'cuda', 'vulkan', 'cpu'] as v (v)}<option value={v}>{t(`ai.llama.variants.${v}`)}</option>{/each}
        </select>
      {/if}
      <button class="btn primary small" disabled={app.jobs['llama']?.state === 'running'} onclick={() => run(() => api('ai.llama.install'))}><Icon name="download" size={14} />{t('ai.llama.install')}</button>
    </div>
    {#if status?.llama.installed.length}
      <table class="table">
        <tbody>
          {#each status.llama.installed as l (l.path)}
            <tr>
              <td><strong>{l.tag}</strong> <span class="muted">{t(`ai.llama.variants.${l.variant}`)}</span></td>
              <td><code class="small">{l.path}</code></td>
              <td class="right">
                {#if l.current}<span class="badge ok">{t('ai.llama.current')}</span>
                {:else}<button class="btn small" onclick={async () => { await run(() => api('ai.llama.select', { name: l.tag + '-' + l.variant })); await load(); }}>{t('ai.llama.use')}</button>{/if}
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    {:else if status}
      <p class="muted">{t('ai.llama.none')}</p>
    {/if}
    <JobView id="llama" />
  </section>

  <!-- Models -->
  <section class="card">
    <div class="card-head"><Icon name="sparkle" /><h2>{t('ai.models.title')}</h2>
      <span class="spacer"></span>
      <button class="btn ghost small" onclick={() => run(() => api('app.openFolder', { key: 'models' }))}><Icon name="folder" size={14} />{t('common.openFolder')}</button>
    </div>
    {#if status}<p class="small muted">{t('ai.models.folder', { path: status.modelsDir })}</p>{/if}
    {#if status && status.models.length === 0}<div class="note"><Calcifer /><p>{t('ai.models.empty')}</p></div>{/if}
    <ul class="models">
      {#each status?.models ?? [] as m (m.id)}
        <li>
          <div class="grow">
            <strong>{m.name}</strong>
            {#if m.note}<span class="small muted"> · {m.note}</span>{/if}
            <p class="small muted break">{m.downloaded ? t('ai.models.downloaded', { size: formatBytes(m.size) }) : t('ai.models.notDownloaded')}</p>
          </div>
          {#if m.downloaded}
            <button class="btn ghost small" onclick={() => deleteModel(m.id, m.name)}><Icon name="trash" size={14} /></button>
          {:else}
            <button class="btn small" disabled={app.jobs['model:' + m.id]?.state === 'running'} onclick={() => run(() => api('ai.models.download', { id: m.id }))}><Icon name="download" size={14} />{t('ai.models.download')}</button>
          {/if}
          <div class="full"><JobView id={'model:' + m.id} compact /></div>
        </li>
      {/each}
    </ul>
    <div class="add">
      <input bind:value={newModel.url} placeholder={t('ai.models.addUrl')} spellcheck="false" />
      <input bind:value={newModel.name} placeholder={t('ai.models.addName')} />
      <button class="btn small" disabled={!newModel.url.trim()} onclick={addModel}><Icon name="plus" size={14} />{t('ai.models.add')}</button>
    </div>
  </section>

  <!-- llama-server -->
  <section class="card">
    <div class="card-head">
      <Icon name="server" />
      <h2>{t('ai.server.title')}</h2>
      {#if status?.llama.serverRunning}<span class="badge ok"><span class="dot"></span>{t('ai.server.running', { port: status.llama.port, model: status.llama.serverModel ?? '' })}</span>{/if}
    </div>
    {#if settings && status}
      <div class="server">
        <label class="field"><span>{t('ai.server.model')}</span>
          <select bind:value={serverModel}>
            {#each status.models.filter((m) => m.downloaded) as m (m.id)}<option value={m.id}>{m.name}</option>{/each}
          </select>
        </label>
        <label class="field narrow"><span>{t('ai.server.port')}</span><input type="number" bind:value={settings.llamaPort} onchange={() => saveSettings()} /></label>
        <label class="field"><span>{t('ai.server.args')}</span><input bind:value={settings.llamaArgs} onchange={() => saveSettings()} class="mono" spellcheck="false" /></label>
      </div>
      <div class="row">
        {#if status.llama.serverRunning}
          <button class="btn" onclick={async () => { await run(() => api('ai.server.stop')); await load(); }}><Icon name="stop" size={14} />{t('ai.server.stop')}</button>
          <button class="btn ghost" onclick={() => run(() => api('app.openExternal', { url: `http://127.0.0.1:${status!.llama.port}/` }))}><Icon name="external" size={14} />{t('ai.server.openUi')}</button>
        {:else}
          <button class="btn primary" disabled={!serverModel} onclick={startServer}><Icon name="play" size={14} />{t('ai.server.start')}</button>
        {/if}
      </div>
    {/if}
  </section>

  <!-- Agent tools -->
  <section class="card">
    <div class="card-head"><Icon name="terminal" /><div><h2>{t('ai.agents.title')}</h2><p class="small muted">{t('ai.agents.subtitle')}</p></div></div>
    <ul class="models">
      {#each agents ?? [] as a (a.package)}
        <li>
          <div class="grow">
            <strong>{a.name}</strong> <code class="small muted">{a.package}</code>
            {#if a.description}<p class="small muted">{a.description}</p>{/if}
            <p class="small">
              {a.installed ? t('ai.agents.installed', { version: a.installed }) : t('ai.agents.notInstalled')}
              {#if a.latest} · <span class="muted">{t('ai.agents.latest', { version: a.latest })}</span>{/if}
            </p>
          </div>
          {#if !a.installed || (a.latest && a.installed !== a.latest)}
            <button class="btn small {a.installed ? '' : 'primary'}" disabled={app.jobs['agent:' + a.package]?.state === 'running'} onclick={() => run(() => api('ai.agents.install', { package: a.package }))}>
              <Icon name="download" size={14} />{a.installed ? t('ai.agents.update') : t('ai.agents.install')}
            </button>
          {:else}
            <span class="badge ok"><Icon name="check" size={13} /></span>
          {/if}
          <div class="full"><JobView id={'agent:' + a.package} compact /></div>
        </li>
      {/each}
    </ul>
    <div class="add">
      <input bind:value={newAgent} placeholder="@scope/package" spellcheck="false" class="mono" />
      <button class="btn small" disabled={!newAgent.trim()} onclick={addAgent}><Icon name="plus" size={14} />{t('ai.agents.add')}</button>
    </div>
  </section>
</div>

<style>
  .device { display: grid; grid-template-columns: 1.2fr 1fr 1.2fr 90px auto; gap: 10px; align-items: end; padding: 10px 0; border-top: 1px solid var(--border); }
  .device:first-of-type { border-top: none; }
  .full { grid-column: 1 / -1; }
  .full:empty { display: none; }
  .end { justify-content: flex-end; }
  .narrow input { width: 90px; }
  .gpu { display: flex; flex-direction: column; gap: 6px; margin-bottom: 8px; }
  .right { text-align: right; }
  .variant { width: auto; min-width: 160px; }
  .models { list-style: none; margin: 8px 0; padding: 0; display: flex; flex-direction: column; }
  .models li { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; padding: 10px 0; border-top: 1px solid var(--border); }
  .models li:first-child { border-top: none; }
  .grow { flex: 1; min-width: 240px; }
  .add { display: grid; grid-template-columns: 2fr 1fr auto; gap: 8px; margin-top: 8px; }
  .server { display: grid; grid-template-columns: 1.5fr 100px 1.5fr; gap: 10px; margin-bottom: 10px; }
  @media (max-width: 1100px) {
    .device { grid-template-columns: 1fr 1fr; }
    .server, .add { grid-template-columns: 1fr; }
  }
</style>

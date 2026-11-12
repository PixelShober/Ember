<script lang="ts">
  import { onMount } from 'svelte';
  import { api, forget, app, confirm, copyText, run, toast } from '../lib/app.svelte';
  import { t } from '../lib/i18n.svelte';
  import { formatBytes } from '../lib/format';
  import type { AiStatus, LlamaProfile, ModelState, PiPackageState, SettingsView, UserSettings } from '../lib/types';
  import Icon from '../lib/components/Icon.svelte';
  import Calcifer from '../lib/components/Calcifer.svelte';
  import JobView from '../lib/components/JobView.svelte';
  import WolCard from '../lib/components/WolCard.svelte';

  let status = $state<AiStatus | null>(null);
  let settings = $state<UserSettings | null>(null);
  let serverModel = $state('');
  /** Why "Server starten" is off, as a locale key below ai.server.blocked, or null. */
  let startBlocked = $derived.by(() => {
    if (!status) return null;
    const builds = status.llama.installed;
    if (!builds.length) return 'noLlama';
    const current = builds.find((b) => b.current) ?? builds.find((b) => b.complete) ?? builds[0];
    if (!current.complete) return 'incomplete';
    if (!serverModel) return 'noModel';
    return null;
  });
  let profile = $state<LlamaProfile['id']>('lang');
  let newModel = $state({ url: '', name: '' });

  async function load() {
    const [s, v] = await Promise.all([
      run(() => api<AiStatus>('ai.status')),
      run(() => api<SettingsView>('settings.get')),
    ]);
    status = s ?? status;
    settings = v?.user ?? settings;
    if (status) profile = (status.llama.serverProfile as LlamaProfile['id'] | null) ?? status.profile;
    if (!serverModel || !ready.some((m) => m.id === serverModel)) serverModel = ready[0]?.id ?? '';
  }
  onMount(load);

  // Newest llama.cpp build for this machine: green when it is the active one, orange when newer.
  let latest = $state<string | null>(null);
  onMount(() => { void api<{ tag: string | null }>('ai.llama.latest').then((r) => (latest = r.tag)).catch(() => {}); });
  const active = $derived(status?.llama.installed.find((l) => l.current && l.complete && l.tag !== 'custom') ?? null);
  const upToDate = $derived(!!latest && active?.tag === latest);
  const updateAvailable = $derived(!!latest && !!active && active.tag !== latest);

  /** Models the server can start: downloaded, or a local file that exists. */
  const ready = $derived(status?.models.filter((m) => m.downloaded) ?? []);

  // Reload the lists when a download or installation finishes.
  $effect(() => {
    const finished = Object.values(app.jobs).filter((j) => ['llama', 'model', 'pi'].includes(j.kind) && j.state === 'done').length;
    if (finished) void load();
  });

  async function saveSettings(okText?: string) {
    if (!settings) return;
    const r = await run(() => api<SettingsView>('settings.save', { settings: $state.snapshot(settings) }), okText);
    if (r) settings = r.user;
  }

  async function addModel() {
    if (!settings || !newModel.url.trim()) return;
    const file = newModel.url.split('?')[0].split('/').pop() ?? newModel.url;
    settings.models.push({ id: file, name: newModel.name.trim() || file, url: newModel.url.trim() });
    newModel = { url: '', name: '' };
    await saveSettings(t('common.saved'));
    await load();
  }

  async function addLocal() {
    const id = await run(() => api<string | null>('ai.models.addLocal'));
    if (!id) return;
    await load();
    serverModel = id;
    toast('ok', t('ai.models.addedLocal', { name: status?.models.find((m) => m.id === id)?.name ?? id }));
  }

  async function deleteModel(m: ModelState) {
    const text = m.local ? t('ai.models.confirmRemoveLocal', { name: m.name }) : t('ai.models.confirmDelete', { name: m.name });
    if (!(await confirm(m.name, text, m.local ? t('ai.models.removeLocal') : t('common.delete'), !m.local))) return;
    await run(() => api('ai.models.delete', { id: m.id }));
    await load();
  }

  async function startServer() {
    await run(() => api('ai.server.start', { model: serverModel, profile }));
    await load();
  }

  // While the model loads, ask again until the server answers.
  $effect(() => {
    if (!status?.llama.serverRunning || status.llama.loaded) return;
    const timer = setInterval(load, 3000);
    return () => clearInterval(timer);
  });
  const onCpu = $derived(!!status?.llama.loaded && !status.llama.device && status.llama.current?.endsWith('-cuda'));

  const gpu = $derived(status?.gpus[0]);
  const vramFree = $derived(gpu?.memoryTotalMb != null && gpu.memoryUsedMb != null ? gpu.memoryTotalMb - gpu.memoryUsedMb : null);
  const partial = $derived.by(() => {
    const m = status?.llama.offloaded?.match(/^(\d+)\/(\d+)$/);
    return !!m && m[1] !== m[2];
  });

  // Pi and its extensions: versions come from npm (through px), so they load separately.
  let piPackages = $state<{ pi: PiPackageState; packages: PiPackageState[] } | null>(null);
  async function loadPackages() {
    piPackages = (await api<{ pi: PiPackageState; packages: PiPackageState[] }>('ai.pi.packages').catch(() => null)) ?? piPackages;
  }
  onMount(loadPackages);
  $effect(() => {
    const done = Object.values(app.jobs).filter((j) => j.kind === 'pi' && j.state === 'done').length;
    if (done) void loadPackages();
  });
  const piRunning = $derived(Object.values(app.jobs).some((j) => j.kind === 'pi' && j.state === 'running'));

  async function useFolder() {
    const dir = await run(() => api<string | null>('ai.llama.useFolder'));
    if (dir) { toast('ok', t('ai.llama.usedFolder', { path: dir })); await load(); }
  }

  async function configurePi() {
    const file = await run(() => api<string>('ai.pi.configure'));
    if (file) toast('ok', t('ai.pi.configuredToast', { path: file }));
    await load();
  }
</script>

<header class="page-head">
  <h1>{t('ai.title')}</h1>
  <p>{t('ai.subtitle')}</p>
</header>

<div class="stack">
  <!-- Wake-on-LAN for this machine -->
  <WolCard />

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
      <button class="btn small" onclick={useFolder}><Icon name="folder" size={14} />{t('ai.llama.useFolder')}</button>
      {#if upToDate}
        <span class="btn small current" title={t('ai.llama.upToDateHint', { tag: latest ?? '' })}><Icon name="check" size={14} />{t('ai.llama.upToDate')}</span>
      {:else}
        <button class="btn primary small" disabled={app.jobs['llama']?.state === 'running'} onclick={() => run(() => api('ai.llama.install'))}>
          <Icon name="download" size={14} />{updateAvailable ? t('ai.llama.update', { tag: latest ?? '' }) : t('ai.llama.install')}
        </button>
      {/if}
    </div>
    {#if status?.llama.recommendedVariant === 'cuda'}<p class="small muted hint">{t('ai.llama.cudaHint')}</p>{/if}
    {#if status?.llama.installed.length}
      <div class="table-scroll"><table class="table">
        <tbody>
          {#each status.llama.installed as l (l.path)}
            <tr>
              <td><strong>{l.tag === 'custom' ? t('ai.llama.custom') : l.tag}</strong> <span class="muted">{l.variant === '?' ? '' : t(`ai.llama.variants.${l.variant}`)}</span>
                {#if !l.complete}<span class="badge warn" title={t('ai.llama.incompleteHint')}>{t('ai.llama.incomplete')}</span>{/if}</td>
              <td><code class="small">{l.path}</code></td>
              <td class="right">
                {#if l.current && l.complete}<span class="badge ok">{t('ai.llama.current')}</span>
                {:else if !l.complete}<button class="btn small" disabled={app.jobs['llama']?.state === 'running'} onclick={() => run(() => api('ai.llama.install'))}>{t('ai.llama.reinstall')}</button>
                {:else}<button class="btn small" onclick={async () => { await run(() => api('ai.llama.select', { name: l.tag + '-' + l.variant })); await load(); }}>{t('ai.llama.use')}</button>{/if}
              </td>
            </tr>
          {/each}
        </tbody>
      </table></div>
    {:else if status}
      <p class="muted">{t('ai.llama.none')}</p>
    {/if}
    <JobView id="llama" />
  </section>

  <!-- Models -->
  <section class="card">
    <div class="card-head"><Icon name="sparkle" /><h2>{t('ai.models.title')}</h2>
      <span class="spacer"></span>
      <button class="btn small" onclick={addLocal}><Icon name="folder" size={14} />{t('ai.models.addLocal')}</button>
      <button class="btn ghost small" onclick={() => run(() => api('app.openFolder', { key: 'models' }))}><Icon name="external" size={14} />{t('common.openFolder')}</button>
    </div>
    {#if status}<p class="small muted">{t('ai.models.folder', { path: status.modelsDir })}</p>{/if}
    {#if status && status.models.length === 0}<div class="note"><Calcifer /><p>{t('ai.models.empty')}</p></div>{/if}
    <ul class="models">
      {#each status?.models ?? [] as m (m.id)}
        <li>
          <div class="grow">
            <strong>{m.name}</strong>
            {#if m.note}<span class="small muted"> · {m.note}</span>{/if}
            {#if m.mtp}<span class="badge">MTP</span>{/if}
            {#if m.mmprojPresent}<span class="badge ok" title={m.mmprojPath ?? ''}><Icon name="check" size={11} />{t('ai.models.vision')}</span>
            {:else if m.mmprojUrl && m.downloaded}<span class="badge warn">{t('ai.models.visionMissing')}</span>{/if}
            <p class="small muted break">
              {#if m.local}{m.downloaded ? t('ai.models.local', { size: formatBytes(m.size) }) : t('ai.models.localMissing', { path: m.path })} · <code>{m.path}</code>
              {:else}{m.downloaded ? t('ai.models.downloaded', { size: formatBytes(m.size) }) : t('ai.models.notDownloaded')}{/if}
            </p>
          </div>
          {#if m.local}
            {#if m.mine}<button class="btn ghost small" title={t('ai.models.removeLocal')} aria-label={t('ai.models.removeLocal')} onclick={() => deleteModel(m)}><Icon name="x" size={14} /></button>{/if}
          {:else if m.downloaded}
            {#if m.mmprojUrl && !m.mmprojPresent}
              <button class="btn small" disabled={app.jobs['model:' + m.id]?.state === 'running'} onclick={() => run(() => api('ai.models.download', { id: m.id }))}><Icon name="download" size={14} />{t('ai.models.downloadMmproj')}</button>
            {/if}
            <button class="btn ghost small" title={t('common.delete')} aria-label={t('common.delete')} onclick={() => deleteModel(m)}><Icon name="trash" size={14} /></button>
          {:else}
            <button class="btn small" disabled={app.jobs['model:' + m.id]?.state === 'running'} onclick={() => run(() => api('ai.models.download', { id: m.id }))}><Icon name="download" size={14} />{t('ai.models.download')}</button>
          {/if}
          <div class="full"><JobView id={'model:' + m.id} compact /></div>
        </li>
      {/each}
    </ul>
    <p class="small muted">{t('ai.models.orUrl')}</p>
    <div class="add">
      <label class="field"><span class="small">{t('ai.models.addUrl')}</span><input bind:value={newModel.url} spellcheck="false" /></label>
      <label class="field"><span class="small">{t('ai.models.addName')}</span><input bind:value={newModel.name} /></label>
      <button class="btn small" disabled={!newModel.url.trim()} onclick={addModel}><Icon name="plus" size={14} />{t('ai.models.add')}</button>
    </div>
  </section>

  <!-- llama-server -->
  <section class="card">
    <div class="card-head">
      <Icon name="server" />
      <h2>{t('ai.server.title')}</h2>
      {#if status?.llama.serverRunning}<span class="badge ok"><span class="dot"></span>{t('ai.server.running', { port: String(status.llama.port), model: status.llama.serverModel ?? '' })}</span>{/if}
    </div>
    {#if settings && status}
      <div class="server">
        <label class="field"><span>{t('ai.server.model')}</span>
          <select bind:value={serverModel} disabled={status.llama.serverRunning}>
            {#each ready as m (m.id)}<option value={m.id}>{m.name}</option>{/each}
          </select>
        </label>
        <label class="field"><span>{t('ai.server.profile')}</span>
          <select bind:value={profile} disabled={status.llama.serverRunning}>
            {#each status.profiles as p (p.id)}<option value={p.id}>{t(`ai.server.profiles.${p.id}`)}</option>{/each}
          </select>
        </label>
        <label class="field narrow"><span>{t('ai.server.port')}</span><input type="number" bind:value={settings.llamaPort} onchange={() => saveSettings()} disabled={status.llama.serverRunning} /></label>
      </div>
      <p class="small muted">{t('ai.server.profileHint')}</p>
      <label class="field args"><span>{t('ai.server.args')}</span><input bind:value={settings.llamaArgs} onchange={() => saveSettings()} class="mono" spellcheck="false" placeholder="--threads 8" /></label>

      {#if status.llama.serverRunning}
        <div class="check">
          <span class="small muted">{t('ai.server.check')}</span>
          {#if !status.llama.loaded}<span class="badge">{t('ai.server.offloadedPending')}</span>
          {:else}
            {#if status.llama.device}<span class="badge ok"><Icon name="check" size={12} />{status.llama.device}</span>{/if}
            {#if status.llama.offloaded && !partial}<span class="badge ok">{t('ai.server.offloaded', { layers: status.llama.offloaded })}</span>{/if}
          {/if}
          {#if vramFree != null}<span class="badge {vramFree < 512 ? 'warn' : 'ok'}">{t('ai.server.vramFree', { free: vramFree.toLocaleString() })}</span>{/if}
          {#if status.llama.generationTps != null}<span class="badge info">{t('ai.server.generation', { tps: status.llama.generationTps.toLocaleString() })}</span>{/if}
          {#if status.llama.promptTps != null}<span class="badge">{t('ai.server.prompt', { tps: status.llama.promptTps.toLocaleString() })}</span>{/if}
          <button class="btn ghost small" onclick={() => { forget('ai.'); void load(); }}><Icon name="refresh" size={13} />{t('common.refresh')}</button>
        </div>
        {#if onCpu}<div class="note warn"><Calcifer /><p class="small">{t('ai.server.onCpu')}</p></div>{/if}
        {#if partial}<div class="note warn"><Calcifer /><p class="small">{t('ai.server.offloadedPartial', { layers: status.llama.offloaded ?? '' })}</p></div>{/if}
        {#if vramFree != null && vramFree < 512}<div class="note warn"><Calcifer /><p class="small">{t('ai.server.vramLow', { free: vramFree.toLocaleString() })}</p></div>{/if}
      {/if}
      {#if status.llama.lastError}<p class="small error">{t('ai.server.lastError', { line: status.llama.lastError })}</p>{/if}

      <div class="row">
        {#if status.llama.serverRunning}
          <button class="btn" onclick={async () => { await run(() => api('ai.server.stop')); await load(); }}><Icon name="stop" size={14} />{t('ai.server.stop')}</button>
          <button class="btn ghost" onclick={() => run(() => api('app.openExternal', { url: `http://127.0.0.1:${status!.llama.port}/` }))}><Icon name="external" size={14} />{t('ai.server.openUi')}</button>
        {:else}
          <button class="btn primary" disabled={!!startBlocked} onclick={startServer}><Icon name="play" size={14} />{t('ai.server.start')}</button>
          {#if startBlocked}<span class="small muted">{t(`ai.server.blocked.${startBlocked}`)}</span>{/if}
        {/if}
      </div>
    {/if}
  </section>

  <!-- Pi -->
  {#if status}
    <section class="card stack tight">
      <div class="card-head">
        <Icon name="ai" />
        <div class="grow"><h2>{t('ai.pi.title')}</h2><p class="small muted">{t('ai.pi.subtitle')}</p></div>
        <span class="badge {status.pi.installed ? 'ok' : ''}">{status.pi.installed ? (status.pi.version ? t('ai.pi.version', { version: status.pi.version }) : t('ai.pi.installed')) : t('ai.pi.notInstalled')}</span>
        {#if status.pi.configured}<span class="badge ok"><Icon name="check" size={12} />{t('ai.pi.configuredBadge')}</span>{/if}
      </div>
      {#if !status.pi.nodeFound}
        <div class="note warn"><Calcifer /><p class="small">{t('ai.pi.noNode')}</p></div>
      {:else if !status.pi.configured}
        <p class="small muted">{t('ai.pi.notConfigured')}</p>
      {/if}
      <div class="row">
        {#if !status.pi.installed}
          <button class="btn primary small" disabled={!status.pi.nodeFound || app.jobs['pi']?.state === 'running'} onclick={() => run(() => api('ai.pi.install'))}><Icon name="download" size={14} />{t('ai.pi.install')}</button>
        {/if}
        {#if piPackages?.pi.updateAvailable}
          <button class="btn small" disabled={piRunning} onclick={() => run(() => api('ai.pi.update'))}><Icon name="download" size={14} />{t('ai.pi.updateTo', { version: piPackages.pi.latest ?? '' })}</button>
        {/if}
        <button class="btn small {status.pi.installed && !status.pi.configured ? 'primary' : ''}" onclick={configurePi}><Icon name="check" size={14} />{t('ai.pi.configure')}</button>
      </div>
      <JobView id="pi" />

      <h3 class="ext-title">{t('ai.pi.extensions')}</h3>
      <p class="small muted">{t('ai.pi.extensionsHint')}</p>
      {#if !piPackages}<p class="small muted">{t('ai.pi.checking')}</p>{/if}
      <ul class="models ext">
        {#each piPackages?.packages ?? [] as p (p.id)}
          <li>
            <div class="grow">
              <strong>{p.npm}</strong>
              <p class="small muted">{p.note ?? t(`ai.pi.packages.${p.id}`)}</p>
            </div>
            {#if p.installed}
              <span class="badge {p.updateAvailable ? 'warn' : 'ok'}">{p.installed === '?' ? t('ai.pi.installedUnknown') : t('ai.pi.installedVersion', { version: p.installed })}</span>
              {#if p.updateAvailable}
                <button class="btn small" disabled={piRunning || !status.pi.installed} onclick={() => run(() => api('ai.pi.package', { id: p.id, update: true }))}><Icon name="download" size={14} />{t('ai.pi.updateTo', { version: p.latest ?? '' })}</button>
              {:else if p.latest}<span class="small muted">{t('ai.pi.upToDate')}</span>{/if}
            {:else}
              {#if p.latest}<span class="small muted">{t('ai.pi.latest', { version: p.latest })}</span>{/if}
              <button class="btn small" disabled={piRunning || !status.pi.installed} onclick={() => run(() => api('ai.pi.package', { id: p.id }))}><Icon name="download" size={14} />{t('ai.pi.installOne')}</button>
            {/if}
            <div class="full"><JobView id={'pi:' + p.id} compact /></div>
          </li>
        {/each}
      </ul>
      <p class="small muted">{t('ai.pi.start')}</p>
      <p class="row"><code>{status.pi.command}</code>
        <button class="btn ghost small" aria-label={t('common.copy')} onclick={async () => { await copyText(status!.pi.command); toast('ok', t('common.copied')); }}><Icon name="copy" size={13} /></button></p>
    </section>
  {/if}
</div>

<style>
  .full { grid-column: 1 / -1; }
  .full:empty { display: none; }
  .narrow input { width: 90px; }
  .gpu { display: flex; flex-direction: column; gap: 6px; margin-bottom: 8px; }
  .right { text-align: right; }
  .variant { width: auto; min-width: 160px; }
  .models { list-style: none; margin: 8px 0; padding: 0; display: flex; flex-direction: column; }
  .models li { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; padding: 10px 0; border-top: 1px solid var(--border); }
  .models li:first-child { border-top: none; }
  .grow { flex: 1; min-width: 240px; }
  .add { display: grid; grid-template-columns: 2fr 1fr auto; gap: 8px; margin-top: 8px; align-items: end; }
  .add > .btn { min-height: 32px; justify-self: start; }
  .server { display: grid; grid-template-columns: 1.4fr 1.4fr 100px; gap: 10px; margin-bottom: 6px; }
  .args { margin: 10px 0; display: flex; flex-direction: column; gap: 4px; }
  .check { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; margin-bottom: 10px; }
  .error { color: var(--error); }
  .hint { margin: -4px 0 10px; }
  .ext-title { margin-top: 10px; }
  .ext li { padding: 8px 0; }
  @media (max-width: 1100px) {
    .server, .add { grid-template-columns: 1fr; }
  }
  .current { background: var(--ok-soft); border-color: color-mix(in srgb, var(--ok) 40%, transparent); color: var(--ok); cursor: default; }
</style>

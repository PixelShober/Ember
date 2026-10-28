// Sample host for developing the UI in a normal browser (npm run dev) and for screenshots.
// Nothing here touches the system – it only imitates what Ember.exe answers.
import { BridgeError, type Bridge } from './bridge';
import { t } from './i18n.svelte';
import type {
  AiStatus, PlaywrightState, AppInfo, CheckResult, GitIdentityInfo, JobSnapshot, LogEntry, PowerState,
  ProxyRow, PxStatus, SettingsView, SoftwareList, StepSnapshot, UpdateStatus, UserSettings, WolLocalState,
} from './types';

const wait = (ms: number) => new Promise((r) => setTimeout(r, ms));
const now = () => new Date().toISOString();

export function createMockBridge(): Bridge {
  const listeners = new Map<string, Set<(d: any) => void>>();
  const emit = (event: string, data: unknown) => listeners.get(event)?.forEach((h) => h(data));
  const log = (message: string, level: LogEntry['level'] = 'info', source = 'ember') =>
    emit('log', [{ time: now(), level, source, message }]);

  const lang = new URLSearchParams(location.search).get('lang') ?? 'de';

  const user: UserSettings = {
    usePac: null, pacUrl: null, upstreamProxy: null, internalDomains: null, port: null,
    installDir: 'C:\\Ember\\px', localZip: '', autostart: true,
    configureEnvVars: true, configureGit: true, configureNpm: true, configurePip: true, configureNuGet: true,
    language: lang === 'auto' ? 'auto' : lang, hideAdvanced: false, teamConfigPath: '\\\\fs01\\entwicklung\\ember\\team.json', autoUpdate: true,
    modelsDir: '', llamaVariant: 'auto', llamaCurrent: 'b6890-cuda', llamaPort: 8080, llamaArgs: '-ngl 99 -c 8192',
    models: [],
  };

  const team = {
    path: '\\\\fs01\\entwicklung\\ember\\team.json', source: 'settings' as const, loadedAt: now(), fromCache: false,
    error: null, version: 3, name: 'Team Entwicklung',
  };

  const info: AppInfo = {
    version: '1.0.0', uiVersion: '1.0.0', bridge: 1, mode: 'installed', language: lang as AppInfo['language'],
    languageSetting: user.language, windowsLanguage: 'de', user: 'FIRMA\\jmoessner', computer: 'NB-ENT-0427', elevated: false,
    admin: true, showAdvanced: true, root: 'C:\\Ember', logDir: 'C:\\Ember\\Logs', team, teamName: 'Team Entwicklung', serviceCenter: 'servicecenter@firma.example',
    repo: 'https://github.com/PixelShober/Ember', docs: 'https://pixelshober.github.io/Ember/',
  };

  let px: PxStatus = { installed: true, running: true, portOpen: true, debug: false, url: 'http://127.0.0.1:3128', configOutdated: false };
  const pxUrl = px.url;

  const rows = (): ProxyRow[] => [
    { tool: 'windows', label: t('proxy.tools.windows'), value: 'PAC http://pac.firma.example/proxy.pac', state: 'info', canSet: false, canRemove: false, hint: t('proxy.hints.windows') },
    { tool: 'env', label: t('proxy.tools.env'), value: `HTTP_PROXY=${pxUrl} · HTTPS_PROXY=${pxUrl} · NO_PROXY=localhost,127.0.0.1,::1,firma.local`, state: 'px', canSet: false, canRemove: true, hint: null },
    { tool: 'git', label: 'git', value: pxUrl, state: 'px', canSet: false, canRemove: true, hint: null },
    { tool: 'npm', label: 'npm', value: `proxy=${pxUrl} · https-proxy=${pxUrl}`, state: 'px', canSet: false, canRemove: true, hint: null },
    { tool: 'nodeCerts', label: t('proxy.tools.nodeCerts'), value: '–', state: 'unset', canSet: true, canRemove: false, hint: t('proxy.hints.nodeCerts') },
    { tool: 'nuget', label: t('proxy.tools.nuget'), value: '–', state: 'unset', canSet: true, canRemove: false, hint: t('proxy.hints.nuget') },
    { tool: 'pip', label: 'pip', value: 'http://proxy.firma.example:8080', state: 'other', canSet: true, canRemove: false, hint: null },
    { tool: 'vscode', label: 'VS Code', value: '–', state: 'unset', canSet: true, canRemove: false, hint: t('proxy.hints.vscode') },
  ];
  const proxyRows = rows();

  const update: UpdateStatus = {
    current: '1.0.0', uiCurrent: '1.0.0', latest: '1.0.0', uiAvailable: false, exeAvailable: false,
    uiReady: null, exeReady: false, checking: false, lastCheck: now(), error: null, notes: null, source: 'github',
  };

  const jobs = new Map<string, JobSnapshot>();

  /** Plays a job step by step so the UI can be tried out. */
  async function simulate(id: string, kind: string, title: string, stepTitles: string[], failAt = -1) {
    const steps: StepSnapshot[] = stepTitles.map((s) => ({ title: s, status: 'pending', detail: '', duration: '', progress: null, progressVisible: false }));
    const job: JobSnapshot = { id, kind, title, state: 'running', failedIndex: -1, progress: 0, status: '', started: now(), steps };
    const push = () => {
      job.progress = (100 * steps.filter((s) => s.status === 'done' || s.status === 'skipped').length) / steps.length;
      jobs.set(id, structuredClone(job));
      emit('job', structuredClone(job));
    };
    push();
    for (let i = 0; i < steps.length; i++) {
      const s = steps[i];
      s.status = 'running';
      s.progressVisible = true;
      job.status = s.title + ' …';
      push();
      for (let p = 0; p <= 100; p += 25) {
        s.progress = p;
        s.detail = p < 100 ? `${p} %` : '';
        push();
        await wait(120);
      }
      s.progressVisible = false;
      if (i === failAt) {
        s.status = 'failed';
        s.detail = 'HTTP 407 Proxy Authentication Required – der Proxy verlangt eine Anmeldung';
        job.state = 'failed';
        job.failedIndex = i;
        job.status = t('jobs.failedAt', { step: s.title });
        push();
        return;
      }
      s.status = 'done';
      s.duration = '0,6 s';
      s.detail = 'OK';
      log(`✔ ${s.title}`);
      push();
    }
    job.state = 'done';
    job.status = t('jobs.done', { title });
    push();
  }

  const power: PowerState = {
    schemeGuid: '381b4222-f694-41f0-9685-ff5bb260df2e', schemeName: 'Ausbalanciert', activeGuid: '381b4222-f694-41f0-9685-ff5bb260df2e',
    activeName: 'Ausbalanciert', activeIsTarget: true, activeSchemeByPolicy: false,
    profiles: [
      {
        id: 'ai-performance', name: t('power.profiles.ai-performance.name'), description: t('power.profiles.ai-performance.description'),
        status: 'partial', atDefault: false, hasBackup: true,
        settings: [
          { alias: 'PERFBOOSTMODE', guid: 'be337238', subGuid: '', name: 'Leistungssteigerungsmodus für Prozessoren', unit: null, ac: { current: 2, target: 2, default: 2 }, dc: { current: 2, target: null, default: 2 }, supported: true, locked: false, options: { '0': 'Deaktiviert', '1': 'Aktiviert', '2': 'Hoch' } },
          { alias: 'PERFEPP', guid: '36687f9e-6863', subGuid: '', name: 'Richtlinie für die bevorzugte Prozessorenergieeffizienz', unit: '%', ac: { current: 0, target: 0, default: 33 }, dc: { current: 50, target: null, default: 50 }, supported: true, locked: false, options: {} },
          { alias: 'PERFEPP1', guid: '36687f9e-6864', subGuid: '', name: 'Richtlinie für die bevorzugte Prozessorenergieeffizienz für Prozessorleistungseffizienzklasse 1', unit: '%', ac: { current: 25, target: 0, default: 25 }, dc: { current: 50, target: null, default: 50 }, supported: true, locked: false, options: {} },
          { alias: 'PERFEPP2', guid: '36687f9e-6865', subGuid: '', name: 'Richtlinie für die bevorzugte Prozessorenergieeffizienz für Prozessorleistungseffizienzklasse 2', unit: '%', ac: { current: 33, target: 0, default: 33 }, dc: { current: 50, target: null, default: 50 }, supported: true, locked: false, options: {} },
          { alias: 'PROCTHROTTLEMAX', guid: 'bc5038f7', subGuid: '', name: 'Maximaler Leistungszustand des Prozessors', unit: '%', ac: { current: 100, target: 100, default: 100 }, dc: { current: 100, target: null, default: 100 }, supported: true, locked: false, options: {} },
          { alias: 'PROCTHROTTLEMIN', guid: '893dee8e', subGuid: '', name: 'Minimaler Leistungszustand des Prozessors', unit: '%', ac: { current: 5, target: 5, default: 5 }, dc: { current: 5, target: null, default: 5 }, supported: true, locked: false, options: {} },
        ],
      },
      {
        id: 'sleep-ac', name: t('power.profiles.sleep-ac.name'), description: t('power.profiles.sleep-ac.description'),
        status: 'notApplied', atDefault: true, hasBackup: false,
        settings: [
          { alias: 'STANDBYIDLE', guid: '29f6c1db', subGuid: '', name: 'Energie sparen nach', unit: 'Sekunden', ac: { current: 1800, target: 0, default: 1800 }, dc: { current: 900, target: null, default: 900 }, supported: true, locked: false, options: {} },
          { alias: 'HIBERNATEIDLE', guid: '9d7815a6', subGuid: '', name: 'Ruhezustand nach', unit: 'Sekunden', ac: { current: 0, target: 0, default: 0 }, dc: { current: 0, target: null, default: 0 }, supported: true, locked: false, options: {} },
          { alias: 'VIDEOIDLE', guid: '3c0bc021', subGuid: '', name: 'Bildschirm ausschalten nach', unit: 'Sekunden', ac: { current: 600, target: 900, default: 600 }, dc: { current: 300, target: null, default: 300 }, supported: true, locked: true, options: {} },
        ],
      },
      {
        id: 'lid-ac', name: t('power.profiles.lid-ac.name'), description: t('power.profiles.lid-ac.description'),
        status: 'applied', atDefault: false, hasBackup: true,
        settings: [
          { alias: 'LIDACTION', guid: '5ca83367', subGuid: '', name: 'Zuklappen des Deckels', unit: null, ac: { current: 0, target: 0, default: 1 }, dc: { current: 1, target: null, default: 1 }, supported: true, locked: false, options: { '0': 'Nichts unternehmen', '1': 'Energie sparen' } },
          { alias: 'PBUTTONACTION', guid: '7648efa3', subGuid: '', name: 'Netzschalterbetätigung', unit: null, ac: { current: 3, target: 3, default: 1 }, dc: { current: 1, target: null, default: 1 }, supported: true, locked: false, options: { '1': 'Energie sparen', '3': 'Herunterfahren' } },
        ],
      },
    ],
  };

  const wol: WolLocalState = {
    adapters: [
      {
        name: 'Dock-LAN', description: 'Realtek USB GbE Family Controller #2', mac: '02-00-5E-10-00-01', status: 'Up', armed: true, ready: true,
        props: [
          { keyword: '*WakeOnMagicPacket', name: 'Bei Magic Packet aufwecken', value: '1', display: 'Aktiviert', target: '1', required: true, ok: true },
          { keyword: 'S5WakeOnLan', name: 'Wake-On-Lan herunterfahren', value: '1', display: 'Aktiviert', target: '1', required: true, ok: true },
          { keyword: '*WakeOnPattern', name: 'Bei Musterübereinstimmung aufwecken', value: '1', display: 'Aktiviert', target: '1', required: false, ok: true },
          { keyword: 'WakeOnLinkChange', name: 'Wake on link change', value: '1', display: 'Aktiviert', target: '1', required: false, ok: true },
          { keyword: 'WolShutdownLinkSpeed', name: 'WoL / Link-Geschwindigkeit reduzieren', value: '0', display: '10 Mbit/s zuerst', target: '0', required: false, ok: true },
        ],
      },
      {
        name: 'Ethernet-LAN-Port', description: 'Intel(R) Ethernet Controller I226-LM', mac: '02-00-5E-10-00-02', status: 'Disconnected', armed: true, ready: true,
        props: [
          { keyword: '*WakeOnMagicPacket', name: 'Akt. über Magic Packet', value: '1', display: 'Aktiviert', target: '1', required: true, ok: true },
          { keyword: 'WakeOnMagicPacketFromS5', name: 'Wake-on-Magic-Packet von S5', value: '1', display: 'Aktiviert', target: '1', required: true, ok: true },
          { keyword: '*WakeOnPattern', name: 'Akt. bei Musterübereinstimmung', value: '1', display: 'Aktiviert', target: '1', required: false, ok: true },
        ],
      },
    ],
    hiberboot: 0, fastStartupOff: true, ready: true, hasBackup: false,
  };

  const checks: CheckResult[] = [
    { id: 'gpo', title: t('system.checks.gpo.title'), status: 'ok', value: t('system.checks.gpo.applied', { count: 7 }), detail: 'GPO-Benutzer-Basis\nGPO-Entwickler-Proxy\nGPO-Laufwerkszuordnung Ü-Drive\nGPO-Office\nGPO-Sicherheit\nGPO-Softwarecenter\nGPO-Zertifikate', actions: [{ id: 'gpupdate', label: t('system.checks.gpo.actions.gpupdate'), elevated: false }] },
    { id: 'executionPolicy', title: t('system.checks.executionPolicy.title'), status: 'warn', value: 'Restricted', detail: 'MachinePolicy: Undefined\nUserPolicy: Undefined\nProcess: Undefined\nCurrentUser: Undefined\nLocalMachine: Restricted', actions: [{ id: 'remoteSigned', label: t('system.checks.executionPolicy.actions.remoteSigned'), elevated: false }] },
    { id: 'gitLongPaths', title: t('system.checks.gitLongPaths.title'), status: 'ok', value: 'true', detail: null, actions: [] },
    { id: 'winLongPaths', title: t('system.checks.winLongPaths.title'), status: 'warn', value: t('common.off'), detail: null, actions: [{ id: 'enable', label: t('system.checks.winLongPaths.actions.enable'), elevated: true }] },
    { id: 'proxyPolicy', title: t('system.checks.proxyPolicy.title'), status: 'info', value: t('system.checks.proxyPolicy.managed'), detail: 'HKCU: AutoConfigURL = http://pac.firma.example/proxy.pac', actions: [] },
    { id: 'devMode', title: t('system.checks.devMode.title'), status: 'info', value: t('common.off'), detail: t('system.checks.devMode.hint'), actions: [] },
  ];

  const software: SoftwareList = {
    clientPresent: true, clientVersion: '5.00.9132.1011',
    devTools: [
      { id: 'nodejs', name: 'Node.js LTS (npm)', description: t('devtools.nodejs.description'), installedVersion: '24.11.0', installedName: 'Node.js', sccm: null, method: 'direct', needsAdmin: false },
      { id: 'git', name: 'Git for Windows', description: t('devtools.git.description'), installedVersion: '2.47.1', installedName: 'Git', sccm: { kind: 'package', name: 'Git for Windows 2.47', packageId: 'FIR00042', programId: 'Install', appId: null, version: '2.47', installState: null, evaluationState: 12, lastRunStatus: 'Succeeded' }, method: 'sccm', needsAdmin: false },
      { id: 'vscode', name: 'Visual Studio Code', description: t('devtools.vscode.description'), installedVersion: null, installedName: null, sccm: null, method: 'direct', needsAdmin: false },
      { id: 'python', name: 'Python 3', description: t('devtools.python.description'), installedVersion: null, installedName: null, sccm: null, method: 'direct', needsAdmin: false },
      { id: 'visualstudio', name: 'Visual Studio Professional', description: t('devtools.visualstudio.description'), installedVersion: '17.14.12', installedName: 'Visual Studio Professional 2022', sccm: null, method: 'direct', needsAdmin: true },
      { id: 'dotnet', name: '.NET SDK (LTS)', description: t('devtools.dotnet.description'), installedVersion: '10.0.100', installedName: 'Microsoft .NET SDK 10.0.100 (x64)', sccm: null, method: 'direct', needsAdmin: true },
      { id: 'pwsh', name: 'PowerShell 7', description: t('devtools.pwsh.description'), installedVersion: null, installedName: null, sccm: null, method: 'direct', needsAdmin: true },
      { id: 'bruno', name: 'Bruno', description: t('devtools.bruno.description'), installedVersion: null, installedName: null, sccm: null, method: 'direct', needsAdmin: false },
    ], errors: [],
    items: [
      { key: 'vs', name: 'Visual Studio Professional 2022', category: 'IDE', note: 'Lizenz über das Team', installed: { name: 'Visual Studio Professional 2022', version: '17.14.12', publisher: 'Microsoft Corporation' }, sccm: { kind: 'package', name: 'Visual Studio Professional 2022', packageId: 'FIR00123', programId: 'Installieren', appId: null, version: '17.14', installState: null, evaluationState: 12, lastRunStatus: 'Succeeded' }, link: null, canInstall: true, source: 'team' },
      { key: 'alfaview', name: 'Alfaview', category: 'Kommunikation', note: null, installed: null, sccm: { kind: 'package', name: 'alfaview 9', packageId: 'FIR00456', programId: 'Install', appId: null, version: '9.21', installState: null, evaluationState: 1, lastRunStatus: null }, link: null, canInstall: true, source: 'team' },
      { key: 'stackfield', name: 'Stackfield', category: 'Kommunikation', note: 'Desktop-App für Aufgaben und Räume', installed: { name: 'Stackfield', version: '5.4.1', publisher: 'Stackfield GmbH' }, sccm: { kind: 'package', name: 'Stackfield Desktop', packageId: 'FIR00789', programId: 'Install', appId: null, version: '5.4', installState: null, evaluationState: 12, lastRunStatus: 'Succeeded' }, link: null, canInstall: true, source: 'team' },
      { key: 'terminal', name: 'Windows Terminal', category: 'Werkzeuge', note: null, installed: null, sccm: null, link: null, canInstall: false, source: 'team' },
      ...[['7-Zip', 'FIR01001', '24.08'], ['Notepad++', 'FIR01002', null], ['Microsoft Teams', 'FIR01003', '25.1'], ['Adobe Acrobat Reader', 'FIR01004', '25.001'], ['Citrix Workspace', 'FIR01005', null], ['KeePassXC', 'FIR01006', null]]
        .map(([name, id, version]) => ({ key: 'sccm:' + id, name: name!, category: null, note: null, installed: version ? { name: name!, version, publisher: null } : null,
          sccm: { kind: 'package' as const, name: name!, packageId: id, programId: 'Install', appId: null, version, installState: null, evaluationState: version ? 12 : 1, lastRunStatus: null },
          link: null, canInstall: true, source: 'sccm' as const })),
    ],
  };

  const ai: AiStatus = {
    gpus: [{ name: 'NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU', driver: '581.42', cuda: '13.0', memoryTotalMb: 24463, memoryUsedMb: 1830, powerDraw: 18.4, powerLimit: 175, temperature: 46, utilization: 3 }],
    llama: { installed: [{ tag: 'b6890', variant: 'cuda', path: 'C:\\Ember\\llama.cpp\\b6890-cuda', current: true }, { tag: 'b6811', variant: 'cuda', path: 'C:\\Ember\\llama.cpp\\b6811-cuda', current: false }], current: 'b6890-cuda', recommendedVariant: 'cuda', serverRunning: false, port: 8080, serverModel: null },
    models: [
      { id: 'qwen3-coder-30b', name: 'Qwen3 Coder 30B A3B (Q4_K_M)', url: 'https://huggingface.co/…/Qwen3-Coder-30B-A3B-Instruct-Q4_K_M.gguf', note: 'Gut für Code, passt in 24 GB', downloaded: true, size: 18_556_687_000, path: '' },
      { id: 'gemma-3-12b', name: 'Gemma 3 12B (Q4_K_M)', url: 'https://huggingface.co/…/gemma-3-12b-it-Q4_K_M.gguf', note: null, downloaded: false, size: null, path: '' },
    ],
    modelsDir: 'C:\\Ember\\Models',
  };

  const playwright: PlaywrightState = {
    browsersDir: 'C:\\Users\\jmoessner\\AppData\\Local\\ms-playwright',
    browsers: [{ name: 'chromium-1194', path: '' }, { name: 'chromium_headless_shell-1194', path: '' }, { name: 'ffmpeg-1011', path: '' }],
    downloadHost: null, nodeCerts: false, nodeFound: true,
    project: { dir: 'C:\\Users\\jmoessner\\source\\repos\\kundenportal', hasPackageJson: true, package: '@playwright/test', version: '1.56.1', wanted: '^1.56.0', hasLockFile: true },
  };

  const git: GitIdentityInfo = { gitFound: true, name: null, email: null, suggestedName: 'Jürgen Mößner', suggestedEmail: 'juergen.moessner@firma.example', source: 'ad' };

  const logs: LogEntry[] = [
    { time: now(), level: 'info', source: 'ember', message: 'Ember 1.0.0 gestartet (installiert, Sprache de)' },
    { time: now(), level: 'info', source: 'ember', message: 'Team-Konfiguration geladen: \\\\fs01\\entwicklung\\ember\\team.json' },
    { time: now(), level: 'debug', source: 'ember', message: '> powercfg.exe /qh 381b4222-f694-41f0-9685-ff5bb260df2e' },
    { time: now(), level: 'warning', source: 'ember', message: 'Nicht von Ember gesetzt, bleibt unverändert: pip proxy = http://proxy.firma.example:8080' },
    { time: now(), level: 'info', source: 'px', message: 'Serving at 127.0.0.1:3128 – Kerberos-Anmeldung für FIRMA\\jmoessner erfolgreich' },
  ];

  const settingsView = (): SettingsView => ({
    user,
    team: { usePac: null, pacUrl: null, upstreamProxy: null, internalDomains: 'firma.local, intranet.firma.example', port: null },
    detected: { usePac: true, pacUrl: 'http://pac.firma.example/proxy.pac', upstreamProxy: 'proxy.firma.example:8080' },
    effective: { usePac: user.usePac ?? true, pacUrl: user.pacUrl ?? 'http://pac.firma.example/proxy.pac', upstreamProxy: user.upstreamProxy ?? 'proxy.firma.example:8080', internalDomains: user.internalDomains ?? 'firma.local, intranet.firma.example', port: user.port ?? 3128, pxUrl, detectedPac: 'http://pac.firma.example/proxy.pac' },
    teamStatus: team,
    paths: { settings: 'C:\\Ember\\Config\\settings.json', logs: 'C:\\Ember\\Logs', pxConfig: 'C:\\Users\\jmoessner\\AppData\\Roaming\\px\\px.ini', models: ai.modelsDir },
  });

  const handlers: Record<string, (p: any) => unknown | Promise<unknown>> = {
    'app.info': () => info,
    'app.ready': () => null,
    'app.openExternal': (p) => { window.open(p.url, '_blank'); return null; },
    'app.openFolder': () => null,
    'app.pickFolder': () => 'D:\\Werkzeuge\\px',
    'app.pickFile': () => 'C:\\Users\\jmoessner\\Downloads\\px-v0.10.2-windows-amd64.zip',
    'settings.get': () => settingsView(),
    'settings.save': (p) => { Object.assign(user, p.settings); info.languageSetting = user.language; return settingsView(); },
    'app.setAppearance': () => null,
    'app.toggleAdvanced': () => { user.hideAdvanced = !user.hideAdvanced; info.showAdvanced = !user.hideAdvanced; emit('app.info', { ...info }); return info.showAdvanced; },
    'settings.defaults': () => ({ ...user, usePac: null, pacUrl: null, upstreamProxy: null, internalDomains: null, port: null }),
    'team.status': () => ({ status: team, name: info.teamName }),
    'team.reload': () => settingsView(),
    'proxy.detect': () => ({ pacUrl: 'http://pac.firma.example/proxy.pac', systemProxy: null, pacProxies: ['proxy.firma.example:8080'] }),
    'proxy.diagnose': () => proxyRows,
    'proxy.set': (p) => { const r = proxyRows.find((x) => x.tool === p.tool)!; r.state = 'px'; r.value = pxUrl; r.canSet = false; r.canRemove = true; return `${r.label} → ${pxUrl}`; },
    'proxy.remove': (p) => { const r = proxyRows.find((x) => x.tool === p.tool)!; r.state = 'unset'; r.value = '–'; r.canSet = true; r.canRemove = false; return t('proxy.removed', { items: r.label }); },
    'px.status': () => px,
    'px.install': () => { void simulate('px', 'px', t('px.jobs.install'), ['detectProxy', 'resolveRelease', 'download', 'verifyHash', 'stop', 'extract', 'writeConfig', 'autostart', 'start', 'test', 'envVars', 'git', 'npm', 'nuget', 'pip'].map((k) => t(`px.steps.${k}`)), 9); return null; },
    'px.uninstall': () => { void simulate('px', 'px', t('px.jobs.uninstall'), ['stop', 'removeAutostart', 'removeEnvVars', 'removeGit', 'deleteFiles'].map((k) => t(`px.steps.${k}`))); return null; },
    'px.start': () => { px = { ...px, running: true, portOpen: true }; emit('px.status', px); return null; },
    'px.stop': () => { px = { ...px, running: false, portOpen: false, debug: false }; emit('px.status', px); return 1; },
    'px.restart': () => null,
    'px.debug': () => { px = { ...px, debug: !px.debug }; emit('px.status', px); log('px läuft im Debug-Modus.', 'info', 'px'); return px.debug; },
    'jobs.list': () => [...jobs.values()],
    'jobs.cancel': () => null,
    'jobs.retry': (p) => { const j = jobs.get(p.id); if (j) void simulate(j.id, j.kind, j.title, j.steps.map((s) => s.title)); return null; },
    'jobs.dismiss': (p) => { jobs.delete(p.id); return null; },
    'git.get': () => git,
    'git.set': (p) => { git.name = p.name; git.email = p.email; return git; },
    'software.list': () => software,
    'software.open': () => null,
    'playwright.state': () => playwright,
    'playwright.inspect': (p) => { playwright.project = { dir: p.dir, hasPackageJson: true, package: '@playwright/test', version: '1.56.1', wanted: '^1.56.0', hasLockFile: true }; return playwright; },
    'playwright.install': (p) => { void simulate('playwright', 'playwright', t('playwright.jobs.install', { path: p.dir }), [t('playwright.steps.project'), t('playwright.steps.environment'), t('playwright.steps.npm'), t('playwright.steps.browsers'), t('playwright.steps.check')]); return null; },
    'devtools.latest': (p) => ({ nodejs: '26.12.1', git: '2.47.1', vscode: '1.106.2', python: '3.14.2', visualstudio: t('devtools.channel'), dotnet: '10.0.105', pwsh: '7.5.4', bruno: '2.14.2' } as Record<string, string>)[p.id],
    'devtools.install': (p) => { void simulate('devtool:' + p.id, 'devtool', t('devtools.jobs.install', { name: p.id }), [t('devtools.steps.resolve'), t('devtools.steps.download'), t('devtools.steps.verify'), t('devtools.steps.install'), t('devtools.steps.check')]); return null; },
    'devtools.installMany': (p) => { void simulate('devtools', 'devtool', t('devtools.jobs.installMany', { count: p.tools.length }), p.tools.flatMap((x: { id: string }) => [x.id + ': ' + t('devtools.steps.download'), x.id + ': ' + t('devtools.steps.install')])); return null; },
    'software.install': (p) => { void simulate('software:' + p.key, 'software', t('software.jobs.install', { name: p.name }), [t('software.steps.trigger'), t('software.steps.wait')]); return null; },
    'mail.preview': (p) => ({ to: info.serviceCenter, subject: t(`mail.templates.${p.template}.subject`, { ...p.vars, user: info.user, computer: info.computer }), body: t(`mail.templates.${p.template}.body`, { ...p.vars, user: info.user, computer: info.computer }) }),
    'mail.compose': (p) => ({ method: 'mailto', to: info.serviceCenter, subject: 'Betreff', body: 'Text', reportPath: 'C:\\Ember\\Reports\\ember-diagnose-20261028-141500.txt', reportText: p.attachReport ? 'Ember-Diagnosebericht\n…' : null }),
    'diagnostics.report': () => ({ text: 'Ember-Diagnosebericht\n=====\nRechner: NB-ENT-0427\nBenutzer: FIRMA\\jmoessner', path: 'C:\\Ember\\Reports\\ember-diagnose-20261028-141500.txt' }),
    'power.state': () => power,
    'power.apply': () => ({ changed: 2, skipped: 0, elevated: false, errors: [] }),
    'power.defaults': () => ({ changed: 3, skipped: 0, elevated: false, errors: [] }),
    'power.restore': () => ({ changed: 3, skipped: 0, elevated: false, errors: [] }),
    'wol.local': () => wol,
    'wol.apply': () => { throw new BridgeError('elevationCancelled', t('errors.elevationCancelled')); },
    'wol.reset': () => ({ errors: [], state: wol }),
    'system.checks': () => checks,
    'system.list': () => checks.map((c) => ({ ...c, status: 'pending', value: '', detail: null, actions: [] })),
    'system.check': async (p) => { await new Promise((r) => setTimeout(r, p.id === 'gpo' ? 1500 : 300)); return checks.find((x) => x.id === p.id); },
    'system.action': (p) => { if (p.check === 'winLongPaths') throw new BridgeError('elevationCancelled', t('errors.elevationCancelled')); return 'OK'; },
    'system.adminCommands': () => 'reg add HKLM\\SYSTEM\\CurrentControlSet\\Control\\FileSystem /v LongPathsEnabled /t REG_DWORD /d 1 /f',
    'ai.status': () => ai,
    'ai.llama.install': () => { void simulate('llama', 'llama', t('ai.llama.jobs.install'), ['detect', 'release', 'download', 'extract', 'cleanup'].map((k) => t(`ai.llama.steps.${k}`))); return null; },
    'ai.llama.select': () => null,
    'ai.server.start': (p) => { ai.llama.serverRunning = true; ai.llama.serverModel = p.model; return null; },
    'ai.server.stop': () => { ai.llama.serverRunning = false; return null; },
    'ai.models.download': (p) => { void simulate('model:' + p.id, 'model', t('ai.models.jobs.download', { name: p.id }), [t('ai.models.steps.download'), t('ai.models.steps.verify')]); return null; },
    'ai.models.delete': () => null,
    'update.status': () => update,
    'update.check': async () => { await wait(600); return update; },
    'update.applyUi': () => null,
    'update.applyExe': () => null,
    'log.recent': () => logs,
    'log.clear': () => null,
    'log.save': () => 'C:\\Ember\\Reports\\ember-log.txt',
  };

  return {
    native: false,
    async call<T>(method: string, params: Record<string, unknown> = {}) {
      await wait(80 + Math.random() * 120);
      const handler = handlers[method];
      if (!handler) throw new BridgeError('unknownMethod', t('errors.unknownMethod', { method }));
      return (await handler(params)) as T;
    },
    on(event, handler) {
      let set = listeners.get(event);
      if (!set) listeners.set(event, (set = new Set()));
      set.add(handler);
      return () => set!.delete(handler);
    },
  };
}

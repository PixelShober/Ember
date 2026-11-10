// Sample host for developing the UI in a normal browser (npm run dev) and for screenshots.
// Nothing here touches the system – it only imitates what Ember.exe answers.
import { BridgeError, type Bridge } from './bridge';
import { t } from './i18n.svelte';
import type {
  ModuleId, AiStatus, PlaywrightState, PlaywrightTemplateInfo, TemplateEntry, TeamFileView, AppInfo, CheckResult, GitIdentityInfo, JobSnapshot, LogEntry, PowerState,
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
    installDir: 'C:\\Ember\\px', autostart: true,
    configureEnvVars: true, configurePowerShell: true, configureGit: true, configureNpm: true, configurePip: true, configureVsCode: true, configureNuGet: true,
    language: lang === 'auto' ? 'auto' : lang, hideAdvanced: false, teamConfigPath: '\\\\fs01\\entwicklung\\ember\\team.json', autoUpdate: true,
    modelsDir: '', llamaVariant: 'auto', llamaCurrent: 'b6890-cuda', llamaPort: 8080, llamaProfile: 'lang', llamaArgs: '',
    models: [],
  };

  // ?setup: no usable team file; ?locked: no role in it; ?blocked: on its block list.
  const params = new URLSearchParams(location.search);
  const allModules: ModuleId[] = ['proxy', 'git', 'software', 'playwright', 'system', 'power', 'ai', 'access', 'settings', 'log'];
  const setup = params.has('setup');
  const blocked = params.has('blocked');
  const ownModules: ModuleId[] = setup || blocked || params.has('locked') ? [] : allModules;
  const withTeam = !setup;
  // ?preview=role:Test|none: an admin looks at Ember through another role (app.preview).
  const mockRoles: Record<string, ModuleId[]> = { Admins: allModules, Entwickler: ['proxy', 'playwright', 'git', 'software'], Test: ['proxy', 'playwright'] };
  const preview = ownModules.includes('access') ? params.get('preview') : null;
  const modules: ModuleId[] = !preview ? ownModules
    : preview === 'none' ? []
    : mockRoles[preview.slice(5)] ?? [];

  const team = {
    path: withTeam ? '\\\\fs01\\entwicklung\\ember\\team.ember' : null, source: withTeam ? ('settings' as const) : null, loadedAt: withTeam ? now() : null, fromCache: false,
    error: null, version: withTeam ? 3 : 0, name: withTeam ? 'Team Entwicklung' : null,
  };

  const info: AppInfo = {
    version: '1.0.0', uiVersion: '1.0.0', bridge: 1, mode: 'installed', language: lang as AppInfo['language'],
    languageSetting: user.language, windowsLanguage: 'de', user: 'FIRMA\\jmoessner', displayName: 'Jürgen Mößner', computer: 'NB-ENT-0427', elevated: false,
    modules, grant: { source: setup ? 'none' : 'team', roles: withTeam && modules.length ? ['Admins'] : [] },
    locked: setup ? 'setup' : blocked ? 'blocked' : modules.length ? null : 'notListed',
    preview: ownModules.includes('access') ? { current: preview, roles: Object.keys(mockRoles) } : null,
    admin: modules.includes('access'), advanced: modules.some((m) => ['power', 'ai', 'access'].includes(m)), showAdvanced: true, access: modules.length > 0,
    root: 'C:\\Ember', logDir: 'C:\\Ember\\Logs', team, teamName: withTeam ? 'Team Entwicklung' : null,
    serviceCenter: withTeam ? 'servicecenter@firma.example' : null,
    supervisor: withTeam ? { name: 'Petra Lindner', email: 'petra.lindner@firma.example' } : null,
    repo: 'https://github.com/PixelShober/Ember', docs: 'https://pixelshober.github.io/Ember/',
  };

  /** Starts over with other URL parameters (the real host keeps this state in settings.json). */
  function restartWith(query: string) {
    history.replaceState(null, '', `?${query}${location.hash}`);
    location.reload();
  }

  let px: PxStatus = { installed: true, running: true, portOpen: true, debug: false, url: 'http://127.0.0.1:3128', configOutdated: false };
  const pxUrl = px.url;

  const rows = (): ProxyRow[] => [
    { tool: 'windows', label: t('proxy.tools.windows'), value: 'PAC http://pac.firma.example/proxy.pac', state: 'info', canSet: false, canRemove: false, hint: t('proxy.hints.windows'),
      pac: { proxies: ['proxy.firma.example:8080'], direct: ['firma.local', 'intranet.firma.example', '10.0.0.0/8'], exceptions: ['firma.local', 'intranet.firma.example', '10.0.0.0/8'], source: 'pac' } },
    { tool: 'env', label: t('proxy.tools.env'), value: `HTTP_PROXY=${pxUrl} · HTTPS_PROXY=${pxUrl} · NO_PROXY ${t('proxy.exceptions.current')}`, state: 'px', canSet: false, canRemove: true, hint: null },
    { tool: 'powershell', label: t('proxy.tools.powershell'), value: '–', state: 'unset', canSet: true, canRemove: false,
      hint: t('proxy.hints.powershell', { files: 'C:\\Users\\jmoessner\\Documents\\WindowsPowerShell\\profile.ps1 · C:\\Users\\jmoessner\\Documents\\PowerShell\\profile.ps1' }) },
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

  const profiles: AiStatus['profiles'] = [
    { id: 'standard', context: 98304, kvCache: 'q8_0', mtp: true },
    { id: 'lang', context: 163840, kvCache: 'q4_0', mtp: true },
    { id: 'max', context: 262144, kvCache: 'q4_0', mtp: false },
  ];
  const ai: AiStatus = {
    gpus: [{ name: 'NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU', driver: '581.42', cuda: '13.2', memoryTotalMb: 24463, memoryUsedMb: 1830, powerDraw: 18.4, powerLimit: 175, temperature: 46, utilization: 3 }],
    llama: { installed: [{ tag: 'b12110', variant: 'cuda', path: 'C:\\Ember\\llama.cpp\\b12110-cuda', current: true, complete: true }, { tag: 'b12096', variant: 'cuda', path: 'C:\\Ember\\llama.cpp\\b12096-cuda', current: false, complete: false }],
      current: 'b12110-cuda', recommendedVariant: 'cuda', serverRunning: false, port: 8080, serverModel: null, serverAlias: null, serverProfile: null, serverContext: null, offloaded: null, lastError: null, loaded: false, device: null, promptTps: null, generationTps: null },
    models: [
      { id: 'qwen3.8-27b', name: 'Qwen3.8-27B (UD-Q4_K_XL)', url: 'https://huggingface.co/unsloth/Qwen3.8-27B-GGUF/resolve/main/Qwen3.8-27B-UD-Q4_K_XL.gguf', note: '≈ 17,6 GB · 24 GB VRAM', downloaded: true, size: 17_612_000_000, path: 'C:\\Ember\\Models\\Qwen3.8-27B-UD-Q4_K_XL.gguf', local: false, mine: false, alias: 'qwen3.8-27b', mtp: true, mmprojPath: 'C:\\Ember\\Models\\Qwen3.8-27B-UD-Q4_K_XL.mmproj-F16.gguf', mmprojPresent: true, mmprojUrl: 'https://huggingface.co/unsloth/Qwen3.8-27B-GGUF/resolve/main/mmproj-F16.gguf' },
      { id: 'Devstral-Small-Q4_K_M.gguf', name: 'Devstral-Small-Q4_K_M', url: '', note: null, downloaded: true, size: 14_330_000_000, path: 'D:\\KI\\Devstral-Small-Q4_K_M.gguf', local: true, mine: true, alias: 'devstral-small-q4_k_m', mtp: false, mmprojPath: null, mmprojPresent: false, mmprojUrl: null },
    ],
    modelsDir: 'C:\\Ember\\Models',
    profiles,
    profile: 'standard',
    pi: { installed: false, nodeFound: true, configured: false, configFile: 'C:\\Users\\jmoessner\\.pi\\agent\\models.json', command: 'pi --provider llama-cpp --model qwen3.8-27b', version: null },
  };

  let pwFailedOnce = false;
  let teamView: TeamFileView = {
    doc: {
      serviceCenter: { email: 'servicecenter@firma.example', name: 'IT-Service-Center', subjectPrefix: '[Ember]', problemIntro: '' },
      supervisor: { name: 'Petra Lindner', firstName: '', email: 'petra.lindner@firma.example' },
      access: {
        roles: [
          { name: 'Admins', modules: ['*'], members: [{ name: 'FIRMA\\jmoessner', kind: 'user' }, { name: 'FIRMA\\GG-Ember-Admins', kind: 'group' }], hideTools: [] },
          { name: 'Entwickler', modules: ['proxy', 'playwright', 'git', 'software'], members: [{ name: 'FIRMA\\GG-Entwicklung', kind: 'group' }, { name: 'FIRMA\\ext.schmidt', kind: 'user' }], hideTools: ['bruno'] },
          { name: 'Test', modules: ['proxy', 'playwright'], members: [{ name: 'FIRMA\\GG-Test', kind: 'group' }, { name: 'FIRMA\\akaya', kind: 'user' }], hideTools: ['bruno'] },
        ],
        blocked: [{ name: 'FIRMA\\praktikant.alt', kind: 'user' }],
      },
      update: { mirror: '' },
      playwright: { template: '\\\\fs01\\entwicklung\\vorlagen\\playwright', downloadHost: '' },
      mailTemplates: { softwareRequest: { subject: '', body: '' }, problem: { subject: '', body: '' }, adminAction: { subject: '', body: '' } },
    },
    extra: '{\n  "proxy": { "internalDomains": ["firma.local", "intranet.firma.example"] },\n  "devTools": { "allowDirect": true }\n}',
    path: '\\\\fs01\\entwicklung\\ember\\team.ember', version: 12, signedBy: 'FIRMA\\akaya', signedAt: '2026-11-03T16:42:00', keyId: '3f9a 61c2 08de 7b14',
    signatureRequired: true, trusted: true, writable: true, problem: null, account: 'FIRMA\\jmoessner',
    myGroups: ['FIRMA\\Domain Users', 'FIRMA\\GG-Entwicklung', 'FIRMA\\GG-Ember-Admins', 'FIRMA\\GG-VPN'],
    modules: allModules, keyFile: 'H:\\Admin\\ember-team-signing.pem',
  };
  const playwright: PlaywrightState = {
    browsersDir: 'C:\\Users\\jmoessner\\AppData\\Local\\ms-playwright',
    browsers: [{ name: 'chromium-1194', path: '', label: 'Chromium' }, { name: 'chromium_headless_shell-1194', path: '', label: null }, { name: 'ffmpeg-1011', path: '', label: null }, { name: 'firefox-1495', path: '', label: 'Firefox' }, { name: 'winldd-1007', path: '', label: null }],
    downloadHost: null, nodeCerts: false, nodeFound: true, edgeFound: true, vsCodeFound: true,
    defaultParent: 'C:\\Users\\jmoessner\\source\\repos',
    project: { dir: 'C:\\Users\\jmoessner\\source\\repos\\kundenportal', hasPackageJson: true, package: '@playwright/test', version: '1.56.1', wanted: '^1.56.0', hasLockFile: true, config: 'playwright.config.ts', usesEdge: false },
  };

  let pwTemplate: PlaywrightTemplateInfo = {
    source: 'team', path: '\\\\fs01\\entwicklung\\vorlagen\\playwright', name: 'Playwright Web-Tests',
    description: 'Page Objects, Login-Fixture und Edge-Profil, wie im Team üblich.', created: '2026-10-21T09:14:00', createdBy: 'FIRMA\\akaya',
    files: 14, sample: ['package.json', 'playwright.config.ts', 'tsconfig.json', '.gitignore', '.env.example', 'tests/pages/login.page.ts', 'tests/fixtures/auth.ts', 'tests/smoke/', '.vscode/extensions.json', 'README.md'],
    problem: null, writable: true,
  };
  const templateEntries: TemplateEntry[] = [
    ['.vscode', 1, 'keep'], ['.vscode/extensions.json', 0, 'keep'], ['playwright', 1, 'keep'], ['playwright/.auth', 1, 'drop', 'secret'], ['playwright/.auth/user.json', 0, 'drop', 'secret'],
    ['tests', 1, 'keep'], ['tests/data', 1, 'drop', 'data'], ['tests/data/kunden.csv', 0, 'drop', 'data'], ['tests/fixtures', 1, 'keep'], ['tests/fixtures/auth.ts', 0, 'keep'],
    ['tests/pages', 1, 'keep'], ['tests/pages/login.page.ts', 0, 'keep'], ['tests/pages/vertrag.page.ts', 0, 'keep'], ['tests/login.spec.ts', 0, 'drop', 'test'], ['tests/vertrag.spec.ts', 0, 'drop', 'test'],
    ['.env', 0, 'drop', 'secret'], ['.env.example', 0, 'keep'], ['.gitignore', 0, 'keep'], ['package.json', 0, 'keep'], ['playwright.config.ts', 0, 'keep'], ['README.md', 0, 'keep'], ['tsconfig.json', 0, 'keep'],
    ['node_modules', 1, 'skip', 'dependencies'], ['.git', 1, 'skip', 'git'], ['playwright-report', 1, 'skip', 'report'], ['package-lock.json', 0, 'skip', 'generated'],
  ].map(([path, dir, action, reason]) => ({ path: path as string, dir: !!dir, action: action as TemplateEntry['action'], reason: (reason ?? null) as TemplateEntry['reason'], size: 0 }));

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
    detected: { usePac: true, pacUrl: 'http://pac.firma.example/proxy.pac', upstreamProxy: 'proxy.firma.example:8080', internalDomains: 'firma.local, intranet.firma.example, 10.0.0.0/8' },
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
    'app.preview': (p) => {
      const q = new URLSearchParams(location.search);
      if (p.target) q.set('preview', p.target); else q.delete('preview');
      restartWith(q.toString());
      return info;
    },
    'app.toggleAdvanced': () => { user.hideAdvanced = !user.hideAdvanced; info.showAdvanced = !user.hideAdvanced; emit('app.info', { ...info }); return info.showAdvanced; },
    'settings.defaults': () => ({ ...user, usePac: null, pacUrl: null, upstreamProxy: null, internalDomains: null, port: null }),
    'team.status': () => ({ status: team, name: info.teamName }),
    'team.reload': () => (modules.includes('settings') ? settingsView() : null),
    'proxy.detect': () => ({ pacUrl: 'http://pac.firma.example/proxy.pac', systemProxy: null, pacProxies: ['proxy.firma.example:8080'], pacDirect: ['firma.local', 'intranet.firma.example', '10.0.0.0/8'] }),
    'proxy.diagnose': () => proxyRows,
    'team.admin': () => structuredClone(teamView),
    'team.newKey': (p) => ({ path: p.folder + '\\ember-team-signing.pem', keyId: '5be0 93a1 c47f 2d68', publicKey: '-----BEGIN PUBLIC KEY-----\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE…\n-----END PUBLIC KEY-----\n' }),
    'team.save': (p) => {
      teamView = { ...teamView, doc: p.doc, extra: p.extra, version: teamView.version + 1, signedBy: info.user, signedAt: new Date().toISOString(), keyFile: p.keyFile };
      return structuredClone(teamView);
    },
    'access.lookup': (p) => {
      const name = String(p.name).trim();
      if (/^x/i.test(name)) return { name, kind: null, found: false };
      const group = /gruppe|team|admins|entwicklung/i.test(name);
      return { name: name.includes('\\') ? name : `FIRMA\\${name}`, kind: group ? 'group' : 'user', found: true };
    },
    'team.setPath': (p) => {
      if (!/\.json$/i.test(String(p.path))) throw new BridgeError('teamUnreadable', t('errors.teamUnreadable', { path: String(p.path), detail: '–' }));
      restartWith('lang=' + lang + '&clean');
      return info;
    },
    'git.proxy': () => {
      const r = proxyRows.find((x) => x.tool === 'git')!;
      const on = r.state === 'px';
      return { httpProxy: on ? pxUrl : null, state: r.state, domains: ['firma.local', 'intranet.firma.example'], missingExceptions: on ? [] : ['https://firma.local'], noProxyEnv: true, canSet: !on, canRemove: on, sslBackend: on ? 'schannel' : null };
    },
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
    'playwright.template': () => pwTemplate,
    'playwright.template.use': (p) => {
      pwTemplate = p.path
        ? { source: 'user', path: p.path, name: 'Eigene Vorlage', description: null, created: null, createdBy: null, files: 0, sample: [], problem: null, writable: true }
        : { source: 'builtin', path: null, name: null, description: null, created: null, createdBy: null, files: 0, sample: [], problem: null, writable: false };
      return pwTemplate;
    },
    'playwright.template.preview': () => templateEntries,
    'playwright.template.save': (p) => {
      pwTemplate = { ...pwTemplate, name: p.name ?? pwTemplate.name, description: p.description, files: p.keep.length, created: new Date().toISOString(), createdBy: info.user };
      return { count: p.keep.length, template: pwTemplate };
    },
    'playwright.inspect': (p) => { playwright.project = { dir: p.dir, hasPackageJson: true, package: null, version: null, wanted: null, hasLockFile: false, config: null, usesEdge: false }; return playwright; },
    'playwright.install': (p) => {
      const edge = p.browsers === 'msedge';
      const steps = [t('playwright.steps.project'), t('playwright.steps.environment'), t('playwright.steps.npm'), t(edge ? 'playwright.steps.edge' : 'playwright.steps.browsers'), t('playwright.steps.check')];
      // The first browser download "fails" in the preview, so the Edge way out can be seen.
      const fail = !edge && !pwFailedOnce ? 3 : -1;
      pwFailedOnce = true;
      if (playwright.project) Object.assign(playwright.project, { package: '@playwright/test', version: '1.56.1', wanted: '^1.56.1', config: playwright.project.config ?? 'playwright.config.ts', usesEdge: playwright.project.usesEdge || edge });
      void simulate('playwright', 'playwright', t('playwright.jobs.install', { path: p.dir }), steps, fail);
      return null;
    },
    'playwright.create': (p) => {
      const dir = `${p.parent}\\${p.name}`;
      playwright.project = { dir, hasPackageJson: true, package: '@playwright/test', version: '1.56.1', wanted: '^1.56.1', hasLockFile: true, config: 'playwright.config.ts', usesEdge: p.browsers === 'msedge' };
      void simulate('playwright', 'playwright', t('playwright.jobs.create', { name: p.name }), [t('playwright.steps.template'), t('playwright.steps.environment'), t('playwright.steps.npm'), t(p.browsers === 'msedge' ? 'playwright.steps.edge' : 'playwright.steps.browsers'), t('playwright.steps.check')]);
      return null;
    },
    'playwright.useEdge': () => {
      if (playwright.project) playwright.project.usesEdge = true;
      return { file: 'playwright.msedge.config.ts', command: 'npm run test:edge', created: true };
    },
    'playwright.openInCode': () => null,
    'devtools.latest': (p) => ({ nodejs: '26.12.1', git: '2.47.1', vscode: '1.106.2', python: '3.14.2', visualstudio: t('devtools.channel'), dotnet: '10.0.105', pwsh: '7.5.4', bruno: '2.14.2' } as Record<string, string>)[p.id],
    'devtools.install': (p) => { void simulate('devtool:' + p.id, 'devtool', t('devtools.jobs.install', { name: p.id }), [t('devtools.steps.resolve'), t('devtools.steps.download'), t('devtools.steps.verify'), t('devtools.steps.install'), t('devtools.steps.check')]); return null; },
    'devtools.installMany': (p) => { void simulate('devtools', 'devtool', t('devtools.jobs.installMany', { count: p.tools.length }), p.tools.flatMap((x: { id: string }) => [x.id + ': ' + t('devtools.steps.download'), x.id + ': ' + t('devtools.steps.install')])); return null; },
    'software.install': (p) => { void simulate('software:' + p.key, 'software', t('software.jobs.install', { name: p.name }), [t('software.steps.trigger'), t('software.steps.wait')]); return null; },
    'mail.preview': (p) => {
      const vars = { ...p.vars, user: info.user, computer: info.computer, sender: 'Jürgen Mößner', supervisor: 'Petra', problemIntro: t('mail.problemIntro') };
      const to = p.template === 'adminAction' ? info.supervisor?.email ?? null : info.serviceCenter;
      return { to, subject: t(`mail.templates.${p.template}.subject`, vars), body: t(`mail.templates.${p.template}.body`, vars) };
    },
    'mail.compose': () => ({ method: 'mailto', to: info.serviceCenter, subject: 'Betreff', body: 'Text' }),
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
    'access.search': (p) => {
      const all = [
        { name: 'FIRMA\\jmoessner', kind: 'user', display: 'Mößner, Jürgen', detail: 'juergen.moessner@firma.example' },
        { name: 'FIRMA\\jmeyer', kind: 'user', display: 'Meyer, Julia', detail: 'julia.meyer@firma.example' },
        { name: 'FIRMA\\GG-Entwicklung', kind: 'group', display: null, detail: 'Alle Entwicklerinnen und Entwickler' },
        { name: 'FIRMA\\GG-Ember-Admins', kind: 'group', display: null, detail: 'Admins für Ember' },
        { name: 'FIRMA\\GG-Test', kind: 'group', display: null, detail: 'Testteam' },
      ];
      const q = String(p.query).toLowerCase();
      return all.filter((h) => (h.name + (h.display ?? '') + (h.detail ?? '')).toLowerCase().includes(q));
    },
    'ai.llama.latest': () => ({ tag: 'b12110' }),
    'ai.llama.install': () => { void simulate('llama', 'llama', t('ai.llama.jobs.install'), ['detect', 'release', 'download', 'extract', 'devices', 'cleanup'].map((k) => t(`ai.llama.steps.${k}`))); return null; },
    'ai.llama.select': () => null,
    'ai.server.start': (p) => {
      const m = ai.models.find((x) => x.id === p.model)!;
      const profile = profiles.find((x) => x.id === p.profile) ?? profiles[0];
      ai.profile = profile.id;
      user.llamaProfile = profile.id;
      Object.assign(ai.llama, { serverRunning: true, serverModel: m.name, serverAlias: m.alias, serverProfile: profile.id, serverContext: profile.context, offloaded: '65/65', loaded: true, device: 'NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU', promptTps: 1840.2, generationTps: 78.4 });
      ai.gpus[0].memoryUsedMb = 22_900;
      return null;
    },
    'ai.server.stop': () => { Object.assign(ai.llama, { serverRunning: false, offloaded: null }); ai.gpus[0].memoryUsedMb = 1830; return null; },
    'ai.models.addLocal': () => { ai.models.push({ id: 'Mistral-Small-Q4_K_M.gguf', name: 'Mistral-Small-Q4_K_M', url: '', note: null, downloaded: true, size: 13_700_000_000, path: 'D:\\KI\\Mistral-Small-Q4_K_M.gguf', local: true, mine: true, alias: 'mistral-small-q4_k_m', mtp: false, mmprojPath: null, mmprojPresent: false, mmprojUrl: null }); return 'Mistral-Small-Q4_K_M.gguf'; },
    'ai.pi.install': () => { void simulate('pi', 'pi', t('ai.pi.jobs.install'), [t('ai.pi.steps.install'), t('ai.pi.steps.mcp')]).then(() => (ai.pi.installed = true)); return null; },
    'ai.pi.configure': () => { ai.pi.configured = true; return ai.pi.configFile; },
    'ai.pi.packages': async () => {
      await wait(400);
      const pkg = (id: string, npm: string, installed: string | null, latest: string) => ({ id, npm, note: null, installed, latest, updateAvailable: !!installed && installed !== latest });
      return {
        pi: pkg('pi', '@earendil-works/pi-coding-agent', ai.pi.installed ? '1.4.2' : null, '1.5.0'),
        packages: [pkg('mcp', 'pi-mcp-adapter', '0.9.1', '0.9.1'), pkg('websearch', 'pi-web-access', null, '0.47.0'), pkg('pretty', '@heyhuynhgiabuu/pi-pretty', '0.9.0', '0.10.0'),
          pkg('ponytail', '@dietrichgebert/ponytail', null, '5.3.3'), pkg('simplify', 'pi-simplify', null, '0.2.3'), pkg('contextMode', 'context-mode', null, '2.1.7'), pkg('todo', '@juicesharp/rpiv-todo', '2.15.0', '2.15.0')],
      };
    },
    'ai.pi.package': (p) => { void simulate('pi:' + p.id, 'pi', t('ai.pi.jobs.package', { name: String(p.id) }), [t('ai.pi.steps.package', { name: String(p.id) })]); return null; },
    'ai.pi.update': () => { void simulate('pi', 'pi', t('ai.pi.jobs.update'), [t('ai.pi.steps.update', { name: 'pi' })]); return null; },
    'ai.llama.useFolder': () => 'C:\\llama',
    'ai.models.download': (p) => { void simulate('model:' + p.id, 'model', t('ai.models.jobs.download', { name: p.id }), [t('ai.models.steps.download'), t('ai.models.steps.verify')]); return null; },
    'ai.models.delete': (p) => { const m = ai.models.find((x) => x.id === p.id); if (m?.local) ai.models.splice(ai.models.indexOf(m), 1); else if (m) m.downloaded = false; return null; },
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

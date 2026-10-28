// Shapes of the data the host (Ember.Core, C#) sends. Property names are camelCase JSON.

export type Route = 'start' | 'proxy' | 'git' | 'software' | 'power' | 'system' | 'ai' | 'settings' | 'log';

export interface TeamStatus {
  path: string | null;
  source: 'settings' | 'file' | 'env' | null;
  loadedAt: string | null;
  fromCache: boolean;
  error: string | null;
  version: number;
  name: string | null;
}

export interface AppInfo {
  version: string;
  uiVersion: string;
  bridge: number;
  mode: 'installed' | 'portable' | 'dev';
  language: 'de' | 'de-CH' | 'en';
  languageSetting: string;
  /** "de" or "en": what "auto" means on this machine. */
  windowsLanguage: 'de' | 'en';
  user: string;
  computer: string;
  elevated: boolean;
  /** Admin in the team configuration: may use the advanced modules (KI, Energie). */
  admin: boolean;
  /** Advanced tabs visible (admin and not hidden with Ctrl+E). */
  showAdvanced: boolean;
  /** C:\\Ember (or the fallback under %LOCALAPPDATA%). */
  root: string;
  logDir: string;
  team: TeamStatus;
  teamName: string | null;
  serviceCenter: string | null;
  repo: string;
  docs: string;
}

export interface PxStatus {
  installed: boolean;
  running: boolean;
  portOpen: boolean;
  debug: boolean;
  url: string;
  configOutdated: boolean;
}

export type StepStatus = 'pending' | 'running' | 'done' | 'failed' | 'skipped';
export type JobState = 'running' | 'done' | 'failed' | 'cancelled';

export interface StepSnapshot {
  title: string;
  status: StepStatus;
  detail: string;
  duration: string;
  progress: number | null;
  progressVisible: boolean;
}

export interface JobSnapshot {
  id: string;
  kind: string;
  title: string;
  state: JobState;
  failedIndex: number;
  progress: number;
  status: string;
  started: string;
  steps: StepSnapshot[];
}

export type LogLevel = 'debug' | 'info' | 'warning' | 'error';

export interface LogEntry {
  time: string;
  level: LogLevel;
  source: string;
  message: string;
}

export interface UpdateStatus {
  current: string;
  uiCurrent: string;
  latest: string | null;
  uiAvailable: boolean;
  exeAvailable: boolean;
  uiReady: string | null;
  exeReady: boolean;
  checking: boolean;
  lastCheck: string | null;
  error: string | null;
  notes: string | null;
  source: string;
}

export type ProxyState = 'px' | 'other' | 'unset' | 'missing' | 'info';

export interface ProxyRow {
  tool: string;
  label: string;
  value: string;
  state: ProxyState;
  canSet: boolean;
  canRemove: boolean;
  hint: string | null;
}

export interface ModelEntry {
  id: string;
  name: string;
  url: string;
  sha256?: string | null;
  note?: string | null;
}

export interface UserSettings {
  usePac: boolean | null;
  pacUrl: string | null;
  upstreamProxy: string | null;
  internalDomains: string | null;
  port: number | null;
  installDir: string;
  localZip: string;
  autostart: boolean;
  configureEnvVars: boolean;
  configureGit: boolean;
  configureNpm: boolean;
  configurePip: boolean;
  configureNuGet: boolean;
  language: string;
  hideAdvanced: boolean;
  teamConfigPath: string;
  autoUpdate: boolean;
  modelsDir: string;
  llamaVariant: string;
  llamaCurrent: string;
  llamaPort: number;
  llamaArgs: string;
  models: ModelEntry[];
}

export interface TeamProxyValues {
  usePac: boolean | null;
  pacUrl: string | null;
  upstreamProxy: string | null;
  internalDomains: string | null;
  port: number | null;
}

export interface SettingsView {
  user: UserSettings;
  team: TeamProxyValues;
  /** What Windows reported at start (PAC address, first proxy from the PAC file or the system proxy). */
  detected: { usePac: boolean | null; pacUrl: string | null; upstreamProxy: string | null };
  effective: {
    usePac: boolean;
    pacUrl: string;
    upstreamProxy: string;
    internalDomains: string;
    port: number;
    pxUrl: string;
    detectedPac: string | null;
  };
  teamStatus: TeamStatus;
  paths: { settings: string; logs: string; pxConfig: string; models: string };
}

export interface ProxyDetection {
  pacUrl: string | null;
  systemProxy: string | null;
  pacProxies: string[];
}

export interface GitIdentityInfo {
  gitFound: boolean;
  name: string | null;
  email: string | null;
  suggestedName: string | null;
  suggestedEmail: string | null;
  source: 'ad' | 'upn' | null;
}

export interface InstalledApp {
  name: string;
  version: string | null;
  publisher: string | null;
}

export interface SccmItem {
  kind: 'package' | 'application';
  name: string;
  packageId: string | null;
  programId: string | null;
  appId: string | null;
  version: string | null;
  installState: string | null;
  evaluationState: number | null;
  lastRunStatus: string | null;
}

export interface SoftwareItem {
  key: string;
  name: string;
  category: string | null;
  note: string | null;
  installed: InstalledApp | null;
  sccm: SccmItem | null;
  link: string | null;
  canInstall: boolean;
}

export interface DevToolState {
  id: string;
  name: string;
  description: string;
  installedVersion: string | null;
  installedName: string | null;
  sccm: SccmItem | null;
  /** sccm: package in Software Center · direct: from the vendor · mail: only via the service center */
  method: 'sccm' | 'direct' | 'mail';
  needsAdmin: boolean;
}

export interface SoftwareList {
  clientPresent: boolean;
  clientVersion: string | null;
  devTools: DevToolState[];
  items: SoftwareItem[];
  errors: string[];
}

export interface MailResult {
  method: 'outlook' | 'mailto';
  to: string;
  subject: string;
  body: string;
  reportPath: string | null;
  reportText: string | null;
}

export interface PowerValue {
  current: number | null;
  target: number | null;
  default: number | null;
}

export interface PowerSettingState {
  alias: string;
  guid: string;
  subGuid: string;
  name: string;
  unit: string | null;
  ac: PowerValue;
  dc: PowerValue;
  supported: boolean;
  locked: boolean;
  options: Record<string, string>;
}

export interface PowerProfileState {
  id: string;
  name: string;
  description: string | null;
  status: 'applied' | 'partial' | 'notApplied' | 'unsupported';
  atDefault: boolean;
  hasBackup: boolean;
  settings: PowerSettingState[];
}

export interface PowerState {
  schemeGuid: string;
  schemeName: string;
  activeGuid: string;
  activeName: string;
  activeIsTarget: boolean;
  activeSchemeByPolicy: boolean;
  profiles: PowerProfileState[];
}

export interface PowerApplyResult {
  changed: number;
  skipped: number;
  elevated: boolean;
  errors: string[];
}

export interface WolProp {
  keyword: string;
  name: string;
  value: string | null;
  display: string | null;
  target: string;
  required: boolean;
  ok: boolean;
}

export interface WolAdapter {
  name: string;
  description: string;
  mac: string;
  status: string;
  armed: boolean;
  ready: boolean;
  props: WolProp[];
}

export interface WolLocalState {
  adapters: WolAdapter[];
  hiberboot: number | null;
  fastStartupOff: boolean;
  ready: boolean;
  hasBackup: boolean;
}

export interface CheckAction {
  id: string;
  label: string;
  elevated: boolean;
}

export interface CheckResult {
  id: string;
  title: string;
  status: 'ok' | 'warn' | 'locked' | 'info' | 'missing' | 'error' | 'pending';
  value: string;
  detail: string | null;
  actions: CheckAction[];
}

export interface GpuInfo {
  name: string;
  driver: string | null;
  cuda: string | null;
  memoryTotalMb: number | null;
  memoryUsedMb: number | null;
  powerDraw: number | null;
  powerLimit: number | null;
  temperature: number | null;
  utilization: number | null;
}

export interface LlamaInstall {
  tag: string;
  variant: string;
  path: string;
  current: boolean;
}

export interface LlamaStatus {
  installed: LlamaInstall[];
  current: string | null;
  recommendedVariant: string;
  serverRunning: boolean;
  port: number;
  serverModel: string | null;
}

export interface ModelState {
  id: string;
  name: string;
  url: string;
  note: string | null;
  downloaded: boolean;
  size: number | null;
  path: string;
}

export interface AiStatus {
  gpus: GpuInfo[];
  llama: LlamaStatus;
  models: ModelState[];
  modelsDir: string;
}


// Shapes of the data the host (Ember.Core, C#) sends. Property names are camelCase JSON.

export type Route = 'start' | 'proxy' | 'git' | 'software' | 'playwright' | 'power' | 'system' | 'ai' | 'access' | 'settings' | 'log';

/** What roles and keys unlock; the start page is there as soon as any module is. */
export type ModuleId = Exclude<Route, 'start'>;

export interface TeamStatus {
  path: string | null;
  source: 'settings' | 'policy' | 'file' | 'beside' | 'env' | null;
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
  /** Modules this account sees: from its roles in team.json, or from a starter key while there is none. */
  modules: ModuleId[];
  grant: { source: 'team' | 'key' | 'dev' | 'none'; roles: string[]; keyLevel: KeyLevel | null };
  /** setup: no team.json and no key; notListed: team.json has no role for this account. */
  locked: 'setup' | 'notListed' | null;
  /** Has the Berechtigungen tab. */
  admin: boolean;
  /** Has one of the advanced tabs (KI, Energie, Berechtigungen): Ctrl+E shows or hides them. */
  advanced: boolean;
  /** Advanced tabs visible (has one and Ctrl+E did not hide them). */
  showAdvanced: boolean;
  /** At least one module. */
  access: boolean;
  /** Where access requests go: access.contact, else the service center. */
  accessContact: string | null;
  /** Colour scheme and design as Ember.exe stored them. */
  appearance?: { scheme: 'light' | 'dark'; design: 'efeu' | 'glut' };
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
  /** Windows row only: what the PAC file says and the exceptions every tool gets. */
  pac?: { proxies: string[]; direct: string[]; exceptions: string[]; source: 'user' | 'team' | 'pac' | 'none' } | null;
}

export interface AccessEntry {
  name: string;
  kind: 'user' | 'group';
}

export type KeyLevel = 'proxy' | 'proxyPlaywright' | 'all';

export interface AccessRole {
  name: string;
  /** Module ids or "*" for all. */
  modules: string[];
  members: AccessEntry[];
}

export interface AccessView {
  roles: AccessRole[];
  contact: string | null;
  teamPath: string | null;
  writable: boolean;
  version: string | null;
  problem: 'noTeamFile' | 'teamOffline' | 'teamReadOnly' | null;
  account: string;
  myGroups: string[];
  /** Every module a role can contain. */
  modules: ModuleId[];
  /** team.json still has "admins" or the old access list; saving writes roles. */
  legacy: boolean;
}

export interface AccessLookup {
  name: string;
  kind: 'user' | 'group' | null;
  /** null: could not be checked (no connection to the directory) */
  found: boolean | null;
}

export interface GitProxyInfo {
  httpProxy: string | null;
  state: ProxyState;
  domains: string[];
  missingExceptions: string[];
  noProxyEnv: boolean;
  canSet: boolean;
  canRemove: boolean;
  /** http.sslBackend: "schannel" = Windows certificate store. */
  sslBackend: string | null;
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
  autostart: boolean;
  configureEnvVars: boolean;
  configurePowerShell: boolean;
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
  llamaProfile: LlamaProfile['id'];
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
  detected: { usePac: boolean | null; pacUrl: string | null; upstreamProxy: string | null; internalDomains: string | null };
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
  /** Hosts and domains the PAC file sends DIRECT (internal, without proxy). */
  pacDirect: string[];
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
  /** team: from team.json (category, note) · sccm: everything else Software Center offers */
  source: 'team' | 'sccm';
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

export interface PlaywrightProject {
  dir: string;
  hasPackageJson: boolean;
  package: string | null;
  version: string | null;
  wanted: string | null;
  hasLockFile: boolean;
  /** playwright.config.* in the folder */
  config: string | null;
  /** a config already runs the tests in the installed Edge (channel "msedge") */
  usesEdge: boolean;
}

export type PlaywrightMode = 'chromium' | 'all' | 'msedge';

export interface PlaywrightEdgeSetup {
  file: string;
  command: string;
  created: boolean;
}

export interface PlaywrightState {
  browsersDir: string;
  browsers: { name: string; path: string }[];
  downloadHost: string | null;
  nodeCerts: boolean;
  nodeFound: boolean;
  edgeFound: boolean;
  vsCodeFound: boolean;
  defaultParent: string;
  project: PlaywrightProject | null;
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
  serverAlias: string | null;
  serverProfile: string | null;
  serverContext: number | null;
  /** "62/62": layers on the GPU, from the server log. */
  offloaded: string | null;
  lastError: string | null;
  /** The server answers. */
  loaded: boolean;
  /** GPU llama.cpp found; null once loaded = CPU only. */
  device: string | null;
  promptTps: number | null;
  generationTps: number | null;
}

export interface ModelState {
  id: string;
  name: string;
  url: string;
  note: string | null;
  downloaded: boolean;
  size: number | null;
  path: string;
  /** A file that was already there: never downloaded or deleted by Ember. */
  local: boolean;
  /** Added on this PC (can be removed from the list). */
  mine: boolean;
  alias: string;
  mtp: boolean;
}

export interface LlamaProfile {
  id: 'standard' | 'lang' | 'max';
  context: number;
  kvCache: string;
  mtp: boolean;
}

export interface PiState {
  installed: boolean;
  nodeFound: boolean;
  configured: boolean;
  configFile: string;
  command: string;
}

export interface AiStatus {
  gpus: GpuInfo[];
  llama: LlamaStatus;
  models: ModelState[];
  modelsDir: string;
  profiles: LlamaProfile[];
  profile: LlamaProfile['id'];
  pi: PiState;
}


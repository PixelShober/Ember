// Global UI state: app info, px status, jobs, log, updates, toasts and dialogs.
import { flushSync } from 'svelte';
import { BridgeError, getBridge, type Bridge } from './bridge';
import { applyAppearance, normalize, storedAppearance } from './appearance';
import { host, setLanguage, t } from './i18n.svelte';
import type { AppInfo, JobSnapshot, LogEntry, ModuleId, PxStatus, Route, UpdateStatus } from './types';

export interface Toast {
  id: number;
  kind: 'ok' | 'error' | 'info';
  text: string;
  action?: { label: string; run: () => void };
}

export interface ConfirmRequest {
  title: string;
  text: string;
  confirm: string;
  danger?: boolean;
  resolve: (ok: boolean) => void;
}

export interface MailRequest {
  template: 'softwareRequest' | 'adminAction' | 'problem';
  vars: Record<string, string>;
}

const routes: Route[] = ['start', 'proxy', 'git', 'software', 'playwright', 'power', 'system', 'ai', 'access', 'settings', 'log'];
const MAX_LOG = 3000;

export const app = $state({
  ready: false,
  native: true,
  info: null as AppInfo | null,
  px: null as PxStatus | null,
  jobs: {} as Record<string, JobSnapshot>,
  logs: [] as LogEntry[],
  update: null as UpdateStatus | null,
  route: 'start' as Route,
  toasts: [] as Toast[],
  confirm: null as ConfirmRequest | null,
  mail: null as MailRequest | null,
  /** The pointer or the keyboard is on a toast: none of them goes away meanwhile. */
  toastsHeld: false,
  /** What a screen reader says next (App.svelte renders the live regions). */
  live: { polite: '', assertive: '' },
});

let bridge: Bridge | null = null;
let toastId = 0;

/** The account sees this module (its roles in the team file). */
export const can = (module: ModuleId): boolean => !!app.info?.modules?.includes(module);

/** A page may be shown: the start page always, the advanced tabs only while Ctrl+E has not hidden them. */
export function canOpen(route: Route): boolean {
  if (route === 'start') return !!app.info?.access;
  if (!can(route)) return false;
  return !advancedModules.includes(route) || !!app.info?.showAdvanced;
}

export const advancedModules: ModuleId[] = ['power', 'ai', 'access'];

// ------------------------------------------------------------------ page data cache
// What the tabs show (diagnosis, software list, checks …) changes rarely, but reading it can take
// seconds (PowerShell, WMI, npm). So the answers stay for a while: a tab opened again shows at once.
// Anything that changes something clears it, as does a finished job, a px start or stop, a new team
// file and the refresh buttons. Answers are copied, so a form bound to one never changes the cache.

const CACHE_MS = 10 * 60_000;

/** Reads whose answers may be kept. */
const cachedMethods = new Set([
  'proxy.diagnose', 'git.get', 'git.proxy', 'software.list', 'devtools.latest', 'system.list', 'system.check',
  'power.state', 'playwright.state', 'playwright.template', 'ai.llama.latest', 'ai.pi.packages', 'wol.local',
]);
/** Calls that change nothing, so they leave the cache alone. Everything else clears it. */
const pureMethods = new Set([
  'app.info', 'app.ready', 'app.pickFolder', 'app.pickFile', 'px.status', 'jobs.list', 'log.recent', 'log.save', 'update.status',
  'settings.get', 'ai.status', 'access.search', 'access.lookup', 'mail.preview', 'playwright.template.preview', 'playwright.inspect',
  'system.adminCommands', 'diagnostics.report', 'team.admin', 'mail.compose', 'app.openExternal', 'app.openFolder', 'app.setAppearance',
]);
const cache = new Map<string, { at: number; value: Promise<unknown> }>();

/** Forget kept answers: all, or those of methods starting with one of the prefixes. */
export function forget(...prefixes: string[]): void {
  if (!prefixes.length) cache.clear();
  else for (const key of [...cache.keys()]) if (prefixes.some((p) => key.startsWith(p))) cache.delete(key);
}

export async function api<T = unknown>(method: string, params: Record<string, unknown> = {}): Promise<T> {
  bridge ??= await getBridge();
  if (cachedMethods.has(method)) {
    const key = method + ' ' + JSON.stringify(params);
    let hit = cache.get(key);
    if (!hit || Date.now() - hit.at > CACHE_MS) {
      const value = bridge.call<T>(method, params);
      hit = { at: Date.now(), value };
      cache.set(key, hit);
      // A failed read is not kept: the next visit asks again.
      value.catch(() => cache.get(key)?.value === value && cache.delete(key));
    }
    return hit.value.then((v) => structuredClone(v) as T);
  }
  if (pureMethods.has(method)) return bridge.call<T>(method, params);
  // A change: what was kept may be outdated now, and again once the change is done.
  forget();
  return bridge.call<T>(method, params).finally(() => forget());
}

/** Fill the cache in the background, so the first visit of a tab is quick too. One at a time. */
async function warmUp(modules: ModuleId[]): Promise<void> {
  const reads: [ModuleId, string][] = [
    ['git', 'git.get'], ['proxy', 'proxy.diagnose'], ['git', 'git.proxy'], ['software', 'software.list'],
    ['playwright', 'playwright.state'], ['playwright', 'playwright.template'], ['power', 'power.state'], ['system', 'system.list'],
  ];
  for (const [module, method] of reads) {
    if (modules.includes(module)) await api(method).catch(() => {});
  }
}

function routeFromHash(): Route {
  const r = location.hash.replace(/^#\/?/, '') as Route;
  return routes.includes(r) ? r : 'start';
}

export function navigate(route: Route): void {
  changeRoute(route);
  if (location.hash !== '#/' + route) history.replaceState(null, '', '#/' + route);
}

/**
 * Page change as in CakeSender: the page fades out and the new one rises in, the sidebar stays and
 * the marker of the new tab grows in place (CSS in app.css and App.svelte).
 */
function changeRoute(next: Route): void {
  if (next === app.route) return;
  const doc = document as Document & { startViewTransition?: (update: () => void) => unknown };
  const root = document.documentElement;
  if (!app.ready || !doc.startViewTransition || root.classList.contains('theme-reveal') || matchMedia('(prefers-reduced-motion: reduce)').matches) {
    app.route = next;
    return;
  }
  // The DOM must be updated inside the callback, so the new page is in the "after" snapshot.
  doc.startViewTransition(() => flushSync(() => (app.route = next)));
}

export async function init(): Promise<void> {
  bridge = await getBridge();
  app.native = bridge.native;
  app.route = routeFromHash();
  window.addEventListener('hashchange', () => changeRoute(routeFromHash()));

  bridge.on('log', (entries: LogEntry[]) => {
    app.logs.push(...entries);
    if (app.logs.length > MAX_LOG) app.logs.splice(0, app.logs.length - MAX_LOG);
  });
  bridge.on('job', (job: JobSnapshot) => {
    // Finished: whatever it installed or changed is not in the kept answers yet.
    const before = app.jobs[job.id]?.state;
    if (before !== job.state && (job.state === 'done' || job.state === 'failed')) forget();
    // Started, finished, failed or cancelled: say so (the progress itself is not read out every second).
    if (before !== job.state) announce(`${host(job.title)}: ${t(`jobs.states.${job.state}`)}`, job.state === 'failed' ? 'assertive' : 'polite');
    app.jobs[job.id] = job;
  });
  bridge.on('px.status', (s: PxStatus) => {
    if (app.px && (s.portOpen !== app.px.portOpen || s.installed !== app.px.installed)) forget('proxy.', 'git.proxy', 'ai.pi.');
    app.px = s;
  });
  bridge.on('update', (s: UpdateStatus) => (app.update = s));
  // Inactive and see-through (like the other windows under GlazeWM/WinMarchy): Ember.exe made the
  // window transparent behind the page, the page now lets the desktop through.
  bridge.on('window.seeThrough', (w: { opacity: number | null }) => {
    const root = document.documentElement;
    root.classList.toggle('see-through', w.opacity != null);
    if (w.opacity != null) root.style.setProperty('--see-through', String(w.opacity));
  });
  bridge.on('app.info', (info: AppInfo) => {
    // Just got access (key, team.json changed): start over, so everything loads that the lock screen skipped.
    if (app.info && !app.info.access && info.access) location.reload();
    // New team file, other roles or a role preview: the pages may show something else now.
    forget();
    app.info = info;
    setLanguage(info.language);
  });

  const info = await bridge.call<AppInfo>('app.info');
  app.info = info;
  setLanguage(info.language);
  // The browser storage may be empty (new UI cache, cleared data): Ember.exe's copy wins then.
  if (!storedAppearance() && info.appearance) applyAppearance(normalize(info.appearance));
  // No module (setup screen or not in any role): the host refuses everything else anyway.
  if (!info.access) {
    app.ready = true;
    return;
  }
  const [px, jobs, logs, update] = await Promise.all([
    bridge.call<PxStatus>('px.status'),
    bridge.call<JobSnapshot[]>('jobs.list'),
    info.modules.includes('log') ? bridge.call<LogEntry[]>('log.recent') : Promise.resolve([] as LogEntry[]),
    bridge.call<UpdateStatus>('update.status'),
  ]);
  app.px = px;
  for (const j of jobs) app.jobs[j.id] = j;
  app.logs = logs;
  app.update = update;
  app.ready = true;

  // Tell the host we started; a downloaded UI that never says this is rolled back.
  const manifest = await fetch('./ui-manifest.json').then((r) => r.json()).catch(() => ({ version: '?' }));
  void bridge.call('app.ready', { version: manifest.version });
  void warmUp(info.modules);
}

/**
 * Tells screen readers about something that changed without moving the focus (WCAG 4.1.3).
 * "assertive" interrupts (errors), "polite" waits until the reader is done.
 */
export function announce(text: string, urgency: 'polite' | 'assertive' = 'polite'): void {
  // Empty first, so the same message twice in a row is read twice.
  app.live[urgency] = '';
  setTimeout(() => (app.live[urgency] = text), 60);
}

/**
 * A short message at the bottom right. Errors and messages with a button stay until closed
 * (WCAG 2.2.1: no time limit on something you need to read or act on); the others go after
 * a few seconds, but not while the pointer or the keyboard is on the toasts.
 */
export function toast(kind: Toast['kind'], text: string, action?: Toast['action']): void {
  const id = ++toastId;
  const shown = host(text);
  app.toasts.push({ id, kind, text: shown, action });
  announce(shown, kind === 'error' ? 'assertive' : 'polite');
  if (kind !== 'error' && !action) expire(id, 6000);
}

function expire(id: number, ms: number): void {
  setTimeout(() => (app.toastsHeld ? expire(id, 2000) : dismissToast(id)), ms);
}

export function dismissToast(id: number): void {
  const i = app.toasts.findIndex((x) => x.id === id);
  if (i >= 0) app.toasts.splice(i, 1);
}

export function confirm(title: string, text: string, confirmLabel = t('common.confirm'), danger = false): Promise<boolean> {
  return new Promise((resolve) => {
    app.confirm = { title, text, confirm: confirmLabel, danger, resolve };
  });
}

export function closeConfirm(ok: boolean): void {
  app.confirm?.resolve(ok);
  app.confirm = null;
}

export function openMail(template: MailRequest['template'], vars: Record<string, string> = {}): void {
  app.mail = { template, vars };
}

/**
 * Runs a host call and shows errors as toasts. If Windows' admin prompt was cancelled,
 * the toast offers the mail to the service center instead.
 */
export async function run<T>(fn: () => Promise<T>, okText?: string | ((result: T) => string), adminDetails?: string): Promise<T | undefined> {
  try {
    const result = await fn();
    if (okText) toast('ok', typeof okText === 'function' ? okText(result) : okText);
    return result;
  } catch (e) {
    const err = e instanceof BridgeError ? e : new BridgeError('internal', String(e));
    if (err.code === 'elevationCancelled' && adminDetails !== undefined) {
      toast('error', err.message, { label: t('common.askServiceCenter'), run: () => openMail('adminAction', { details: adminDetails }) });
    } else {
      toast('error', err.message);
    }
    return undefined;
  }
}

export async function copyText(text: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    // Fallback for contexts without clipboard permission
    const area = document.createElement('textarea');
    area.value = text;
    document.body.appendChild(area);
    area.select();
    document.execCommand('copy');
    area.remove();
  }
}

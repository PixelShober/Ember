// Global UI state: app info, px status, jobs, log, updates, toasts and dialogs.
import { BridgeError, getBridge, type Bridge } from './bridge';
import { host, setLanguage, t } from './i18n.svelte';
import type { AppInfo, JobSnapshot, LogEntry, PxStatus, Route, UpdateStatus } from './types';

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
  attachReport: boolean;
}

const routes: Route[] = ['start', 'proxy', 'git', 'software', 'power', 'system', 'ai', 'settings', 'log'];
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
});

let bridge: Bridge | null = null;
let toastId = 0;

export async function api<T = unknown>(method: string, params: Record<string, unknown> = {}): Promise<T> {
  bridge ??= await getBridge();
  return bridge.call<T>(method, params);
}

function routeFromHash(): Route {
  const r = location.hash.replace(/^#\/?/, '') as Route;
  return routes.includes(r) ? r : 'start';
}

export function navigate(route: Route): void {
  app.route = route;
  if (location.hash !== '#/' + route) history.replaceState(null, '', '#/' + route);
}

export async function init(): Promise<void> {
  bridge = await getBridge();
  app.native = bridge.native;
  app.route = routeFromHash();
  window.addEventListener('hashchange', () => (app.route = routeFromHash()));

  bridge.on('log', (entries: LogEntry[]) => {
    app.logs.push(...entries);
    if (app.logs.length > MAX_LOG) app.logs.splice(0, app.logs.length - MAX_LOG);
  });
  bridge.on('job', (job: JobSnapshot) => (app.jobs[job.id] = job));
  bridge.on('px.status', (s: PxStatus) => (app.px = s));
  bridge.on('update', (s: UpdateStatus) => (app.update = s));
  bridge.on('app.info', (info: AppInfo) => {
    app.info = info;
    setLanguage(info.language);
  });

  const info = await bridge.call<AppInfo>('app.info');
  app.info = info;
  setLanguage(info.language);
  const [px, jobs, logs, update] = await Promise.all([
    bridge.call<PxStatus>('px.status'),
    bridge.call<JobSnapshot[]>('jobs.list'),
    bridge.call<LogEntry[]>('log.recent'),
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
}

export function toast(kind: Toast['kind'], text: string, action?: Toast['action']): void {
  const id = ++toastId;
  app.toasts.push({ id, kind, text: host(text), action });
  setTimeout(() => dismissToast(id), kind === 'error' ? 12000 : 5000);
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

export function openMail(template: MailRequest['template'], vars: Record<string, string> = {}, attachReport = true): void {
  app.mail = { template, vars, attachReport };
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

import { locale, t, tOr } from './i18n.svelte';

export function formatDate(value: string | Date | null | undefined, withTime = true): string {
  if (!value) return '–';
  const d = typeof value === 'string' ? new Date(value) : value;
  if (Number.isNaN(d.getTime())) return '–';
  return new Intl.DateTimeFormat(locale(), withTime ? { dateStyle: 'medium', timeStyle: 'short' } : { dateStyle: 'medium' }).format(d);
}

export function formatTime(value: string): string {
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return value;
  return new Intl.DateTimeFormat(locale(), { hour: '2-digit', minute: '2-digit', second: '2-digit' }).format(d);
}

export function formatBytes(bytes: number | null | undefined): string {
  if (bytes == null) return '–';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let v = bytes;
  let i = 0;
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024;
    i++;
  }
  return `${new Intl.NumberFormat(locale(), { maximumFractionDigits: i >= 3 ? 1 : 0 }).format(v)} ${units[i]}`;
}

const timeSettings = new Set(['STANDBYIDLE', 'HIBERNATEIDLE', 'VIDEOIDLE']);

/**
 * Human-readable power setting value: times in minutes ("nie" for 0), percent,
 * or the option name (Ember's own translation first, then the Windows name).
 */
export function formatPowerValue(alias: string, value: number | null, unit: string | null, options: Record<string, string>): string {
  if (value == null) return '–';
  const base = alias.replace(/\d+$/, '');
  if (timeSettings.has(base)) {
    if (value === 0) return t('power.never');
    return value % 60 === 0 ? t('power.minutes', { value: value / 60 }) : t('power.seconds', { value });
  }
  const option = options[String(value)];
  const own = tOr(`power.options.${base}.${value}`, option ?? '');
  if (own) return own;
  if (unit && unit.trim() === '%') return `${new Intl.NumberFormat(locale()).format(value)} %`;
  return unit ? `${value} ${unit}` : String(value);
}

/** First name for the greeting: "Max Mustermann" → "Max", "DOMAIN\\mmustermann" → "mmustermann". */
export function firstName(name: string | null | undefined, account: string): string {
  const n = (name ?? '').trim();
  if (n) return n.split(/\s+/)[0];
  const user = account.includes('\\') ? account.split('\\')[1] : account;
  return user || account;
}

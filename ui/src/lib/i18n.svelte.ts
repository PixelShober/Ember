// Translations for the UI. The same JSON files are embedded in the host (Ember.Core),
// so every text exists exactly once per language.
import de from '../locales/de.json';
import en from '../locales/en.json';

export type Language = 'de' | 'en';
type Table = Record<string, string>;

export function flatten(obj: Record<string, unknown>, prefix = '', into: Table = {}): Table {
  for (const [key, value] of Object.entries(obj)) {
    const full = prefix ? `${prefix}.${key}` : key;
    if (value && typeof value === 'object') flatten(value as Record<string, unknown>, full, into);
    else if (typeof value === 'string') into[full] = value;
  }
  return into;
}

const tables: Record<Language, Table> = { de: flatten(de), en: flatten(en) };

export const i18n = $state({ lang: 'de' as Language, swiss: false });

/** "de", "de-CH" or "en" (as the host reports it). Anything else falls back to the browser language. */
export function setLanguage(tag: string | null | undefined): void {
  const t = (tag || (typeof navigator !== 'undefined' ? navigator.language : 'de')).toLowerCase();
  i18n.lang = t.startsWith('de') ? 'de' : 'en';
  i18n.swiss = t === 'de-ch' || t === 'de-li';
  if (typeof document !== 'undefined') document.documentElement.lang = locale();
}

/** BCP 47 locale for Intl formatting. */
export function locale(): string {
  return i18n.swiss ? 'de-CH' : i18n.lang === 'de' ? 'de-DE' : 'en-US';
}

/** Swiss Standard German does not use "ß". */
export function toSwiss(text: string): string {
  return text.replace(/ß/g, 'ss').replace(/ẞ/g, 'SS');
}

function formatValue(value: unknown): string {
  if (value == null) return '';
  if (typeof value === 'number') return new Intl.NumberFormat(locale()).format(value);
  if (value instanceof Date) return new Intl.DateTimeFormat(locale(), { dateStyle: 'medium', timeStyle: 'short' }).format(value);
  return String(value);
}

export function has(key: string): boolean {
  return key in tables[i18n.lang] || key in tables.en;
}

/** Translates a key; {name} placeholders are replaced with the formatted parameter values. */
export function t(key: string, params: Record<string, unknown> = {}): string {
  let text = tables[i18n.lang][key] ?? tables.en[key] ?? key;
  for (const [name, value] of Object.entries(params)) text = text.split(`{${name}}`).join(formatValue(value));
  return i18n.swiss ? toSwiss(text) : text;
}

/** Like t(), but returns the fallback when the key does not exist. */
export function tOr(key: string, fallback: string, params: Record<string, unknown> = {}): string {
  return has(key) ? t(key, params) : i18n.swiss ? toSwiss(fallback) : fallback;
}

/** Host strings are already translated, but still need the Swiss spelling. */
export function host(text: string | null | undefined): string {
  if (!text) return '';
  return i18n.swiss ? toSwiss(text) : text;
}

export const _tables = tables;

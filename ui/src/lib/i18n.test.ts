import { describe, expect, it, beforeEach } from 'vitest';
import { _tables, i18n, locale, setLanguage, t, toSwiss } from './i18n.svelte';

const placeholders = (s: string) => [...s.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();

describe('locale files', () => {
  it('German and English have exactly the same keys', () => {
    const de = Object.keys(_tables.de).sort();
    const en = Object.keys(_tables.en).sort();
    expect(de.filter((k) => !en.includes(k))).toEqual([]);
    expect(en.filter((k) => !de.includes(k))).toEqual([]);
  });

  it('use the same placeholders in both languages', () => {
    const mismatches = Object.keys(_tables.de).filter((k) => placeholders(_tables.de[k]).join() !== placeholders(_tables.en[k] ?? '').join());
    expect(mismatches).toEqual([]);
  });

  it('German texts use real umlauts, not transliterations', () => {
    const words = /\b(fuer|ueber|Ueber|koennen|muessen|waehlen|loeschen|Loeschen|pruefen|Pruefen|zurueck|Zurueck|oeffnen|Oeffnen|schliessen|Schliessen|Groesse|ausfuehren|geloescht|geprueft|Schluessel|aendern|Aenderung)\b/;
    const bad = Object.entries(_tables.de).filter(([, v]) => words.test(v)).map(([k]) => k);
    expect(bad).toEqual([]);
  });

  it('contains no mojibake', () => {
    // UTF-8 read as Windows-1252 (e.g. "ä" shown as A-tilde + currency sign), written as escapes so this file stays clean
    const broken = /Ã[¤¶¼„–œŸš]|â€[žœ“”™]|Â[ ­]/;
    const bad = [...Object.entries(_tables.de), ...Object.entries(_tables.en)].filter(([, v]) => broken.test(v)).map(([k]) => k);
    expect(bad).toEqual([]);
  });
});

describe('t()', () => {
  beforeEach(() => setLanguage('de'));

  it('translates with umlauts and replaces placeholders', () => {
    expect(t('proxy.px.running', { url: 'http://127.0.0.1:3128' })).toBe('px läuft · http://127.0.0.1:3128');
    expect(t('common.close')).toBe('Schließen');
  });

  it('formats numbers for the language', () => {
    expect(t('ai.gpu.memory', { used: 1830, total: 24463 })).toBe('1.830 von 24.463 MB belegt');
    setLanguage('en');
    expect(t('ai.gpu.memory', { used: 1830, total: 24463 })).toBe('1,830 of 24,463 MB in use');
  });

  it('writes ss instead of ß for Swiss German', () => {
    setLanguage('de-CH');
    expect(i18n.swiss).toBe(true);
    expect(locale()).toBe('de-CH');
    expect(t('common.close')).toBe('Schliessen');
    expect(toSwiss('Größe, Straße, GROẞ')).toBe('Grösse, Strasse, GROSS');
  });

  it('falls back to English, then to the key', () => {
    setLanguage('fr-FR');
    expect(i18n.lang).toBe('en');
    expect(t('does.not.exist')).toBe('does.not.exist');
  });
});

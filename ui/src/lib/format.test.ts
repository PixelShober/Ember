import { beforeEach, describe, expect, it } from 'vitest';
import { setLanguage } from './i18n.svelte';
import { firstName, formatBytes, formatPowerValue } from './format';

describe('formatPowerValue', () => {
  beforeEach(() => setLanguage('de'));

  it('shows times in minutes and 0 as "nie"', () => {
    expect(formatPowerValue('STANDBYIDLE', 0, 'Sekunden', {})).toBe('nie');
    expect(formatPowerValue('VIDEOIDLE', 900, 'Sekunden', {})).toBe('15 min');
    expect(formatPowerValue('VIDEOIDLE', 45, 'Sekunden', {})).toBe('45 s');
  });

  it("prefers Ember's option names, then Windows' names, then percent", () => {
    expect(formatPowerValue('PERFBOOSTMODE', 2, null, { '2': 'Hoch' })).toBe('Hoch (aggressiv)');
    expect(formatPowerValue('CUSTOM', 1, null, { '1': 'Aktiviert' })).toBe('Aktiviert');
    expect(formatPowerValue('PERFEPP1', 25, '%', {})).toBe('25 %');
    expect(formatPowerValue('PERFEPP', null, '%', {})).toBe('–');
  });

  it('uses the same names for efficiency class variants', () => {
    expect(formatPowerValue('LIDACTION', 0, null, {})).toBe('Nichts unternehmen');
  });
});

describe('helpers', () => {
  it('formats bytes with German separators', () => {
    setLanguage('de');
    expect(formatBytes(18_556_687_000)).toBe('17,3 GB');
  });

  it('takes the first name for the greeting', () => {
    expect(firstName('Jürgen Mößner', 'FIRMA\\jmoessner')).toBe('Jürgen');
    expect(firstName(null, 'FIRMA\\jmoessner')).toBe('jmoessner');
  });
});

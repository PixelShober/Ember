import { beforeEach, describe, expect, it } from 'vitest';
import { setLanguage } from './i18n.svelte';
import { compareVersions, firstName, formatBytes, formatPowerValue, isNewer } from './format';

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

describe('versions', () => {
  it('compares numerically, not as text', () => {
    expect(compareVersions('2.47.10', '2.47.9')).toBe(1);
    expect(compareVersions('v26.12.1', '26.12.1')).toBe(0);
    expect(compareVersions('3.13', '3.13.1')).toBe(-1);
  });
  it('only reports real updates', () => {
    expect(isNewer('3.14.2', '3.13.1')).toBe(true);
    expect(isNewer('3.13.1', '3.13.1')).toBe(false);
    expect(isNewer('aktuelle Version', '17.14.12')).toBe(false);
    expect(isNewer('1.2.3', null)).toBe(false);
  });
});

import { describe, expect, it } from 'vitest';
import { _tables } from './i18n.svelte';
import { isTourDone, markTourDone, placeCard, TOUR_KEY, tourSteps, visibleSteps } from './tour';
import type { Route } from './types';

describe('tour steps', () => {
  it('have a title and a text in German and English', () => {
    const missing = tourSteps.flatMap((s) => [s.title, s.text]).filter((k) => !_tables.de[k] || !_tables.en[k]);
    expect(missing).toEqual([]);
  });

  it('have unique ids and follow the sidebar', () => {
    expect(new Set(tourSteps.map((s) => s.id)).size).toBe(tourSteps.length);
    const order = tourSteps.slice(0, -1).map((s) => s.route).filter((r, i, all) => r !== all[i - 1]);
    expect(order).toEqual(['start', 'proxy', 'git', 'software', 'playwright']);
  });

  it('leave out tabs the account may not open', () => {
    const allowed: Route[] = ['start', 'git'];
    const steps = visibleSteps((r) => allowed.includes(r));
    expect(steps.length).toBeGreaterThan(0);
    expect(steps.every((s) => allowed.includes(s.route))).toBe(true);
    expect(steps[0].id).toBe('welcome');
    expect(steps[steps.length - 1].id).toBe('done');
    expect(visibleSteps(() => true)).toHaveLength(tourSteps.length);
  });
});

describe('remembering the tour', () => {
  const memory = () => {
    const data: Record<string, string> = {};
    return { getItem: (k: string) => data[k] ?? null, setItem: (k: string, v: string) => void (data[k] = v), data };
  };

  it('is not done until marked', () => {
    const store = memory();
    expect(isTourDone(store)).toBe(false);
    markTourDone(store);
    expect(store.data[TOUR_KEY]).toBe('1');
    expect(isTourDone(store)).toBe(true);
  });

  it('never starts by itself when the storage is blocked', () => {
    const blocked = {
      getItem: () => { throw new Error('blocked'); },
      setItem: () => { throw new Error('blocked'); },
    };
    expect(isTourDone(blocked)).toBe(true);
    expect(() => markTourDone(blocked)).not.toThrow();
    expect(isTourDone(null)).toBe(true);
  });
});

describe('placeCard', () => {
  const view = { width: 1280, height: 800 };
  const card = { width: 380, height: 260 };

  it('goes to the right of the sidebar', () => {
    expect(placeCard({ left: 12, top: 70, width: 208, height: 300 }, card, view)).toEqual({ left: 236, top: 70 });
  });

  it('goes below a wide target and above one at the bottom', () => {
    expect(placeCard({ left: 268, top: 100, width: 980, height: 150 }, card, view)).toEqual({ left: 268, top: 266 });
    expect(placeCard({ left: 268, top: 600, width: 980, height: 150 }, card, view)).toEqual({ left: 268, top: 324 });
  });

  it('stays inside the window', () => {
    const at = placeCard({ left: 1100, top: 100, width: 160, height: 60 }, card, view)!;
    expect(at.left + card.width).toBeLessThanOrEqual(view.width - 12);
  });

  it('sits in the bottom left corner of a target that fills the window', () => {
    expect(placeCard({ left: 268, top: 20, width: 980, height: 760 }, card, view)).toEqual({ left: 284, top: 504 });
  });

  it('gives up when the window is too small', () => {
    expect(placeCard({ left: 0, top: 0, width: 100, height: 40 }, card, { width: 360, height: 640 })).toBeNull();
  });
});

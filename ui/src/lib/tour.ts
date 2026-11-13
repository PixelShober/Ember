// The guided tour through the developer tabs: which steps there are, which of them this account
// sees, and whether the tour has run before. No DOM and no Svelte here, so it can be tested as is;
// the overlay itself is components/Tour.svelte.
import type { Route } from './types';

export interface TourStep {
  id: string;
  /** The tab the step is shown on. */
  route: Route;
  /** CSS selector of what the step points at. Without one the card sits in the middle. */
  target?: string;
  /** A sub-tab that has to be selected first (clicked while its aria-selected is "false"). */
  select?: string;
  /** Icon in the card head (a name from Icon.svelte); the first and last step show Calcifer instead. */
  icon?: string;
  /** Keys in the locale files. */
  title: string;
  text: string;
}

const step = (id: string, route: Route, rest: Omit<TourStep, 'id' | 'route' | 'title' | 'text'> = {}): TourStep => ({
  id,
  route,
  title: `tour.steps.${id}.title`,
  text: `tour.steps.${id}.text`,
  ...rest,
});

/** In the order of the sidebar: Start, Proxy, Git, Software, Playwright. */
export const tourSteps: TourStep[] = [
  step('welcome', 'start'),
  step('nav', 'start', { target: '[data-tour="nav"]', icon: 'home' }),
  step('status', 'start', { target: '[data-tour="start-status"]', icon: 'home' }),
  step('px', 'proxy', { target: '[data-tour="proxy-px"]', icon: 'proxy' }),
  step('diagnose', 'proxy', { target: '[data-tour="proxy-diag"]', icon: 'proxy' }),
  step('identity', 'git', { target: '[data-tour="git-identity"]', icon: 'git' }),
  step('gitProxy', 'git', { target: '[data-tour="git-proxy"]', icon: 'git' }),
  step('devtools', 'software', { target: '[data-tour="software-devtools"]', icon: 'software' }),
  step('pwTabs', 'playwright', { target: '[role="tablist"]:has(#pw-tab-project)', icon: 'browser' }),
  step('pwProject', 'playwright', { target: '[data-tour="pw-project"]', select: '#pw-tab-project', icon: 'browser' }),
  step('done', 'start', { target: '[data-tour="start-tour"]' }),
];

/** Only the steps on tabs this account may open. */
export function visibleSteps(canOpen: (route: Route) => boolean, steps: TourStep[] = tourSteps): TourStep[] {
  return steps.filter((s) => canOpen(s.route));
}

export const TOUR_KEY = 'ember-tutorial-done';

type Store = Pick<Storage, 'getItem' | 'setItem'>;
const browserStore = (): Store | null => {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage;
  } catch {
    return null;
  }
};

/** The tour has been finished or skipped on this computer. A blocked storage counts as "yes": never nag. */
export function isTourDone(store: Store | null = browserStore()): boolean {
  try {
    return !store || store.getItem(TOUR_KEY) === '1';
  } catch {
    return true;
  }
}

export function markTourDone(store: Store | null = browserStore()): void {
  try {
    store?.setItem(TOUR_KEY, '1');
  } catch {
    /* Storage blocked: the tour simply does not start by itself. */
  }
}

export interface Box { left: number; top: number; width: number; height: number }
export interface Size { width: number; height: number }

/**
 * Where the card goes: next to what it points at, on the first side with enough room
 * (right, below, above, left), kept inside the window. null: no room at all, dock it at the bottom.
 */
export function placeCard(target: Box, card: Size, view: Size, gap = 16, margin = 12): { left: number; top: number } | null {
  const clamp = (v: number, min: number, max: number) => Math.max(min, Math.min(v, max));
  const maxLeft = view.width - card.width - margin;
  const maxTop = view.height - card.height - margin;
  if (maxLeft < margin || maxTop < margin) return null;
  const right = target.left + target.width;
  const bottom = target.top + target.height;
  const alongY = clamp(target.top, margin, maxTop);
  const alongX = clamp(target.left, margin, maxLeft);
  if (right + gap + card.width + margin <= view.width) return { left: right + gap, top: alongY };
  if (bottom + gap + card.height + margin <= view.height) return { left: alongX, top: bottom + gap };
  if (target.top - gap - card.height >= margin) return { left: alongX, top: target.top - gap - card.height };
  if (target.left - gap - card.width >= margin) return { left: target.left - gap - card.width, top: alongY };
  // A target that fills the window (a long table): into its bottom left corner, where the card hides
  // the least, since the buttons of a row are on the right.
  if (target.width >= card.width * 2) return { left: clamp(target.left + gap, margin, maxLeft), top: clamp(bottom - gap - card.height, margin, maxTop) };
  return null;
}

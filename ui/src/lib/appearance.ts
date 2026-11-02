// Appearance = colour scheme (Light | Dark) + design (Efeu | Glut). Without a stored choice the
// Windows setting decides once; from then on the choice stays. public/appearance-boot.js applies the
// stored value before the first paint; Ember.exe keeps a copy in the settings (title bar, window
// background, and after a cache wipe).
export type Scheme = 'light' | 'dark';
export type Design = 'efeu' | 'glut';
export interface Appearance { scheme: Scheme; design: Design }

const KEY = 'ember-appearance';
const windowsScheme = (): Scheme => (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');

/** Anything else (also "system" from older versions) becomes what Windows uses right now. */
export function normalize(value: { scheme?: string; design?: string } | null | undefined): Appearance {
  return {
    scheme: value?.scheme === 'light' || value?.scheme === 'dark' ? value.scheme : windowsScheme(),
    design: value?.design === 'glut' ? 'glut' : 'efeu',
  };
}

export function current(): Appearance {
  const root = document.documentElement;
  return {
    scheme: root.classList.contains('dark') ? 'dark' : root.classList.contains('light') ? 'light' : windowsScheme(),
    design: root.dataset.design === 'glut' ? 'glut' : 'efeu',
  };
}

export function storedAppearance(): Appearance | null {
  try {
    const raw = localStorage.getItem(KEY);
    return raw ? normalize(JSON.parse(raw)) : null;
  } catch {
    return null;
  }
}


/** Sets classes and data attribute, remembers the choice and tells every mounted switch. */
export function applyAppearance(next: Appearance, notify?: (a: Appearance) => void) {
  const root = document.documentElement;
  root.classList.toggle('light', next.scheme === 'light');
  root.classList.toggle('dark', next.scheme === 'dark');
  root.dataset.design = next.design;
  try {
    localStorage.setItem(KEY, JSON.stringify(next));
  } catch {
    /* Storage blocked: the appearance still applies for this session. */
  }
  window.dispatchEvent(new CustomEvent('ember:appearance', { detail: next }));
  notify?.(next);
}

let reveals = 0;

/**
 * The new look grows as a circle out of the control that was pressed (the same reveal as in
 * CakeSender, also for keyboard use). Without a visible change or with reduced motion it switches directly.
 */
export function applyWithReveal(next: Appearance, origin: Element, notify?: (a: Appearance) => void) {
  const root = document.documentElement;
  const before = current();
  const doc = document as Document & { startViewTransition?: (update: () => void) => { finished: Promise<void> } };
  const visible = before.scheme !== next.scheme || before.design !== next.design;
  if (!doc.startViewTransition || !visible || matchMedia('(prefers-reduced-motion: reduce)').matches) {
    applyAppearance(next, notify);
    return;
  }
  // Anchor on the icon the eye followed, not on the invisible radio that may stretch wider.
  const box = (origin.closest('label')?.querySelector('svg') ?? origin).getBoundingClientRect();
  const x = box.left + box.width / 2;
  const y = box.top + box.height / 2;
  // Percentages resolve against the snapshot's own box, so a device-pixel-ratio mismatch cannot shift the origin.
  const { innerWidth: w, innerHeight: h } = window;
  const reach = Math.hypot(Math.max(x, w - x), Math.max(y, h - y));
  root.style.setProperty('--reveal-x', `${(x / w) * 100}%`);
  root.style.setProperty('--reveal-y', `${(y / h) * 100}%`);
  root.style.setProperty('--reveal-r', `${((reach / Math.hypot(w, h)) * Math.SQRT2) * 100}%`); // circle(%) is relative to hypot(w,h)/√2
  root.classList.add('theme-reveal');
  // A quick second switch skips the running transition; only the latest one may clear the class.
  const reveal = ++reveals;
  doc.startViewTransition(() => applyAppearance(next, notify)).finished.finally(() => {
    if (reveal === reveals) root.classList.remove('theme-reveal');
  });
}

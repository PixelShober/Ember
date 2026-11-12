// Shared behaviour of every dialog (WAI-ARIA dialog pattern):
// - on open, focus goes into the dialog ([autofocus], else the first field or button, else the dialog itself);
// - Tab and Shift+Tab stay inside, and focus that slips out (a click on the backdrop, a frame) is brought back;
// - Esc closes the topmost dialog only;
// - on close, focus returns to whatever opened it.
// Usage: <div role="dialog" aria-modal="true" use:modal={close}>.

const focusable = [
  'a[href]', 'button:not([disabled])', 'input:not([disabled]):not([type="hidden"])', 'select:not([disabled])',
  'textarea:not([disabled])', 'iframe', 'summary', '[tabindex]:not([tabindex="-1"])',
].join(',');

/** Open dialogs, topmost last. */
const stack: HTMLElement[] = [];

function items(node: HTMLElement): HTMLElement[] {
  return [...node.querySelectorAll<HTMLElement>(focusable)].filter((e) => !e.closest('[inert]') && e.getClientRects().length > 0);
}

export function modal(node: HTMLElement, onclose: () => void) {
  let close = onclose;
  const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  stack.push(node);
  if (!node.hasAttribute('tabindex')) node.tabIndex = -1;

  // After Svelte has rendered the content (and run its own autofocus).
  queueMicrotask(() => {
    if (node.contains(document.activeElement)) return;
    (node.querySelector<HTMLElement>('[autofocus]') ?? items(node)[0] ?? node).focus();
  });

  const top = () => stack[stack.length - 1] === node;

  function onKey(e: KeyboardEvent) {
    if (!top()) return;
    if (e.key === 'Escape') {
      e.preventDefault();
      e.stopPropagation();
      close();
      return;
    }
    if (e.key !== 'Tab') return;
    const list = items(node);
    if (!list.length) {
      e.preventDefault();
      node.focus();
      return;
    }
    const first = list[0];
    const last = list[list.length - 1];
    const active = document.activeElement;
    if (e.shiftKey && (active === first || active === node)) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && active === last) {
      e.preventDefault();
      first.focus();
    }
  }

  function onFocusIn(e: FocusEvent) {
    if (!top() || node.contains(e.target as Node)) return;
    (items(node)[0] ?? node).focus();
  }

  // Capture phase: Esc reaches the dialog before page shortcuts.
  document.addEventListener('keydown', onKey, true);
  document.addEventListener('focusin', onFocusIn);

  return {
    update(next: () => void) {
      close = next;
    },
    destroy() {
      document.removeEventListener('keydown', onKey, true);
      document.removeEventListener('focusin', onFocusIn);
      const i = stack.indexOf(node);
      if (i >= 0) stack.splice(i, 1);
      // Back to the opener, unless the user has already moved on (or the opener is gone).
      const active = document.activeElement;
      if (opener?.isConnected && (!active || active === document.body || node.contains(active))) opener.focus();
    },
  };
}

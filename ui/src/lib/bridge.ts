// Talks to the host (Ember.exe) through WebView2's postMessage channel.
// Request:  { id, method, params }      Response: { id, result } | { id, error }
// Event:    { event, data }

export class BridgeError extends Error {
  constructor(
    public code: string,
    message: string,
    public params: Record<string, unknown> = {},
  ) {
    super(message);
    this.name = 'BridgeError';
  }
}

export interface Bridge {
  /** true when running inside Ember.exe, false for the browser preview with sample data. */
  readonly native: boolean;
  call<T = unknown>(method: string, params?: Record<string, unknown>): Promise<T>;
  on(event: string, handler: (data: any) => void): () => void;
}

interface WebView {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (e: { data: any }) => void): void;
}

declare global {
  interface Window {
    chrome?: { webview?: WebView };
  }
}

type Pending = { resolve: (v: any) => void; reject: (e: unknown) => void; method: string };

export class HostBridge implements Bridge {
  readonly native = true;
  #id = 0;
  #pending = new Map<number, Pending>();
  #listeners = new Map<string, Set<(data: any) => void>>();

  constructor(private readonly webview: WebView) {
    webview.addEventListener('message', (e) => this.receive(e.data));
  }

  call<T = unknown>(method: string, params: Record<string, unknown> = {}): Promise<T> {
    const id = ++this.#id;
    return new Promise<T>((resolve, reject) => {
      this.#pending.set(id, { resolve, reject, method });
      this.webview.postMessage({ id, method, params });
    });
  }

  on(event: string, handler: (data: any) => void): () => void {
    let set = this.#listeners.get(event);
    if (!set) this.#listeners.set(event, (set = new Set()));
    set.add(handler);
    return () => set!.delete(handler);
  }

  /** Exposed for tests. */
  receive(message: any): void {
    if (message == null || typeof message !== 'object') return;
    if (typeof message.event === 'string') {
      this.#listeners.get(message.event)?.forEach((h) => h(message.data));
      return;
    }
    const pending = this.#pending.get(message.id);
    if (!pending) return;
    this.#pending.delete(message.id);
    if (message.error) {
      const e = message.error;
      pending.reject(new BridgeError(e.code ?? 'internal', e.message ?? pending.method, e.params ?? {}));
    } else {
      pending.resolve(message.result);
    }
  }
}

let instance: Promise<Bridge> | null = null;

/** The bridge to Ember.exe, or a mock with sample data when the page runs in a normal browser. */
export function getBridge(): Promise<Bridge> {
  if (!instance) {
    const webview = typeof window !== 'undefined' ? window.chrome?.webview : undefined;
    instance = webview
      ? Promise.resolve(new HostBridge(webview))
      : import('./mock').then((m) => m.createMockBridge());
  }
  return instance;
}

import { describe, expect, it, vi } from 'vitest';
import { BridgeError, HostBridge } from './bridge';

function fakeWebView() {
  const sent: any[] = [];
  let listener: ((e: { data: any }) => void) | null = null;
  return {
    sent,
    deliver: (data: any) => listener?.({ data }),
    webview: {
      postMessage: (m: unknown) => sent.push(m),
      addEventListener: (_: 'message', l: (e: { data: any }) => void) => (listener = l),
    },
  };
}

describe('HostBridge', () => {
  it('sends requests with id, method and params and resolves the answer', async () => {
    const fake = fakeWebView();
    const bridge = new HostBridge(fake.webview);
    const p = bridge.call<string>('git.get', { a: 1 });
    expect(fake.sent[0]).toEqual({ id: 1, method: 'git.get', params: { a: 1 } });
    fake.deliver({ id: 1, result: 'Jürgen Mößner' });
    await expect(p).resolves.toBe('Jürgen Mößner');
  });

  it('turns error responses into BridgeError with code and params', async () => {
    const fake = fakeWebView();
    const bridge = new HostBridge(fake.webview);
    const p = bridge.call('wol.apply');
    fake.deliver({ id: 1, error: { code: 'elevationCancelled', message: 'abgebrochen', params: { x: 1 } } });
    const err = (await p.catch((e: unknown) => e)) as BridgeError;
    expect(err).toBeInstanceOf(BridgeError);
    expect(err.code).toBe('elevationCancelled');
    expect(err.params).toEqual({ x: 1 });
  });

  it('dispatches events to subscribers and allows unsubscribing', () => {
    const fake = fakeWebView();
    const bridge = new HostBridge(fake.webview);
    const handler = vi.fn();
    const off = bridge.on('px.status', handler);
    fake.deliver({ event: 'px.status', data: { running: true } });
    off();
    fake.deliver({ event: 'px.status', data: { running: false } });
    expect(handler).toHaveBeenCalledTimes(1);
    expect(handler).toHaveBeenCalledWith({ running: true });
  });

  it('ignores unknown ids and garbage', () => {
    const fake = fakeWebView();
    const bridge = new HostBridge(fake.webview);
    expect(() => {
      fake.deliver({ id: 99, result: 1 });
      fake.deliver(null);
      fake.deliver('text');
    }).not.toThrow();
    expect(bridge.native).toBe(true);
  });
});

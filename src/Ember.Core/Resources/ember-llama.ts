// Written by Ember (KI → Pi → "Mit llama-server verbinden"). Ember replaces this file when it updates, so change the
// settings in Ember instead: model, profile and llama.cpp build come from llama.json, which Ember keeps current.
//
// As soon as Pi uses the local model (provider "%PROVIDER%"), this extension starts llama-server in the
// background, unless it already runs. The server keeps running after Pi closes, so the next start is quick.
// "/llama stop" ends it and frees the graphics memory, "/llama" shows the state.
import type { ExtensionAPI } from "@earendil-works/pi-coding-agent";
import { spawn, execFile } from "node:child_process";
import * as fs from "node:fs";
import * as http from "node:http";
import * as path from "node:path";

const CONFIG = String.raw`%CONFIG%`;
const PROVIDER = "%PROVIDER%";
const TEXT: Record<string, string> = %MESSAGES%;
const STATUS = "ember-llama";
const LOAD_TIMEOUT_MS = 5 * 60_000;

type Launch = {
  ready: boolean;
  reason?: string | null;
  exe: string;
  cwd: string;
  args: string[];
  port: number;
  model?: string | null;
  log: string;
};

type Ui = {
  hasUI?: boolean;
  ui: {
    notify(message: string, type?: "info" | "warning" | "error"): void;
    setStatus(key: string, text: string | undefined): void;
    setWorkingMessage(message?: string): void;
  };
};

const say = (key: string, values: Record<string, string | number> = {}) =>
  (TEXT[key] ?? key).replace(/\{(\w+)\}/g, (_, name) => String(values[name] ?? ""));

function readLaunch(): Launch {
  let text: string;
  try {
    text = fs.readFileSync(CONFIG, "utf8");
  } catch {
    throw new Error(say("noConfig", { path: CONFIG }));
  }
  const launch = JSON.parse(text) as Launch;
  if (!launch.ready) throw new Error(launch.reason || say("notReady"));
  return launch;
}

/** 200: ready, 503: loading the model, 0: nothing answers. Plain http, so no proxy gets in the way. */
function health(port: number): Promise<number> {
  return new Promise((resolve) => {
    const req = http.get({ host: "127.0.0.1", port, path: "/health", timeout: 1500 }, (res) => {
      res.resume();
      resolve(res.statusCode ?? 0);
    });
    req.on("timeout", () => req.destroy());
    req.on("error", () => resolve(0));
  });
}

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

function lastError(log: string): string {
  try {
    const lines = fs.readFileSync(log, "utf8").split(/\r?\n/).filter((l) => l.trim());
    return [...lines].reverse().find((l) => /\sE\s|error|failed|out of memory/i.test(l)) ?? lines.at(-1) ?? "";
  } catch {
    return "";
  }
}

const reported = new WeakSet<object>();

/** In Pi's window as a message, without one (pi -p) on stderr; a failed start shared by two waiters only once. */
function report(ctx: Ui, error: unknown) {
  if (typeof error === "object" && error !== null) {
    if (reported.has(error)) return;
    reported.add(error);
  }
  const message = error instanceof Error ? error.message : String(error);
  if (ctx.hasUI !== false) ctx.ui.notify(message, "error");
  else process.stderr.write(`[ember-llama] ${message}\n`);
}

function status(ctx: Ui, text: string | undefined) {
  if (ctx.hasUI !== false) ctx.ui.setStatus(STATUS, text);
}

let starting: Promise<void> | null = null;

/** Starts llama-server if needed and waits until it answers; calls at the same time share one start. */
function ensure(ctx: Ui): Promise<void> {
  starting ??= start(ctx).finally(() => (starting = null));
  return starting;
}

async function start(ctx: Ui): Promise<void> {
  const launch = readLaunch();
  let state = await health(launch.port);
  if (state === 200) {
    status(ctx, say("ready", { port: launch.port }));
    return;
  }
  const proc = { exited: null as number | null };
  if (state === 0) {
    fs.mkdirSync(path.dirname(launch.log), { recursive: true });
    const out = fs.openSync(launch.log, "w");
    const child = spawn(launch.exe, launch.args, { cwd: launch.cwd, detached: true, windowsHide: true, stdio: ["ignore", out, out] });
    fs.closeSync(out);
    child.on("exit", (code) => (proc.exited = code ?? -1));
    child.on("error", (e) => {
      proc.exited = -1;
      fs.appendFileSync(launch.log, `\n${e.message}\n`);
    });
    child.unref();
  }
  status(ctx, say("loading", { model: launch.model ?? "", port: launch.port }));
  const until = Date.now() + LOAD_TIMEOUT_MS;
  while (Date.now() < until) {
    await sleep(1000);
    if (proc.exited !== null) {
      status(ctx, undefined);
      throw new Error(say("exited", { code: proc.exited, line: lastError(launch.log), log: launch.log }));
    }
    state = await health(launch.port);
    if (state === 200) {
      status(ctx, say("ready", { port: launch.port }));
      return;
    }
  }
  status(ctx, undefined);
  throw new Error(say("timeout", { log: launch.log }));
}

function stop(): Promise<boolean> {
  return new Promise((resolve) => {
    execFile("taskkill", ["/IM", "llama-server.exe", "/F"], { windowsHide: true }, (error) => resolve(!error));
  });
}

const usesLlama = (model: { provider?: string } | undefined) => model?.provider === PROVIDER;

function startInBackground(ctx: Ui) {
  ensure(ctx).catch((e: unknown) => report(ctx, e));
}

export default function (pi: ExtensionAPI) {
  // Start early: while Pi opens and the first prompt is typed, the model is already loading.
  pi.on("session_start", async (_event, ctx) => {
    if (usesLlama(ctx.model)) startInBackground(ctx);
  });
  pi.on("model_select", async (event, ctx) => {
    if (usesLlama(event.model)) startInBackground(ctx);
  });
  // Before a prompt goes out: wait until the server answers instead of failing on a closed port.
  pi.on("before_agent_start", async (_event, ctx) => {
    if (!usesLlama(ctx.model)) return;
    if (ctx.hasUI !== false) ctx.ui.setWorkingMessage(say("waiting"));
    try {
      await ensure(ctx);
    } catch (e) {
      report(ctx, e);
    } finally {
      if (ctx.hasUI !== false) ctx.ui.setWorkingMessage();
    }
  });

  pi.registerCommand("llama", {
    description: say("command"),
    handler: async (args, ctx) => {
      const action = args.trim().toLowerCase();
      if (action === "stop") {
        const stopped = await stop();
        status(ctx, undefined);
        ctx.ui.notify(say(stopped ? "stopped" : "notRunning"), "info");
        return;
      }
      if (action === "start") {
        try {
          await ensure(ctx);
          ctx.ui.notify(say("readyLong"), "info");
        } catch (e) {
          ctx.ui.notify((e as Error).message, "error");
        }
        return;
      }
      let port = 8080;
      let model = "";
      try {
        const launch = readLaunch();
        port = launch.port;
        model = launch.model ?? "";
      } catch (e) {
        ctx.ui.notify((e as Error).message, "warning");
        return;
      }
      const state = await health(port);
      ctx.ui.notify(say(state === 200 ? "stateReady" : state === 503 ? "stateLoading" : "stateOff", { model, port }), "info");
    },
  });
}

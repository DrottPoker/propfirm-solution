import { useEffect, useState } from "react";

import { api } from "./api/client";

// The trader's choices in the terminal, such as favorites, volumes, indicators and drawings, are kept in the browser
// and on the trader's login, so they follow the trader to another device (ADR 0052). The browser's copy is read at
// once. The login's copy is loaded before the terminal shows and wins, except over changes the browser has not sent
// yet, and every change is sent to it shortly after.

const prefix = "trading.";

/** Whose settings the browser holds: the server and user they were loaded for. */
const ownerKey = "trading.settingsOwner";

/** The keys changed in the browser and not yet sent, for example because the page was closed or the service was away. */
const unsentKey = "trading.settingsUnsent";

// The server and account last used stay on the device, since they decide whose settings to load.
const deviceOnly = new Set(["trading.server", "trading.account", ownerKey, unsentKey]);

// As the trading service allows them.
const keyPattern = /^[A-Za-z0-9._-]{1,120}$/;

const uploadDelayMs = 1_000;
const retryDelayMs = 10_000;

/** Whether the key follows the trader to other devices. */
export function isSynced(key: string): boolean {
  return key.startsWith(prefix) && !deviceOnly.has(key) && keyPattern.test(key);
}

/** The stored value, or null when there is none or the browser refuses storage. */
export function readSetting(key: string): string | null {
  try {
    return typeof window === "undefined" ? null : window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

/** Stores the value, or removes it with null, and sends it to the trader's login once their settings are loaded. */
export function writeSetting(key: string, value: string | null) {
  try {
    if (value === null) {
      window.localStorage.removeItem(key);
    } else {
      window.localStorage.setItem(key, value);
    }
  } catch {
    // Private windows may refuse storage. The login still keeps the choice.
  }

  if (active && isSynced(key)) {
    markUnsent(key, true);
    active.uploader.queue(key, value);
  }
}

/** What loading the login's settings does to the browser's. */
export interface SyncPlan {
  /** Values from the login, written to the browser. */
  write: Record<string, string>;
  /** Keys removed from the browser. */
  remove: string[];
  /** Values the login lacks or has older, sent to it. Null removes one. */
  upload: Record<string, string | null>;
}

/**
 * The login's settings win, except over changes the browser has not sent yet, which are sent now. Settings only the
 * browser has are sent to the login too when the browser held nobody's settings before, and removed when it held
 * another trader's or the same trader's: then they were removed on another device.
 */
export function planSync(
  local: Record<string, string>,
  remote: Record<string, unknown>,
  { localOwner, owner, unsent }: { localOwner: string | null; owner: string; unsent: readonly string[] },
): SyncPlan {
  const plan: SyncPlan = { write: {}, remove: [], upload: {} };
  const own = localOwner === owner;
  const stored = new Set<string>();
  if (own) {
    for (const key of unsent.filter(isSynced)) {
      stored.add(key);
      if (local[key] !== remote[key]) {
        plan.upload[key] = local[key] ?? null;
      }
    }
  }

  for (const [key, value] of Object.entries(remote)) {
    if (isSynced(key) && typeof value === "string" && !stored.has(key)) {
      stored.add(key);
      if (local[key] !== value) {
        plan.write[key] = value;
      }
    }
  }

  for (const [key, value] of Object.entries(local)) {
    if (!isSynced(key) || stored.has(key)) {
      continue;
    }

    if (localOwner === null) {
      plan.upload[key] = value;
    } else {
      plan.remove.push(key);
    }
  }

  return plan;
}

/** How a send went: done, worth trying again later, or the session ended so nothing more can be sent. */
export type SendResult = "done" | "retry" | "stop";

export type Send = (key: string, value: string | null, keepalive: boolean) => Promise<SendResult>;

export interface Uploader {
  /** Sends the value a moment later, with any other change made meanwhile. Null removes it. */
  queue(key: string, value: string | null): void;
  /** Sends what is waiting now. With keepalive, the request outlives a page that is closing. */
  flush(keepalive?: boolean): Promise<void>;
  /** Sends nothing more. */
  stop(): void;
}

/**
 * Collects changes for a moment and sends the latest value of each key. A failed send is tried again later.
 * <c>sent</c> hears of each key whose latest value is done with.
 */
export function createUploader(
  send: Send,
  { sent = () => undefined, delayMs = uploadDelayMs, retryMs = retryDelayMs }: { sent?: (key: string) => void; delayMs?: number; retryMs?: number } = {},
): Uploader {
  const pending = new Map<string, string | null>();
  let timer: ReturnType<typeof setTimeout> | undefined;
  let stopped = false;

  const schedule = (ms: number) => {
    clearTimeout(timer);
    timer = setTimeout(() => void flush(false), ms);
  };

  const flush = async (keepalive = false) => {
    clearTimeout(timer);
    const batch = [...pending];
    pending.clear();
    const results = await Promise.all(batch.map(async ([key, value]) => [key, value, await send(key, value, keepalive)] as const));
    for (const [key, value, result] of results) {
      // A newer value queued meanwhile replaces this one.
      if (result === "stop") {
        stopped = true;
      } else if (result === "retry" && !stopped && !pending.has(key)) {
        pending.set(key, value);
      } else if (result === "done" && !pending.has(key)) {
        sent(key);
      }
    }

    if (stopped) {
      pending.clear();
    } else if (pending.size > 0) {
      schedule(retryMs);
    }
  };

  return {
    queue(key, value) {
      if (stopped) {
        return;
      }

      pending.set(key, value);
      schedule(delayMs);
    },
    flush,
    stop() {
      stopped = true;
      clearTimeout(timer);
      pending.clear();
    },
  };
}

const sendSetting: Send = async (key, value, keepalive) => {
  try {
    const path = { params: { path: { key } } };
    const { response } =
      value === null
        ? await api.DELETE("/api/me/settings/{key}", { ...path, keepalive })
        : await api.PUT("/api/me/settings/{key}", { ...path, body: value, keepalive });
    if (response.ok) {
      return "done";
    }

    // A value too large or one setting too many is never stored, so it is not tried again.
    return response.status === 401 ? "stop" : response.status >= 500 || response.status === 429 ? "retry" : "done";
  } catch {
    return "retry";
  }
};

/** Parses the stored list of unsent keys, ignoring anything that is not one. */
export function parseUnsent(raw: string | null): string[] {
  try {
    const value: unknown = raw ? JSON.parse(raw) : [];
    return Array.isArray(value) ? value.filter((k): k is string => typeof k === "string") : [];
  } catch {
    return [];
  }
}

function markUnsent(key: string, unsent: boolean) {
  const keys = new Set(parseUnsent(readSetting(unsentKey)));
  if (keys.has(key) === unsent) {
    return;
  }

  if (unsent) {
    keys.add(key);
  } else {
    keys.delete(key);
  }

  try {
    window.localStorage.setItem(unsentKey, JSON.stringify([...keys]));
  } catch {
    // Without storage nothing outlives the page anyway.
  }
}

let active: { owner: string; uploader: Uploader } | null = null;
let loading: { owner: string; done: Promise<void> } | null = null;
const listeners = new Set<() => void>();

/** Calls the listener when the login's settings have replaced the browser's. Returns how to stop. */
export function onSettingsLoaded(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/**
 * Loads the settings of the trader, named by server and user, into the browser, and from then on sends every change
 * to their login. Without an answer the browser's own settings are used, and kept on the device only.
 */
export function loadSettings(owner: string): Promise<void> {
  if (active?.owner === owner) {
    return Promise.resolve();
  }

  if (loading?.owner !== owner) {
    active?.uploader.stop();
    active = null;
    loading = { owner, done: load(owner) };
  }

  return loading.done;
}

/** Whether the settings of the trader, named by server and user, are loaded. Loads them first. */
export function useLoadedSettings(owner: string | null): boolean {
  const [loaded, setLoaded] = useState<string | null>(null);
  useEffect(() => {
    if (owner === null) {
      return;
    }

    let current = true;
    void loadSettings(owner).then(() => {
      if (current) {
        setLoaded(owner);
      }
    });
    return () => {
      current = false;
    };
  }, [owner]);

  return owner !== null && loaded === owner;
}

/** Sends the changes still waiting, for example before the trader logs out. */
export function flushSettings(): Promise<void> {
  return active?.uploader.flush() ?? Promise.resolve();
}

async function load(owner: string) {
  const result = await api.GET("/api/me/settings").catch(() => null);
  if (loading?.owner !== owner) {
    return;
  }

  loading = null;
  if (!result?.data) {
    return;
  }

  try {
    const storage = window.localStorage;
    const local: Record<string, string> = {};
    for (let i = 0; i < storage.length; i++) {
      const key = storage.key(i);
      const value = key === null ? null : storage.getItem(key);
      if (key !== null && value !== null && isSynced(key)) {
        local[key] = value;
      }
    }

    const plan = planSync(local, result.data.settings, {
      localOwner: storage.getItem(ownerKey),
      owner,
      unsent: parseUnsent(storage.getItem(unsentKey)),
    });
    Object.entries(plan.write).forEach(([key, value]) => storage.setItem(key, value));
    plan.remove.forEach((key) => storage.removeItem(key));
    storage.setItem(ownerKey, owner);
    storage.setItem(unsentKey, JSON.stringify(Object.keys(plan.upload)));

    const uploader = createUploader(sendSetting, { sent: (key) => markUnsent(key, false) });
    active = { owner, uploader };
    Object.entries(plan.upload).forEach(([key, value]) => uploader.queue(key, value));
  } catch {
    // Private windows may refuse storage. The terminal then keeps its choices for this page only.
    return;
  }

  listeners.forEach((listener) => listener());
}

// A change made just before the page closes is still sent, and kept as unsent until the service has it.
if (typeof window !== "undefined") {
  window.addEventListener("pagehide", () => void active?.uploader.flush(true));
}

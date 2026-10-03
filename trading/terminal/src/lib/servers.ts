import type { ServerInfo } from "./api/types";

const storageKey = "trading.server";

/**
 * The server to preselect on the login page: the one in the link from the firm's portal, then the last
 * one used on this device, then the only one. Empty when the trader has to choose.
 */
export function initialServer(servers: readonly ServerInfo[], requested: string | null, remembered: string | null): string {
  for (const candidate of [requested, remembered]) {
    if (candidate && servers.some((s) => s.id === candidate)) {
      return candidate;
    }
  }

  return servers.length === 1 ? servers[0].id : "";
}

/** The server last logged in to on this device, if the browser allows storage. */
export function rememberedServer(): string | null {
  try {
    return typeof window === "undefined" ? null : window.localStorage.getItem(storageKey);
  } catch {
    return null;
  }
}

export function rememberServer(serverId: string) {
  try {
    window.localStorage.setItem(storageKey, serverId);
  } catch {
    // Private windows may refuse storage. Remembering the server is only a convenience.
  }
}

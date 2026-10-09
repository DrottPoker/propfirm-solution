import type { ServerInfo } from "./api/types";

const serverKey = "trading.server";
const accountKey = "trading.account";

/**
 * Where a trader of the server logs in, with the account to open afterwards, or null when the firm's traders log in
 * here with a password. The firm's portal opens the terminal again with a one-time link.
 */
export function portalLogin(server: ServerInfo | null | undefined, accountId: string | null): string | null {
  if (!server?.loginUrl) {
    return null;
  }

  const url = new URL(server.loginUrl);
  if (accountId) {
    url.searchParams.set("account", accountId);
  }

  return url.toString();
}

/** The server last logged in to on this device, if the browser allows storage. */
export function rememberedServer(): string | null {
  return read(serverKey);
}

export function rememberServer(serverId: string) {
  write(serverKey, serverId);
}

/** The account last open on this device, so a trader who logs in again comes back to it. */
export function rememberedAccount(): string | null {
  return read(accountKey);
}

export function rememberAccount(accountId: string) {
  write(accountKey, accountId);
}

function read(key: string): string | null {
  try {
    return typeof window === "undefined" ? null : window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

function write(key: string, value: string) {
  try {
    window.localStorage.setItem(key, value);
  } catch {
    // Private windows may refuse storage. Remembering is only a convenience.
  }
}

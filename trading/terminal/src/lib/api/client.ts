import createClient from "openapi-fetch";

import { tradingApiUrl } from "../config";
import type { paths } from "./schema";

// The session cookie is sent with every call.
export const api = createClient<paths>({ baseUrl: tradingApiUrl, credentials: "include" });

const sessionListeners = new Set<() => void>();
let loggedOut = false;
let expired = false;

/** Calls the listener when the service no longer accepts the session, for example when it expired. Returns how to stop. */
export function onSessionEnded(listener: () => void): () => void {
  sessionListeners.add(listener);
  return () => sessionListeners.delete(listener);
}

/** The trader logs out, so calls refused from now on are no session that ended by itself. */
export function markLoggedOut() {
  loggedOut = true;
  expired = false;
}

/** A new session started. */
export function markLoggedIn() {
  loggedOut = false;
  expired = false;
}

/** Whether the last session ended by itself, for example when it expired, and not because the trader logged out. */
export function sessionExpired(): boolean {
  return expired;
}

// A login that fails also answers 401, but that is no session ending.
api.use({
  onResponse({ request, response }) {
    if (response.status === 401 && !loggedOut && !new URL(request.url).pathname.startsWith("/api/auth/")) {
      expired = true;
      sessionListeners.forEach((listener) => listener());
    }
  },
});

/** The service rejected a command. The reason is the engine's reject reason, for example "StalePrice". */
export class CommandRejectedError extends Error {
  constructor(
    readonly reason: string,
    readonly status: number,
  ) {
    super(`Rejected: ${reason}`);
    this.name = "CommandRejectedError";
  }
}

/** Unwraps a command response, or throws with the reason from the problem response. */
export function commandResult<T>(result: { data?: T; error?: unknown; response: Response }): T {
  if (result.data !== undefined) {
    return result.data;
  }

  throw new CommandRejectedError(reasonOf(result.error), result.response.status);
}

/** Unwraps a query response, or throws. */
export function queryResult<T>(result: { data?: T; error?: unknown; response: Response }, what: string): T {
  if (result.data !== undefined) {
    return result.data;
  }

  throw new Error(`Could not load ${what} (HTTP ${result.response.status}).`);
}

function reasonOf(error: unknown): string {
  if (typeof error === "object" && error !== null && "reason" in error && typeof error.reason === "string") {
    return error.reason;
  }

  return "Unknown";
}

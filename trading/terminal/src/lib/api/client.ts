import createClient from "openapi-fetch";

import { tradingApiUrl } from "../config";
import type { paths } from "./schema";

export const api = createClient<paths>({ baseUrl: tradingApiUrl });

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

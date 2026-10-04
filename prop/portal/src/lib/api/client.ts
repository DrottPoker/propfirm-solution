import createClient from "openapi-fetch";

import type { paths } from "./schema";

// Same origin: the portal passes /api/portal on to Prop.Api, and the session cookie goes with every call.
export const api = createClient<paths>({ baseUrl: "", credentials: "same-origin" });

/** The service said no. The title is the problem response's, for example "The trader has no open trading account right now." */
export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

/** Unwraps a response, or throws an ApiError with the problem response's title. */
export function resultOf<T>(result: { data?: T; error?: unknown; response: Response }, what: string): T {
  if (result.data !== undefined) {
    return result.data;
  }

  throw new ApiError(titleOf(result.error) ?? `Could not load ${what} (HTTP ${result.response.status}).`, result.response.status);
}

/** Throws the problem's title, for a call that answers without a body when it works. */
export function ensureOk(result: { error?: unknown; response: Response }, what: string): void {
  if (!result.response.ok) {
    throw new ApiError(titleOf(result.error) ?? `Could not send ${what} (HTTP ${result.response.status}).`, result.response.status);
  }
}

function titleOf(error: unknown): string | null {
  if (typeof error === "object" && error !== null && "title" in error && typeof error.title === "string") {
    return error.title;
  }

  return null;
}

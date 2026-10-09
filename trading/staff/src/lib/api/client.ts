import createClient from "openapi-fetch";

import { tradingApiUrl } from "../config";
import type { paths } from "./schema";

// The staff session cookie is sent with every call.
export const api = createClient<paths>({ baseUrl: tradingApiUrl, credentials: "include" });

const sessionListeners = new Set<() => void>();

/** Calls the listener when the service no longer accepts the session, for example when it expired. Returns how to stop. */
export function onSessionEnded(listener: () => void): () => void {
  sessionListeners.add(listener);
  return () => sessionListeners.delete(listener);
}

// A login that fails also answers 401, but that is no session ending.
api.use({
  onResponse({ request, response }) {
    const path = new URL(request.url).pathname;
    if (response.status === 401 && path !== "/api/staff/v1/login" && path !== "/api/staff/v1/me") {
      sessionListeners.forEach((listener) => listener());
    }
  },
});

/** The service refused a request. The title says why, and the reason is a code such as "DuplicateId", when there is one. */
export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly reason: string | null,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

/** Unwraps a response, or throws with the service's own words. */
export function unwrap<T>(result: { data?: T; error?: unknown; response: Response }, what: string): T {
  if (result.data !== undefined) {
    return result.data;
  }

  throw errorOf(result.error, result.response.status, `Could not ${what} (HTTP ${result.response.status}).`);
}

/** Throws unless the response is a success, for requests that answer without a body. */
export function ensureOk(result: { error?: unknown; response: Response }, what: string): void {
  if (!result.response.ok) {
    throw errorOf(result.error, result.response.status, `Could not ${what} (HTTP ${result.response.status}).`);
  }
}

function errorOf(error: unknown, status: number, fallback: string): ApiError {
  const problem = typeof error === "object" && error !== null ? (error as { title?: unknown; reason?: unknown }) : {};
  return new ApiError(
    typeof problem.title === "string" && problem.title.length > 0 ? problem.title : fallback,
    status,
    typeof problem.reason === "string" ? problem.reason : null,
  );
}

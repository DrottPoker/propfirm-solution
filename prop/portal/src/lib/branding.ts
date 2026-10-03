import "server-only";

import { headers } from "next/headers";
import { cache } from "react";

import type { Branding } from "./api/types";
import { propApiUrl } from "./config";

export type PortalBranding =
  | { kind: "found"; branding: Branding }
  | { kind: "unknown-host" }
  | { kind: "unavailable" };

/**
 * The branding of the firm whose portal is at the address the page was opened on. Asked once per request,
 * so the page and its metadata share the answer.
 */
export const getBranding = cache(async (): Promise<PortalBranding> => {
  const incoming = await headers();
  const host = incoming.get("x-forwarded-host") ?? incoming.get("host");
  if (!host) {
    return { kind: "unknown-host" };
  }

  try {
    const response = await fetch(`${propApiUrl}/api/portal/branding`, { headers: { "X-Forwarded-Host": host }, cache: "no-store" });
    if (response.status === 404) {
      return { kind: "unknown-host" };
    }

    return response.ok ? { kind: "found", branding: (await response.json()) as Branding } : { kind: "unavailable" };
  } catch {
    return { kind: "unavailable" };
  }
});

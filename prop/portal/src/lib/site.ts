import "server-only";

import { headers } from "next/headers";
import { cache } from "react";

import type { Branding, Platform } from "./api/types";
import { propApiUrl } from "./config";

/**
 * What is at the address the page was opened on: a firm's portal, our own platform where firms sign up, or
 * nothing at all.
 */
export type Site =
  | { kind: "firm"; branding: Branding }
  | { kind: "platform"; platform: Platform }
  | { kind: "unknown-host" }
  | { kind: "unavailable" };

/** Asked once per request, so the layouts, the page and its metadata share the answer. */
export const getSite = cache(async (): Promise<Site> => {
  const incoming = await headers();
  const host = incoming.get("x-forwarded-host") ?? incoming.get("host");
  if (!host) {
    return { kind: "unknown-host" };
  }

  try {
    const firm = await fetch(`${propApiUrl}/api/portal/branding`, { headers: { "X-Forwarded-Host": host }, cache: "no-store" });
    if (firm.ok) {
      return { kind: "firm", branding: (await firm.json()) as Branding };
    }

    if (firm.status !== 404) {
      return { kind: "unavailable" };
    }

    // Most addresses are firms', so the platform is asked only when no firm is at the address.
    const platform = await fetch(`${propApiUrl}/api/portal/platform`, { headers: { "X-Forwarded-Host": host }, cache: "no-store" });
    if (platform.ok) {
      return { kind: "platform", platform: (await platform.json()) as Platform };
    }

    return platform.status === 404 ? { kind: "unknown-host" } : { kind: "unavailable" };
  } catch {
    return { kind: "unavailable" };
  }
});

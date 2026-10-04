import "server-only";

import { headers } from "next/headers";
import { cache } from "react";

import type { Branding, OpsSite, Platform } from "./api/types";
import { propApiUrl } from "./config";

/**
 * What is at the address the page was opened on: a firm's portal, our own platform where firms sign up, our own
 * admin view where our staff review firms, or nothing at all.
 */
export type Site =
  | { kind: "firm"; branding: Branding }
  | { kind: "platform"; platform: Platform }
  | { kind: "ops"; ops: OpsSite }
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

    // Most addresses are firms', so the platform and our admin view are asked only when no firm is at the address.
    const platform = await fetch(`${propApiUrl}/api/portal/platform`, { headers: { "X-Forwarded-Host": host }, cache: "no-store" });
    if (platform.ok) {
      return { kind: "platform", platform: (await platform.json()) as Platform };
    }

    if (platform.status !== 404) {
      return { kind: "unavailable" };
    }

    const ops = await fetch(`${propApiUrl}/api/portal/ops`, { headers: { "X-Forwarded-Host": host }, cache: "no-store" });
    if (ops.ok) {
      return { kind: "ops", ops: (await ops.json()) as OpsSite };
    }

    return ops.status === 404 ? { kind: "unknown-host" } : { kind: "unavailable" };
  } catch {
    return { kind: "unavailable" };
  }
});

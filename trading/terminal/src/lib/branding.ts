import { headers } from "next/headers";

import type { Branding } from "./api/types";
import { tradingApiUrl } from "./config";
import { hostWithoutPort } from "./theme";

// The terminal's server may reach the service on another address than the browser does.
const serverApiUrl = process.env.TRADING_API_URL ?? tradingApiUrl;

/** Server only. The branding of the firm that owns the address the terminal was opened on. */
export async function getBranding(): Promise<Branding | null> {
  const host = hostWithoutPort((await headers()).get("host"));
  if (!host) {
    return null;
  }

  try {
    const response = await fetch(`${serverApiUrl}/api/branding?host=${encodeURIComponent(host)}`, { next: { revalidate: 60 } });
    return response.ok ? ((await response.json()) as Branding) : null;
  } catch {
    // Without the service the terminal still renders, with the default look.
    return null;
  }
}

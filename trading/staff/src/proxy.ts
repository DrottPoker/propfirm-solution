import { NextResponse, type NextRequest } from "next/server";

import { tradingApiUrl } from "@/lib/config";
import { contentSecurityPolicy, createNonce } from "@/lib/securityHeaders";

// Every page gets a nonce of its own and the policy with it (ADR 0060). Next.js reads the nonce from the policy on the
// request and puts it on its own scripts. The panel has no inline scripts of its own.
export function proxy(request: NextRequest) {
  const nonce = createNonce();
  const policy = contentSecurityPolicy({
    nonce,
    apiUrl: tradingApiUrl,
    pageOrigin: request.nextUrl.origin,
    development: process.env.NODE_ENV === "development",
  });

  const headers = new Headers(request.headers);
  headers.set("x-nonce", nonce);
  headers.set("Content-Security-Policy", policy);
  const response = NextResponse.next({ request: { headers } });
  response.headers.set("Content-Security-Policy", policy);
  return response;
}

// Pages only: built files carry no scripts of their own, and a prefetch is no page.
export const config = {
  matcher: [
    {
      source: "/((?!_next/static|_next/image|favicon.ico).*)",
      missing: [
        { type: "header", key: "next-router-prefetch" },
        { type: "header", key: "purpose", value: "prefetch" },
      ],
    },
  ],
};

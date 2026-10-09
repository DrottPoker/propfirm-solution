// The rules the browser holds the terminal to (ADR 0060). A script runs only with the nonce of the page it came with, or
// when such a script loads it, so a script slipped into a page by a bug never runs. The page talks only to the trading
// service, is never shown inside another site, and loads nothing from elsewhere but the firms' logos.

/** What the page may run, load and connect to, for one response with its own nonce. */
export function contentSecurityPolicy({
  nonce,
  apiUrl,
  pageOrigin,
  development,
}: {
  nonce: string;
  /** The trading service, for its API and its realtime connection. */
  apiUrl: string;
  /** The terminal's own address, whose reloads in development come over a websocket. */
  pageOrigin: string;
  development: boolean;
}): string {
  const api = new URL(apiUrl);
  const page = new URL(pageOrigin);
  const socket = (url: URL) => `${url.protocol === "https:" ? "wss:" : "ws:"}//${url.host}`;
  const directives = [
    "default-src 'self'",
    // React rebuilds error stacks with eval in development only.
    `script-src 'self' 'nonce-${nonce}' 'strict-dynamic'${development ? " 'unsafe-eval'" : ""}`,
    // Style attributes, the fonts' rules and the toasts' styles are inline. A style cannot run code.
    "style-src 'self' 'unsafe-inline'",
    // A firm's logo can be anywhere on the web, and in development on this computer.
    `img-src 'self' data: blob: https:${development ? " http://localhost:* http://*.localhost:*" : ""}`,
    "font-src 'self'",
    `connect-src 'self' ${api.origin} ${socket(api)}${development ? ` ${socket(page)}` : ""}`,
    "object-src 'none'",
    "base-uri 'none'",
    "form-action 'self'",
    "frame-src 'none'",
    "frame-ancestors 'none'",
    ...(development ? [] : ["upgrade-insecure-requests"]),
  ];
  return directives.join("; ");
}

/** A nonce for one response: 128 random bits, as base64. */
export function createNonce(): string {
  return btoa(String.fromCharCode(...crypto.getRandomValues(new Uint8Array(16))));
}

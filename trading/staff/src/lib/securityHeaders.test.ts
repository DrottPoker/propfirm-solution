import { describe, expect, it } from "vitest";

import { contentSecurityPolicy, createNonce } from "./securityHeaders";

const directive = (policy: string, name: string) => policy.split("; ").find((d) => d.startsWith(`${name} `));

describe("contentSecurityPolicy", () => {
  const production = contentSecurityPolicy({
    nonce: "abc123",
    apiUrl: "https://api.kronanttrader.com",
    pageOrigin: "https://staff.kronanttrader.com",
    development: false,
  });

  it("runs only scripts with the page's nonce and those they load", () => {
    expect(directive(production, "script-src")).toBe("script-src 'self' 'nonce-abc123' 'strict-dynamic'");
    expect(production).not.toContain("unsafe-eval");
  });

  it("talks only to the trading service, also over its secure realtime connection", () => {
    expect(directive(production, "connect-src")).toBe("connect-src 'self' https://api.kronanttrader.com wss://api.kronanttrader.com");
  });

  it("is never shown inside another site, runs no plugins and upgrades plain addresses", () => {
    expect(production).toContain("frame-ancestors 'none'");
    expect(production).toContain("object-src 'none'");
    expect(production).toContain("base-uri 'none'");
    expect(production).toContain("upgrade-insecure-requests");
    expect(directive(production, "img-src")).toBe("img-src 'self' data: blob: https:");
  });

  it("allows what development needs on this computer", () => {
    const development = contentSecurityPolicy({ nonce: "n", apiUrl: "http://localhost:5101", pageOrigin: "http://localhost:3003", development: true });

    expect(directive(development, "script-src")).toContain("'unsafe-eval'");
    expect(directive(development, "connect-src")).toBe("connect-src 'self' http://localhost:5101 ws://localhost:5101 ws://localhost:3003");
    expect(directive(development, "img-src")).toContain("http://*.localhost:*");
    expect(development).not.toContain("upgrade-insecure-requests");
  });
});

describe("createNonce", () => {
  it("gives 128 random bits, new every time", () => {
    const first = createNonce();

    expect(atob(first)).toHaveLength(16);
    expect(createNonce()).not.toBe(first);
  });
});

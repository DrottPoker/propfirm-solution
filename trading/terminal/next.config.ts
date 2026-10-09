import type { NextConfig } from "next";

const production = process.env.NODE_ENV === "production";

const nextConfig: NextConfig = {
  // The end-to-end tests run their own dev server, which must not share build output with yours.
  distDir: process.env.NEXT_DIST_DIR ?? ".next",
  // The rules for every response (ADR 0060). The policy for what a page may run comes from the proxy, with a nonce of
  // its own for every page. The terminal is never shown inside another site's page, so no page can lay itself over its
  // buttons (ADR 0058).
  async headers() {
    return [
      {
        source: "/:path*",
        headers: [
          { key: "X-Frame-Options", value: "DENY" },
          { key: "X-Content-Type-Options", value: "nosniff" },
          { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
          { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=(), payment=(), usb=(), fullscreen=(self)" },
          { key: "Cross-Origin-Opener-Policy", value: "same-origin" },
          ...(production ? [{ key: "Strict-Transport-Security", value: "max-age=31536000; includeSubDomains" }] : []),
        ],
      },
    ];
  },
};

export default nextConfig;

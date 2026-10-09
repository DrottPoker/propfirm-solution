import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // The end-to-end tests run their own dev server, which must not share build output with yours.
  distDir: process.env.NEXT_DIST_DIR ?? ".next",
  // The terminal is never shown inside another site's page, so no page can lay itself over its buttons (ADR 0058).
  async headers() {
    return [
      {
        source: "/:path*",
        headers: [
          { key: "Content-Security-Policy", value: "frame-ancestors 'none'" },
          { key: "X-Frame-Options", value: "DENY" },
        ],
      },
    ];
  },
};

export default nextConfig;

import type { NextConfig } from "next";

// The same address as propApiUrl in src/lib/config.ts. The config file cannot import app code when the
// dev server is started from another directory.
const propApiUrl = process.env.PROP_API_URL ?? "http://localhost:5201";

const nextConfig: NextConfig = {
  // The end-to-end tests run their own dev server, which must not share build output with yours.
  distDir: process.env.NEXT_DIST_DIR ?? ".next",

  // Browsers only talk to the portal's own address, so the session cookie belongs to the firm's domain.
  // Next.js passes the original host on as X-Forwarded-Host, and Prop.Api knows the firm from it.
  async rewrites() {
    return [{ source: "/api/portal/:path*", destination: `${propApiUrl}/api/portal/:path*` }];
  },
};

export default nextConfig;

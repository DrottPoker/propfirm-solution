import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // The end-to-end tests run their own dev server, which must not share build output with yours.
  distDir: process.env.NEXT_DIST_DIR ?? ".next",
};

export default nextConfig;

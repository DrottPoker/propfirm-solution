"use client";

import { useBranding } from "@/app/providers";

/** Tells everyone on a firm's portal that the firm is still testing, so no trader takes it for the real thing. */
export function SandboxBanner() {
  const { status } = useBranding();
  if (status === "Live") {
    return null;
  }

  return (
    <div role="status" className="border-b border-warning/40 bg-warning/10 px-6 py-2 text-center text-sm text-warning">
      {status === "Provisioning"
        ? "Test environment. The firm's trading server is being set up."
        : "Test environment. This firm is trying the platform, and nothing here is real."}
    </div>
  );
}

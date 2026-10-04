"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";

import { useBranding } from "@/app/providers";
import { useFirmStatus } from "@/lib/queries";

/**
 * Tells everyone on a firm's portal that the firm is still testing, so no trader takes it for the real thing. The firm's
 * look comes with the page, so the page is rendered again when the firm's server is ready or the firm goes live.
 */
export function SandboxBanner() {
  const { status } = useBranding();
  const router = useRouter();
  const current = useFirmStatus(status);

  useEffect(() => {
    if (current.data && current.data !== status) {
      router.refresh();
    }
  }, [current.data, status, router]);

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

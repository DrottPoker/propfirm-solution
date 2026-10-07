"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect } from "react";

import { useBranding } from "@/app/providers";
import { useFirmStatus } from "@/lib/queries";

/**
 * Tells everyone on a firm's portal that the firm is still testing, so no trader takes it for the real thing: a quiet
 * line, since it is information and not a warning. The firm's own administrators are told it is their firm, with the
 * way to live. The firm's look comes with the page, so the page is rendered again when the firm's server is ready or
 * the firm goes live.
 */
export function SandboxBanner() {
  const { status } = useBranding();
  const router = useRouter();
  const path = usePathname();
  const current = useFirmStatus(status);
  const admin = path === "/admin" || path.startsWith("/admin/");

  useEffect(() => {
    if (current.data && current.data !== status) {
      router.refresh();
    }
  }, [current.data, status, router]);

  if (status === "Live") {
    return null;
  }

  return (
    <div role="status" className="flex items-center justify-center border-b border-accent/20 bg-accent/[0.07] px-6 py-1.5 text-center text-[13px] text-foreground/80 print:hidden">
      {status === "Provisioning" ? (
        "Test environment. The firm's trading server is being set up."
      ) : admin ? (
        <span>
          Test environment: your firm is in the sandbox, and nothing here is real. Your traders see this line too.{" "}
          {!path.startsWith("/admin/go-live") && (
            <Link href="/admin/go-live" className="font-medium text-accent hover:underline">
              Go live
            </Link>
          )}
        </span>
      ) : (
        "Test environment. This firm is trying the platform, and nothing here is real."
      )}
    </div>
  );
}

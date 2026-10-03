"use client";

import Link from "next/link";

import { useVerifySignup } from "@/lib/queries";

import { PlatformCard } from "./PlatformCard";
import { buttonClass, ErrorText } from "./ui";

/**
 * The link in the confirmation email. The person confirms with a click, so a mail scanner that opens the link
 * does not use it up. Then the firm is created and its admin panel opens on the firm's own address.
 */
export function VerifySignup({ token }: { token: string | null }) {
  const verify = useVerifySignup();

  const confirm = () => {
    if (token) {
      verify.mutate(token, { onSuccess: (result) => window.location.assign(result.adminUrl) });
    }
  };

  return (
    <PlatformCard title="Confirm your email">
      {token ? (
        <>
          <p className="text-sm">Confirm your email address to create your firm and open its admin panel.</p>
          <ErrorText error={verify.error} />
          <button type="button" onClick={confirm} disabled={verify.isPending || verify.isSuccess} className={buttonClass}>
            {verify.isPending || verify.isSuccess ? "Creating your firm..." : "Confirm and open the admin panel"}
          </button>
        </>
      ) : (
        <p role="alert" className="text-sm text-loss">
          This link has no confirmation in it. Open the link in the email again.
        </p>
      )}
      <Link href="/signup" className="text-xs text-muted hover:text-foreground">
        Sign up again
      </Link>
    </PlatformCard>
  );
}

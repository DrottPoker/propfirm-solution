"use client";

import Link from "next/link";
import { useState } from "react";

import { usePlatform } from "@/app/providers";
import { useLoginHelp } from "@/lib/passwordQueries";
import { portalAddress } from "@/lib/signup";

import { PlatformCard } from "./PlatformCard";
import { buttonClass, ErrorText, fieldClass } from "./ui";

/**
 * Each firm logs in on its own portal, so the platform's login finds it from the email: it sends a link to the admin
 * panel of each firm the email administers.
 */
export function PlatformLogin() {
  const platform = usePlatform();
  const help = useLoginHelp();
  const [email, setEmail] = useState("");

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    help.mutate(email.trim());
  };

  return (
    <PlatformCard title="Log in to your firm">
      {help.isSuccess ? (
        <div role="status" className="flex flex-col gap-3 text-sm">
          <p>
            If <span className="font-medium">{email.trim()}</span> administers a firm here, we have sent it a link to log in to each of its firms. The
            links work for 1 hour.
          </p>
          <p className="text-xs text-muted">No email? Check the spam folder, or try the email you signed up with.</p>
        </div>
      ) : (
        <form onSubmit={submit} className="flex flex-col gap-4">
          <p className="text-sm text-muted">
            Each firm has its own address, such as <span className="font-mono">{portalAddress(platform.firmPortalUrl, "yourfirm")}admin</span>. Write
            your email, and we send you a link to the admin panel of your firm.
          </p>
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-muted">Email</span>
            <input type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} className={fieldClass} />
          </label>
          <ErrorText error={help.error} />
          <button type="submit" disabled={help.isPending} className={buttonClass}>
            {help.isPending ? "Sending..." : "Email me a login link"}
          </button>
        </form>
      )}
      <p className="text-xs text-muted">
        Not on {platform.name} yet?{" "}
        <Link href="/signup" className="text-accent hover:underline">
          Start a free sandbox
        </Link>
        . Traders log in on their firm&apos;s portal.
      </p>
    </PlatformCard>
  );
}

"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";

import { useBranding } from "@/app/providers";

import type { AccountDetails, MyIdentity } from "@/lib/api/types";
import { asksForIdentity, identityStatus, myIdentityText } from "@/lib/identity";
import { useDecideTestIdentity, useMyIdentity, useStartIdentity } from "@/lib/queries";

import { CheckIcon, ShieldCheckIcon } from "./icons";
import { Badge, buttonClass, dangerButtonClass, ErrorText, Loading, Message, secondaryButtonClass, TraderPage } from "./ui";

/** The button that starts the check, or opens it again, on the provider's page or the firm's own. */
function StartButton({ label, className = buttonClass }: { label: string; className?: string }) {
  const start = useStartIdentity();
  return (
    <div className="flex flex-col items-start gap-1.5">
      <button type="button" disabled={start.isPending || start.isSuccess} onClick={() => start.mutate(undefined, { onSuccess: (url) => window.location.assign(url) })} className={className}>
        {start.isPending || start.isSuccess ? "Opening..." : label}
      </button>
      <ErrorText error={start.error} />
    </div>
  );
}

/** Where the trader's ID check is, with what to do next. */
function IdentityCard({ identity }: { identity: MyIdentity }) {
  const branding = useBranding();
  const text = myIdentityText(identity, branding.name);
  const status = identity.verified ? identityStatus("Approved") : identityStatus(identity.status);
  return (
    <section aria-labelledby="identity-heading" className="flex flex-col gap-4 rounded-lg border border-border bg-panel p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex min-w-0 items-start gap-3">
          <span aria-hidden="true" className={`grid size-10 shrink-0 place-items-center rounded-full ${identity.verified ? "bg-profit/15 text-profit" : "bg-accent/15 text-accent"}`}>
            {identity.verified ? <CheckIcon className="size-5" /> : <ShieldCheckIcon className="size-5" />}
          </span>
          <div className="flex min-w-0 flex-col gap-1">
            <h2 id="identity-heading" className="font-medium">
              {text.title}
            </h2>
            <p className="text-sm text-muted">{text.detail}</p>
          </div>
        </div>
        {identity.mode !== null && <Badge tone={status.tone}>{status.label}</Badge>}
      </div>
      {text.action && identity.canStart && <StartButton label={text.action} />}
      {identity.mode === "BuiltIn" && !identity.verified && (
        <p className="text-xs text-muted">
          The check is done by our ID partner, who keeps your document only as long as the check needs. {branding.name} sees whether you passed, and your name, date of birth
          and country.
        </p>
      )}
    </section>
  );
}

/** The trader's ID check on its own page, where the provider sends the trader back to. */
export function IdentityPage({ returned }: { returned: boolean }) {
  const identity = useMyIdentity();
  if (identity.isError) {
    return <Message text={identity.error.message} />;
  }

  if (!identity.data) {
    return <Loading />;
  }

  const waiting = returned && (identity.data.status === "Pending" || identity.data.status === "InReview") && !identity.data.verified;
  return (
    <TraderPage narrow>
      <div className="flex flex-col gap-1">
        <h1 className="font-serif text-[2.35rem] leading-[1.05] tracking-tight">Your identity</h1>
        <p className="text-muted">Who you are, checked once, before you are paid.</p>
      </div>
      {waiting && (
        <p role="status" className="rounded-lg border border-border bg-panel px-4 py-3 text-sm text-muted">
          Thank you. We are waiting for the result, which usually comes within a minute. This page updates by itself.
        </p>
      )}
      <IdentityCard identity={identity.data} />
      <Link href="/payouts" className="self-start text-sm text-accent hover:underline">
        Back to Payouts
      </Link>
    </TraderPage>
  );
}

/** The trader's ID check on the Payouts page, once the firm has chosen how to check. */
export function IdentityPanel() {
  const identity = useMyIdentity();
  if (!identity.data || identity.data.mode === null) {
    return null;
  }

  return <IdentityCard identity={identity.data} />;
}

/** On the dashboard, once a funded account is near and the firm wants the trader's ID checked first. */
export function VerifyIdentityNotice({ accounts }: { accounts: AccountDetails[] }) {
  const identity = useMyIdentity();
  const branding = useBranding();
  if (!asksForIdentity(identity.data, accounts)) {
    return null;
  }

  const text = myIdentityText(identity.data!, branding.name);
  return (
    <div role="status" className="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-lg border border-accent/40 bg-accent/10 px-4 py-3 text-sm">
      <ShieldCheckIcon className="size-[18px] shrink-0 text-accent" />
      <p className="min-w-0 flex-1">
        <span className="font-medium">{text.title}.</span> <span className="text-muted">{text.detail}</span>
      </p>
      <Link href="/identity" className="font-medium text-accent hover:underline">
        {text.action ?? "See your ID check"}
      </Link>
    </div>
  );
}

/** A test check, for the sandbox and development: no document is checked, and the trader chooses the outcome. */
export function TestIdentityPage({ sessionId }: { sessionId: string | null }) {
  const decide = useDecideTestIdentity();
  const router = useRouter();
  const choose = (approve: boolean) => decide.mutate({ sessionId: sessionId!, approve }, { onSuccess: () => router.replace("/identity?returned=1") });
  return (
    <main className="mx-auto flex w-full max-w-xl flex-col gap-5 px-4 py-10 sm:px-6">
      <div className="flex flex-col gap-1">
        <h1 className="font-serif text-[2.35rem] leading-[1.05] tracking-tight">Test ID check</h1>
        <p className="text-muted">This is a test: no document is checked and nothing is charged. A real check opens our ID partner&apos;s page instead.</p>
      </div>
      {sessionId ? (
        <div className="flex flex-wrap gap-2.5">
          <button type="button" disabled={decide.isPending} onClick={() => choose(true)} className={buttonClass}>
            Approve
          </button>
          <button type="button" disabled={decide.isPending} onClick={() => choose(false)} className={dangerButtonClass}>
            Decline
          </button>
          <Link href="/identity" className={secondaryButtonClass}>
            Cancel
          </Link>
        </div>
      ) : (
        <p className="text-loss">This link has no test check.</p>
      )}
      <ErrorText error={decide.error} />
    </main>
  );
}

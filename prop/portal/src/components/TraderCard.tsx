"use client";

import Link from "next/link";
import { useState } from "react";

import { accountStatus, formatTotals } from "@/lib/admin";
import { countryName } from "@/lib/countries";
import type { Account, TraderIdentity, TraderSummary } from "@/lib/api/types";
import { initials } from "@/lib/dashboard";
import { formatDate, formatDateTime, formatMoney } from "@/lib/format";
import { birthDate, countryText, identityStatus } from "@/lib/identity";
import { providerLabels } from "@/lib/orders";
import { useEmailTrader, useInvite, useSetTraderCheck, useTraderEmailPreview, useTraderSummary } from "@/lib/queries";

import { ConfirmDialog } from "./Dialog";
import { CopyIcon, MailIcon } from "./icons";
import { Badge, ErrorText, fieldClass, secondaryButtonClass } from "./ui";

/**
 * The account's trader as the firm sees them: since when, the firm's own checks of them, their way into the portal, what
 * they bought and were paid out, their accounts at the firm, and what started this account.
 */
export function TraderCard({ account, challengeName }: { account: Account; challengeName: (id: string) => string }) {
  const trader = useTraderSummary(account.id);
  if (trader.isError) {
    return (
      <aside className="rounded-lg border border-border bg-panel p-5">
        <ErrorText error={trader.error} />
      </aside>
    );
  }

  if (!trader.data) {
    return <aside className="rounded-lg border border-border bg-panel p-5 text-sm text-muted">Loading the trader...</aside>;
  }

  const data = trader.data;
  return (
    <aside aria-labelledby="trader-heading" className="flex flex-col gap-5 rounded-lg border border-border bg-panel p-5">
      <div className="flex items-center gap-3">
        <span aria-hidden="true" className="grid size-10 shrink-0 place-items-center rounded-full border border-border bg-background text-sm font-semibold">
          {initials(data.email)}
        </span>
        <div className="flex min-w-0 flex-col">
          <h2 id="trader-heading" className="truncate font-semibold">
            {data.name ?? data.email}
          </h2>
          {data.name && <span className="truncate text-sm text-muted">{data.email}</span>}
          <span className="text-xs text-muted">
            {data.country ? `${countryName(data.country)} · ` : ""}Trader since {formatDate(data.since)}
          </span>
          <span className="flex flex-wrap gap-x-3 text-xs">
            <Link href={`/admin/support/new?email=${encodeURIComponent(data.email)}&account=${account.id}`} className="text-accent hover:underline">
              Write to the trader
            </Link>
            <Link href={`/admin/support?group=All&search=${encodeURIComponent(data.email)}`} className="text-accent hover:underline">
              Support tickets
            </Link>
          </span>
        </div>
      </div>

      <dl className="grid grid-cols-2 gap-3 text-xs">
        <div className="flex flex-col gap-0.5">
          <dt className="text-muted">Bought in your portal</dt>
          <dd className="font-mono text-sm">{data.orders === 0 ? "Nothing" : formatTotals(data.bought, account.currency)}</dd>
        </div>
        <div className="flex flex-col gap-0.5">
          <dt className="text-muted">Paid out</dt>
          <dd className="font-mono text-sm">{formatTotals(data.paidOut, account.currency)}</dd>
        </div>
      </dl>

      {data.identity && <IdentityCheck identity={data.identity} />}

      <Checks account={account} trader={data} />

      <PortalAccess account={account} trader={data} />

      <div className="flex flex-col gap-2">
        <h3 className="text-xs font-medium uppercase tracking-wider text-muted">Accounts at your firm</h3>
        <ul className="flex flex-col text-sm">
          {data.accounts.map((other) => {
            const status = accountStatus(other);
            return (
              <li key={other.id} className="flex items-center justify-between gap-2 border-t border-border py-2">
                {other.id === account.id ? (
                  <span>
                    <span className="font-mono">#{other.number}</span> <span className="text-muted">{challengeName(other.challengeId)} · this one</span>
                  </span>
                ) : (
                  <Link href={`/admin/accounts/${other.id}`} className="min-w-0 truncate">
                    <span className="font-mono text-accent">#{other.number}</span> <span className="text-muted">{challengeName(other.challengeId)}</span>
                  </Link>
                )}
                <Badge tone={status.tone}>{status.label}</Badge>
              </li>
            );
          })}
        </ul>
      </div>

      <div className="flex flex-col gap-2 border-t border-border pt-4 text-sm">
        <h3 className="text-xs font-medium uppercase tracking-wider text-muted">This account</h3>
        <dl className="flex flex-col gap-2">
          <Row label="Started by">{data.order ? `Order #${data.order.number}, ${providerLabels[data.order.provider]}` : "Your firm"}</Row>
          {data.order && (
            <Row label="Price">
              {formatMoney(data.order.amount)} {data.order.currency}
            </Row>
          )}
          {account.reference && <Row label="Your reference">{account.reference}</Row>}
          {account.tradingAccountId && <Row label="Trading account">{account.tradingAccountId}</Row>}
        </dl>
      </div>
    </aside>
  );
}

/**
 * The firm's checks of the trader before it funds them or pays them out, such as their ID, ticked by hand until a
 * verification provider does them. Approving without them asks first.
 */
function Checks({ account, trader }: { account: Account; trader: TraderSummary }) {
  const setCheck = useSetTraderCheck(account.id);
  // A tick shows at once, in the same render as the click, while it is saved.
  const [saving, setSaving] = useState<{ item: string; checked: boolean } | null>(null);
  const toggle = (item: string, checked: boolean) => {
    setSaving({ item, checked });
    setCheck.mutate({ item, checked }, { onSettled: () => setSaving(null) });
  };
  return (
    <fieldset className="flex flex-col gap-2 rounded-lg border border-border p-3.5 text-sm">
      <legend className="px-1 font-medium">Your checks</legend>
      <p className="text-xs text-muted">Tick these once you have seen the documents, before you fund the trader or pay them out.</p>
      {trader.checks.map((check) => (
        <label key={check.item} className="flex items-start gap-2.5">
          <input
            type="checkbox"
            checked={saving?.item === check.item ? saving.checked : check.checkedAt !== null}
            disabled={saving !== null}
            onChange={(e) => toggle(check.item, e.target.checked)}
            className="mt-0.5"
          />
          <span className="flex flex-col">
            <span>{check.label}</span>
            {check.checkedAt && (
              <span className="text-xs text-muted">
                {formatDate(check.checkedAt)}
                {check.checkedBy ? ` by ${check.checkedBy}` : ""}
              </span>
            )}
          </span>
        </label>
      ))}
      <ErrorText error={setCheck.error} />
    </fieldset>
  );
}

/** The trader's KYC (ADR 0042): where the ID check is, who checked, what the document said, or why it was declined. */
function IdentityCheck({ identity }: { identity: TraderIdentity }) {
  const status = identityStatus(identity.status);
  const by = identity.provider === "External" ? "your KYC service" : identity.provider === "Test" ? "a test check" : identity.provider;
  return (
    <section aria-label="KYC" className="flex flex-col gap-2 rounded-lg border border-border p-3.5 text-sm">
      <div className="flex items-center justify-between gap-2">
        <span className="font-medium">KYC</span>
        <Badge tone={status.tone}>{status.label}</Badge>
      </div>
      {identity.status === "Approved" ? (
        <dl className="flex flex-col gap-1.5">
          {identity.fullName && <Row label="Name on ID">{identity.fullName}</Row>}
          {identity.dateOfBirth && <Row label="Born">{birthDate(identity.dateOfBirth)}</Row>}
          {identity.country && <Row label="Country">{countryText(identity.country, countryName)}</Row>}
          {identity.sanctionsChecked && <Row label="Sanctions lists">Not on any</Row>}
        </dl>
      ) : (
        identity.reason && <p className="text-loss">{identity.reason}</p>
      )}
      {identity.decidedAt && (
        <span className="text-xs text-muted">
          {identity.status === "Approved" ? "Verified" : "Decided"} {formatDate(identity.decidedAt)} by {by}
        </span>
      )}
    </section>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex justify-between gap-3">
      <dt className="text-muted">{label}</dt>
      <dd className="min-w-0 break-words text-right">{children}</dd>
    </div>
  );
}

/**
 * Whether the trader can log in to the portal, and the ways to let them in: an email in the firm's name, or a link the
 * firm sends itself. A new link also resets a forgotten password, and replaces the trader's older links.
 */
function PortalAccess({ account, trader }: { account: Account; trader: TraderSummary }) {
  const emailTrader = useEmailTrader();
  const invite = useInvite(account.id);
  const [copied, setCopied] = useState(false);
  // The email is shown before it is sent.
  const [previewing, setPreviewing] = useState(false);
  const preview = useTraderEmailPreview(account.id, previewing);

  const copy = async (url: string) => {
    await navigator.clipboard.writeText(url);
    setCopied(true);
  };

  return (
    <div className="flex flex-col gap-3 rounded-lg border border-border p-3.5 text-sm">
      <div className="flex flex-col gap-0.5">
        <span className="font-medium">Portal access</span>
        <span className="text-xs text-muted">
          {trader.hasPassword ? "Has chosen a password and can log in." : "Has not chosen a password yet, so cannot log in."}
        </span>
      </div>
      <div className="flex flex-wrap gap-2">
        <button type="button" disabled={emailTrader.isPending} onClick={() => setPreviewing(true)} className={`${secondaryButtonClass} flex items-center gap-1.5 text-sm`}>
          <MailIcon className="size-3.5" />
          {emailTrader.isPending ? "Emailing..." : trader.hasPassword ? "Email about this challenge" : "Email an invitation"}
        </button>
        <button type="button" disabled={invite.isPending} onClick={() => invite.mutate()} className={`${secondaryButtonClass} flex items-center gap-1.5 text-sm`}>
          <CopyIcon className="size-3.5" />
          {invite.isPending ? "Creating..." : "Create invitation link"}
        </button>
      </div>
      {emailTrader.isSuccess && (
        <p role="status" className="text-xs text-profit">
          {emailTrader.data.kind === "Invitation" ? `We emailed ${emailTrader.data.email} an invitation.` : `We emailed ${emailTrader.data.email} that the challenge has started.`}
        </p>
      )}
      <ErrorText error={invite.error} />
      <ConfirmDialog
        open={previewing}
        onClose={() => setPreviewing(false)}
        onConfirm={() => emailTrader.mutate(account.id, { onSuccess: () => setPreviewing(false) })}
        title={preview.data ? `Send "${preview.data.subject}"?` : "Send the email?"}
        description={preview.data ? `To ${preview.data.email}, in your firm's name.` : undefined}
        confirmLabel="Send email"
        pendingLabel="Sending..."
        pending={emailTrader.isPending || !preview.data}
      >
        <ErrorText error={preview.error ?? emailTrader.error} />
        {preview.data ? (
          <pre className="max-h-80 overflow-y-auto whitespace-pre-wrap rounded-lg border border-border bg-background p-3.5 font-sans text-sm">{preview.data.body}</pre>
        ) : (
          !preview.error && <p className="text-sm text-muted">Loading the email...</p>
        )}
        {preview.data?.kind === "Invitation" && <p className="text-xs text-muted">The link in the email is made when it is sent.</p>}
      </ConfirmDialog>
      {invite.data && (
        <div className="flex flex-col gap-2">
          <label className="flex flex-col gap-1 text-xs">
            <span className="text-muted">Invitation link, valid until {formatDateTime(invite.data.expiresAt)}</span>
            <input readOnly value={invite.data.url} onFocus={(e) => e.target.select()} className={`${fieldClass} font-mono text-xs`} />
          </label>
          <button type="button" onClick={() => copy(invite.data.url)} className={`${secondaryButtonClass} self-start text-sm`}>
            {copied ? "Copied" : "Copy link"}
          </button>
          <p className="text-xs text-muted">It works once, within 7 days, and replaces the trader&apos;s older links. It also resets a forgotten password.</p>
        </div>
      )}
    </div>
  );
}

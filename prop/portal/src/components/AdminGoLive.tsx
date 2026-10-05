"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";

import { useBranding } from "@/app/providers";

import type { Billing, FirmSettings, IdentityReadiness, Verification } from "@/lib/api/types";
import { expansionText, invoiceUrl, monthlyPrices, vatOn, vatText } from "@/lib/billing";
import { formatDate, formatDateTime, formatMoney } from "@/lib/format";
import { goLiveStateLabels, goLiveStates, goLiveStepKeys, goLiveStepOf, goLiveStepTitles, type GoLiveStepKey, type GoLiveStepState } from "@/lib/goLivePage";
import { readinessText } from "@/lib/identity";
import { providerLabels } from "@/lib/orders";
import { useActivate, useBilling, useBillingQuote, useFirmSettings, useSubmitApplication, useVerification } from "@/lib/queries";
import { useDebounced } from "@/lib/useDebounced";
import { applicationFieldLabels, depositDue } from "@/lib/verification";

import { AutoExpandFields, QuoteView, wholeNumber } from "./AdminBilling";
import { ApplicationEditor } from "./Application";
import { CopyButton } from "./CopyButton";
import { AlertIcon, CheckIcon } from "./icons";
import { AdminPage, buttonClass, ErrorText, fieldClass, Message, PageHeader, Panel, secondaryButtonClass } from "./ui";

/** Links from before the Go live page: the way to live in the sandbox, and the company's details once live. */
export function VerificationRedirect() {
  const router = useRouter();
  const { status } = useBranding();
  useEffect(() => {
    router.replace(status === "Live" ? "/admin/billing?tab=company" : "/admin/go-live");
  }, [router, status]);
  return <Message text="Loading..." />;
}

/**
 * The way from the sandbox to live, in four steps (ADR 0021): the company's details, the deposit that sends them, our
 * answer, and the slots and the first payment. Each step says where it is, and the page opens on the one that needs the
 * firm or us. Once live, the firm has Plan and billing instead.
 */
export function AdminGoLive({ step, returnedFromCheckout }: { step: string | null; returnedFromCheckout: boolean }) {
  const router = useRouter();
  const branding = useBranding();
  const [waiting, setWaiting] = useState(returnedFromCheckout);
  const verification = useVerification(waiting);
  const billing = useBilling(waiting);
  const settings = useFirmSettings();
  const live = billing.data?.status === "Live";

  // A firm that went live has nothing more to do here, unless it just paid and is told so.
  useEffect(() => {
    if (live && !waiting) {
      router.replace("/admin/billing");
    }
  }, [live, waiting, router]);

  // A firm that just went live gets its pages again, so they no longer say it is in the sandbox.
  useEffect(() => {
    if (live && branding.status !== "Live") {
      router.refresh();
    }
  }, [live, branding.status, router]);

  if (verification.isError || billing.isError || settings.isError) {
    return <Message text="The way to live cannot be loaded right now. Try again shortly." />;
  }

  if (!verification.data || !billing.data || !settings.data || (live && !waiting)) {
    return <Message text="Loading..." />;
  }

  const v = verification.data;
  const b = billing.data;
  const states = goLiveStates(v, b);
  const current = goLiveStepOf(step ?? undefined, states);
  const go = (next: GoLiveStepKey) => router.push(`/admin/go-live?step=${next}`, { scroll: false });

  return (
    <AdminPage narrow>
      <PageHeader title="Go live" description="Four steps from the sandbox to real traders. We review every firm by hand before it goes live, usually within a day." />
      {waiting && <PaymentConfirmation verification={v} billing={b} onDone={() => setWaiting(false)} />}
      {b.status === "Provisioning" ? (
        <Panel>
          <p role="status" className="text-sm text-muted">
            Your trading server is being set up. This takes a few seconds.
          </p>
        </Panel>
      ) : live ? (
        <LiveDone settings={settings.data} />
      ) : (
        <>
          <Stepper states={states} current={current} onChoose={go} />
          {current === "details" && v.status === "ChangesRequested" && v.message && (
            <div role="status" className="flex flex-col gap-2 rounded-lg border border-warning/40 bg-warning/10 p-4 text-sm">
              <span className="font-medium">We need a few changes before we can approve your firm:</span>
              <span className="whitespace-pre-line">{v.message}</span>
              <span className="text-muted">Make them below, then send the application again. You do not pay the deposit again.</span>
            </div>
          )}
          {current === "details" && <ApplicationEditor key={`${v.status}-${v.canEdit}`} verification={v} onContinue={() => go("deposit")} />}
          {current === "deposit" && <DepositStep verification={v} billing={b} onDetails={() => go("details")} onSent={() => go("answer")} />}
          {current === "answer" && <AnswerStep verification={v} onGo={go} />}
          {current === "payment" && <PaymentStep billing={b} settings={settings.data} identity={v.identity} />}
        </>
      )}
    </AdminPage>
  );
}

const stateTones: Record<GoLiveStepState, string> = {
  done: "border-profit/50 bg-profit/15 text-profit",
  current: "border-accent bg-accent text-accent-foreground",
  todo: "border-border text-muted",
  waiting: "border-border bg-background text-foreground",
  attention: "border-warning/60 bg-warning/15 text-warning",
  locked: "border-border text-muted",
};

/** The four steps side by side, or under each other on a phone, each with where it is. */
function Stepper({ states, current, onChoose }: { states: Record<GoLiveStepKey, GoLiveStepState>; current: GoLiveStepKey; onChoose: (step: GoLiveStepKey) => void }) {
  return (
    <ol aria-label="Steps to go live" className="grid gap-2 sm:grid-cols-4">
      {goLiveStepKeys.map((key, index) => {
        const state = states[key];
        return (
          <li key={key}>
            <button
              type="button"
              aria-current={key === current ? "step" : undefined}
              onClick={() => onChoose(key)}
              className={`flex w-full items-center gap-3 rounded-lg border p-3 text-left ${key === current ? "border-accent bg-panel" : "border-border bg-panel hover:border-muted"}`}
            >
              <span aria-hidden="true" className={`grid size-7 shrink-0 place-items-center rounded-full border text-xs font-semibold ${stateTones[state]}`}>
                {state === "done" ? <CheckIcon className="size-3.5" /> : index + 1}
              </span>
              <span className="flex min-w-0 flex-col">
                <span className="text-sm font-medium">{goLiveStepTitles[key]}</span>
                <span className="text-xs text-muted">{goLiveStateLabels[state]}</span>
              </span>
            </button>
          </li>
        );
      })}
    </ol>
  );
}

/** After a payment page, the provider tells the platform the payment went through. Until then, the page waits. */
function PaymentConfirmation({ verification, billing, onDone }: { verification: Verification; billing: Billing; onDone: () => void }) {
  const live = billing.status === "Live";
  const deposit = verification.deposit.paid && verification.status !== "Approved";
  const confirmed = live || deposit;
  return (
    <p role="status" className={`rounded border px-4 py-2 text-sm ${confirmed ? "border-profit/40 text-profit" : "border-warning/40 text-warning"}`}>
      {!confirmed
        ? "Waiting for the payment to be confirmed..."
        : live
          ? "Thank you. The payment went through, and your firm is live. The receipt is in your email."
          : verification.status === "Draft"
            ? "Thank you. The deposit is paid. Fill in what is missing and send your application."
            : "Thank you. The deposit is paid and your application is sent. The receipt is in your email."}{" "}
      {confirmed && !live && (
        <button type="button" onClick={onDone} className="underline">
          Close
        </button>
      )}
    </p>
  );
}

/** Why the deposit, what it costs with VAT, and the button that pays it and sends the application. */
function DepositStep({ verification, billing, onDetails, onSent }: { verification: Verification; billing: Billing; onDetails: () => void; onSent: () => void }) {
  const submit = useSubmitApplication();
  const { deposit, status } = verification;
  const due = depositDue(verification);
  const vatAmount = vatOn(deposit.amount, billing.vat);
  const money = (amount: number) => `${formatMoney(amount)} ${deposit.currency}`;
  const paid = billing.charges.find((c) => c.kind === "Deposit" && c.status === "Paid");
  const sent = status === "Submitted" || status === "Approved" || status === "Rejected";
  const missing = verification.problems;
  const busy = submit.isPending || (submit.isSuccess && submit.data.checkoutUrl !== null);

  const send = () =>
    submit.mutate(verification.application, {
      onSuccess: (submitted) => {
        if (submitted.checkoutUrl) {
          window.location.assign(submitted.checkoutUrl);
        } else {
          onSent();
        }
      },
    });

  return (
    <Panel title="Deposit">
      {deposit.amount > 0 ? (
        <div className="flex flex-col gap-2 text-sm text-muted">
          <p>
            We review every firm by hand before it goes live: we look the company up in its register, check who owns it and read your terms for
            traders. The deposit of {money(deposit.amount)} pays for that work.
          </p>
          <p>
            It is taken off the startup fee of {money(billing.prices.startupFee)} when you go live. The review is the same work when we cannot
            approve a firm, so then the deposit is not paid back.
          </p>
        </div>
      ) : (
        <p className="text-sm text-muted">There is no deposit. Send your company&apos;s details, and we review them by hand, usually within a day.</p>
      )}

      {due && (
        <table className="w-full max-w-sm text-sm">
          <tbody>
            <tr className="border-t border-border">
              <td className="py-1">Review deposit</td>
              <td className="py-1 text-right font-mono tabular-nums">{formatMoney(deposit.amount)}</td>
            </tr>
            <tr className="border-t border-border text-muted">
              <td className="py-1">{billing.vat.treatment === "ReverseCharge" ? "VAT (reverse charge)" : `VAT ${billing.vat.percent}%`}</td>
              <td className="py-1 text-right font-mono tabular-nums">{formatMoney(vatAmount)}</td>
            </tr>
            <tr className="border-t border-border font-medium">
              <td className="py-1">Total</td>
              <td className="py-1 text-right font-mono tabular-nums">{money(deposit.amount + vatAmount)}</td>
            </tr>
          </tbody>
        </table>
      )}
      {due && <p className="text-xs text-muted">{vatText(billing.vat)} You get the receipt by email at once, and the invoice is here as a PDF.</p>}

      {paid && (
        <p className="text-sm">
          The deposit was paid on {formatDate(paid.paidAt ?? paid.createdAt)}.{" "}
          {paid.invoice && (
            <a href={invoiceUrl(paid)} className="text-accent hover:underline">
              Invoice {paid.invoice} (PDF)
            </a>
          )}
        </p>
      )}

      {sent ? (
        <p className="text-sm text-muted">Your application was sent{verification.submittedAt ? ` on ${formatDateTime(verification.submittedAt)}` : ""}. Our answer is the next step.</p>
      ) : missing.length > 0 ? (
        <div className="flex flex-col items-start gap-2 rounded-lg border border-border bg-background p-3 text-sm">
          <span>Fill in your company&apos;s details first. Still missing: {missing.map((p) => applicationFieldLabels[p.field] ?? p.field).join(", ")}.</span>
          <button type="button" onClick={onDetails} className={secondaryButtonClass}>
            Back to company details
          </button>
        </div>
      ) : (
        <div className="flex flex-col gap-2">
          <button type="button" disabled={busy} onClick={send} className={`${buttonClass} self-start`}>
            {busy
              ? "Sending..."
              : due
                ? `Pay ${money(deposit.amount + vatAmount)} and send for review`
                : status === "ChangesRequested"
                  ? "Send the changes for review"
                  : "Send for review"}
          </button>
          {due && billing.provider === "Test" && <p className="text-xs text-warning">Test payments: you pay on a test page, and no money is taken.</p>}
        </div>
      )}
      <ErrorText error={submit.error} />
    </Panel>
  );
}

/** Where our review is, and our words to the firm. */
function AnswerStep({ verification, onGo }: { verification: Verification; onGo: (step: GoLiveStepKey) => void }) {
  const message = verification.message && (
    <blockquote role="status" className="rounded border-l-4 border-accent bg-background px-4 py-2 text-sm whitespace-pre-line">
      {verification.message}
    </blockquote>
  );
  switch (verification.status) {
    case "Submitted":
      return (
        <Panel title="Our answer">
          <p className="text-sm">We are reviewing your application, usually within a day. We email you when we have decided.</p>
          {verification.submittedAt && <p className="text-xs text-muted">Sent {formatDateTime(verification.submittedAt)}</p>}
        </Panel>
      );
    case "ChangesRequested":
      return (
        <Panel title="Our answer">
          <p className="text-sm">We need a few changes before we can approve your firm:</p>
          {message}
          <p className="text-sm text-muted">Make them and send the application again. You do not pay the deposit again.</p>
          <button type="button" onClick={() => onGo("details")} className={`${buttonClass} self-start`}>
            Make the changes
          </button>
        </Panel>
      );
    case "Approved":
      return (
        <Panel title="Our answer">
          <p className="text-sm text-profit">We have approved your firm{verification.decidedAt ? ` on ${formatDate(verification.decidedAt)}` : ""}.</p>
          {message}
          <button type="button" onClick={() => onGo("payment")} className={`${buttonClass} self-start`}>
            Choose your slots
          </button>
        </Panel>
      );
    case "Rejected":
      return (
        <Panel title="Our answer">
          <p className="text-sm">We have reviewed your firm, and we cannot approve it, so it cannot go live:</p>
          {message}
          <p className="text-sm text-muted">Reply to our email if you have questions.</p>
        </Panel>
      );
    default:
      return (
        <Panel title="Our answer">
          <p className="text-sm text-muted">Send your company&apos;s details first. We answer here and by email, usually within a day.</p>
          <button type="button" onClick={() => onGo("details")} className={`${secondaryButtonClass} self-start`}>
            Company details
          </button>
        </Panel>
      );
  }
}

/** The slots and the first payment, once we have approved the firm and its KYC is set up. Before that, what it will cost. */
function PaymentStep({ billing, settings, identity }: { billing: Billing; settings: FirmSettings; identity: IdentityReadiness }) {
  if (billing.review !== "Approved") {
    const { prices } = billing;
    return (
      <Panel title="Slots and payment">
        <p className="text-sm text-muted">Once we have approved your firm, you choose your slots here and pay. What it costs:</p>
        <ul className="flex list-disc flex-col gap-0.5 pl-5 text-sm">
          {prices.startupFee > 0 && (
            <li>
              Startup fee: {formatMoney(prices.startupFee)} {prices.currency}, once
              {prices.reviewDeposit > 0 && `, less the ${formatMoney(prices.reviewDeposit)} ${prices.currency} deposit`}
            </li>
          )}
          {monthlyPrices(prices).map((price) => (
            <li key={price}>{price}</li>
          ))}
        </ul>
        <p className="text-xs text-muted">{vatText(billing.vat)}</p>
        {identity !== "Ready" && (
          <p className="text-sm text-muted">
            Before you go live, also{" "}
            <Link href="/admin/identity" className="text-accent hover:underline">
              set up KYC
            </Link>
            , how your traders are checked. You can do it now.
          </p>
        )}
      </Panel>
    );
  }

  return <GoLiveForm billing={billing} settings={settings} identity={identity} />;
}

function GoLiveForm({ billing, settings, identity }: { billing: Billing; settings: FirmSettings; identity: IdentityReadiness }) {
  const activate = useActivate();
  const { prices } = billing;
  const [slots, setSlots] = useState(String(prices.packageSlots));
  const [autoExpand, setAutoExpand] = useState(false);
  const [step, setStep] = useState("10");
  const [understood, setUnderstood] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const count = wholeNumber(slots);
  const quote = useBillingQuote(useDebounced(count, 300), useDebounced(autoExpand ? wholeNumber(step) : null, 300));
  const blocked = billing.goLiveProblem !== null;
  const mustConfirm = billing.sandboxAccounts > 0;

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    const autoExpandStep = autoExpand ? wholeNumber(step) : null;
    setProblem(autoExpand && autoExpandStep === null ? "Write the slots to buy at a time as a whole number." : null);
    if (count !== null && (!autoExpand || autoExpandStep !== null)) {
      activate.mutate({ slots: count, autoExpandStep }, { onSuccess: (checkout) => window.location.assign(checkout.checkoutUrl) });
    }
  };

  // A problem with the slots chosen hides the price, which is not what would be paid.
  const choiceProblem = quote.data?.problem && quote.data.problem !== billing.goLiveProblem ? quote.data.problem : null;
  return (
    <form onSubmit={submit} className="flex flex-col gap-6">
      <Panel title="Slots and payment">
        <p className="text-sm text-muted">
          Choose how many challenges you want open at once. Each one takes a slot from when it starts until it ends. The package includes {prices.packageSlots}{" "}
          slots, and you can add more. You pay the startup fee less the deposit and the rest of this month now, and each month in advance from then on,
          with the card you pay with now.
        </p>
        <ul className="text-sm">
          {monthlyPrices(prices).map((price) => (
            <li key={price}>{price}</li>
          ))}
        </ul>
        <div className="flex flex-wrap items-end gap-4">
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-muted">Slots</span>
            <input aria-label="Slots" inputMode="numeric" required value={slots} onChange={(e) => setSlots(e.target.value)} className={`${fieldClass} w-28 text-right`} />
          </label>
          <AutoExpandFields enabled={autoExpand} step={step} onEnabled={setAutoExpand} onStep={setStep} />
        </div>
        {autoExpand && quote.data?.expansion && <p className="text-sm text-muted">{expansionText(quote.data.expansion, quote.data.currency)}</p>}
        <QuoteView quote={quote.data} sameAs={billing.goLiveProblem} />
        <p className="text-xs text-muted">{vatText(billing.vat)}</p>
      </Panel>

      <Panel title="Before you pay">
        <ul className="flex flex-col gap-3 text-sm">
          <Check ok={billing.sandboxAccounts === 0}>
            {billing.sandboxAccounts === 0
              ? "You have no test accounts that end."
              : `${billing.sandboxAccounts === 1 ? "1 test account" : `${billing.sandboxAccounts} test accounts`} from the sandbox ${billing.sandboxAccounts === 1 ? "ends" : "end"}, with any open trades, and ${billing.sandboxAccounts === 1 ? "its trader gets" : "their traders get"} an email.`}
          </Check>
          <Check ok={billing.shopProblem === null} link={billing.shopProblem ? { href: "/admin/checkout", label: "Set up checkout" } : undefined}>
            {billing.shopProblem ??
              (settings.payments.provider === null
                ? "You do not sell in the portal, so its shop stays closed."
                : `Your shop takes real payments with ${providerLabels[settings.payments.provider]} from the moment you are live.`)}
          </Check>
          <Check ok={identity === "Ready"} link={{ href: "/admin/identity", label: "Set up KYC" }}>
            {readinessText(identity, false) ?? "Your KYC is set up, so your traders are checked as you chose."}
          </Check>
          <Check ok={settings.logoUrl !== null || Object.keys(settings.colors).length > 0} link={{ href: "/admin/design", label: "Portal design" }}>
            {settings.logoUrl !== null || Object.keys(settings.colors).length > 0
              ? "Your portal has your logo and colors."
              : "Your portal has no logo or colors of yours yet. Your traders see your firm's name."}
          </Check>
          <Check ok={settings.payments.termsUrl !== null} link={{ href: "/admin/checkout", label: "Checkout" }}>
            {settings.payments.termsUrl ? `Buyers accept your terms at ${settings.payments.termsUrl}.` : "Buyers accept no terms of yours in the shop."}
          </Check>
        </ul>
        {mustConfirm && (
          <label className="flex items-start gap-2 text-sm">
            <input type="checkbox" checked={understood} onChange={(e) => setUnderstood(e.target.checked)} className="mt-1" />
            <span>I understand that the test accounts end when we go live.</span>
          </label>
        )}
        <ErrorText error={problem ? new Error(problem) : (activate.error ?? quote.error)} />
        <button
          type="submit"
          disabled={blocked || (mustConfirm && !understood) || !quote.data || choiceProblem !== null || activate.isPending || activate.isSuccess}
          className={`${buttonClass} self-start`}
        >
          {activate.isPending || activate.isSuccess
            ? "Going to payment..."
            : quote.data && !choiceProblem
              ? `Pay ${formatMoney(quote.data.amount)} ${quote.data.currency} and go live`
              : "Pay and go live"}
        </button>
        {billing.provider === "Test" && <p className="text-xs text-warning">Test payments: you pay on a test page, and no money is taken.</p>}
      </Panel>
    </form>
  );
}

/** One thing to know before paying: fine, or worth a look, with where to change it. */
function Check({ ok, link, children }: { ok: boolean; link?: { href: string; label: string }; children: React.ReactNode }) {
  return (
    <li className="flex gap-2.5">
      {ok ? <CheckIcon className="mt-0.5 size-4 shrink-0 text-profit" /> : <AlertIcon className="mt-0.5 size-4 shrink-0 text-warning" />}
      <span className="flex flex-col gap-0.5">
        <span>{children}</span>
        {link && !ok && (
          <Link href={link.href} className="text-accent hover:underline">
            {link.label}
          </Link>
        )}
      </span>
    </li>
  );
}

/** Just paid and live: what happens now. */
function LiveDone({ settings }: { settings: FirmSettings }) {
  const shop = new URL("buy", settings.portalUrl).toString();
  return (
    <Panel title="Your firm is live">
      <p className="text-sm">
        {settings.payments.provider ? "Your shop takes real payments from now on, and new" : "New"} challenges count against your slots. We have emailed you the
        receipt.
      </p>
      <ol className="flex list-decimal flex-col gap-2 pl-5 text-sm">
        <li>
          {settings.payments.provider ? (
            <>
              Share your shop with your traders: <span className="font-mono">{shop}</span> <CopyButton value={shop} label="Copy the shop's address" />
            </>
          ) : (
            "Start challenges for your traders from your own systems, or under Accounts."
          )}
        </li>
        <li>Follow your traders under Accounts, and approve their funded accounts and payouts.</li>
        <li>Your slots, charges and invoices are under Plan and billing. Each month is charged to your card some days before it starts.</li>
      </ol>
      <Link href="/admin/billing" className={`${buttonClass} self-start`}>
        Plan and billing
      </Link>
    </Panel>
  );
}

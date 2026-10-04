"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";

import { useBranding } from "@/app/providers";

import type { Billing, Charge, ChargeLine, Quote } from "@/lib/api/types";
import { cardLabel, chargeKindLabels, chargeLabel, chargeStatusLabels, monthName, monthlyPrices, slotsSummary, slotsTaken, unpaidCharges } from "@/lib/billing";
import { formatDate, formatDateTime, formatMoney } from "@/lib/format";
import {
  useActivate,
  useBilling,
  useBillingQuote,
  useChangeCard,
  usePayCharge,
  useRetryCharge,
  useSetAutoExpand,
  useSetSlots,
} from "@/lib/queries";
import { useDebounced } from "@/lib/useDebounced";
import { reviewStatusLabels } from "@/lib/verification";

import { buttonClass, ErrorText, fieldClass, Message, Panel, secondaryButtonClass } from "./ui";

/**
 * What the firm pays us (ADR 0020). In the sandbox, the firm goes live by paying the startup fee and its first
 * month. Live, it sees its slots, buys more or fewer, changes its card and pays a month that was declined.
 */
export function AdminBilling({ returnedFromCheckout }: { returnedFromCheckout: boolean }) {
  const [waiting, setWaiting] = useState(returnedFromCheckout);
  const billing = useBilling(waiting);

  if (billing.isError) {
    return <Message text="The billing cannot be loaded right now. Try again shortly." />;
  }

  if (!billing.data) {
    return <Message text="Loading..." />;
  }

  const data = billing.data;
  return (
    <main className="mx-auto flex w-full max-w-4xl flex-col gap-6 p-6">
      {waiting && <PaymentConfirmation billing={data} onDone={() => setWaiting(false)} />}
      {data.status !== "Live" ? (
        <GoLive billing={data} />
      ) : data.plan === "Paid" ? (
        <>
          <UnpaidCharges billing={data} />
          <SlotsPanel billing={data} />
          <AutoExpand key={data.autoExpandStep ?? "off"} billing={data} />
          <Payment billing={data} />
          <Charges charges={data.charges} />
        </>
      ) : (
        <Panel title="Slots">
          <SlotsUsage billing={data} />
          <p className="text-sm text-muted">Your slots are complimentary, so there is nothing to pay.</p>
        </Panel>
      )}
    </main>
  );
}

/**
 * After the payment page, the provider tells the platform the payment went through. Until then, the page waits.
 * A firm that just went live gets its pages again, so they no longer say it is a test environment.
 */
function PaymentConfirmation({ billing, onDone }: { billing: Billing; onDone: () => void }) {
  const router = useRouter();
  const branding = useBranding();
  const latest = billing.charges[0];
  const confirmed = billing.status === "Live" && (!latest || latest.status !== "Pending");

  useEffect(() => {
    if (billing.status === "Live" && branding.status !== "Live") {
      router.refresh();
    }
  }, [billing.status, branding.status, router]);

  return (
    <p role="status" className={`rounded border px-4 py-2 text-sm ${confirmed ? "border-profit/40 text-profit" : "border-warning/40 text-warning"}`}>
      {confirmed ? "Thank you. The payment went through." : "Waiting for the payment to be confirmed..."}{" "}
      {confirmed && (
        <button type="button" onClick={onDone} className="underline">
          Close
        </button>
      )}
    </p>
  );
}

function GoLive({ billing }: { billing: Billing }) {
  const activate = useActivate();
  const prices = billing.prices;
  const [slots, setSlots] = useState(String(prices.packageSlots));
  const [autoExpand, setAutoExpand] = useState(false);
  const [step, setStep] = useState("10");
  const [problem, setProblem] = useState<string | null>(null);
  const count = wholeNumber(slots);
  const quote = useBillingQuote(useDebounced(count, 300));

  if (billing.status === "Provisioning") {
    return (
      <Panel title="Go live">
        <p role="status" className="text-sm text-muted">
          Your trading server is being set up. This takes a few seconds.
        </p>
      </Panel>
    );
  }

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    const autoExpandStep = autoExpand ? wholeNumber(step) : null;
    setProblem(autoExpand && autoExpandStep === null ? "Write the slots to buy at a time as a whole number." : null);
    if (count !== null && (!autoExpand || autoExpandStep !== null)) {
      activate.mutate({ slots: count, autoExpandStep }, { onSuccess: (checkout) => window.location.assign(checkout.checkoutUrl) });
    }
  };

  return (
    <Panel title="Go live">
      {billing.review !== "Approved" && <ReviewSteps billing={billing} />}
      <p className="text-sm text-muted">
        Choose how many challenges you want open at once. Each one takes a slot from when it starts until it ends. The package includes{" "}
        {prices.packageSlots} slots, and you can add more. You pay the startup fee and the rest of this month now, and each month in advance from
        then on. Your card is saved for that. Your test accounts from the sandbox end when you go live.
      </p>
      <ul className="text-sm">
        {prices.startupFee > 0 && (
          <li>
            Startup fee: {formatMoney(prices.startupFee)} {prices.currency}, once
            {billing.depositPaid > 0
              ? `, less the ${formatMoney(billing.depositPaid)} ${prices.currency} deposit you paid`
              : prices.reviewDeposit > 0 && `, of which ${formatMoney(prices.reviewDeposit)} ${prices.currency} is a deposit paid when you send your application`}
          </li>
        )}
        {monthlyPrices(prices).map((price) => (
          <li key={price}>{price}</li>
        ))}
      </ul>
      <form onSubmit={submit} className="flex flex-col gap-4">
        <div className="flex flex-wrap items-end gap-4">
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-muted">Slots</span>
            <input aria-label="Slots" inputMode="numeric" required value={slots} onChange={(e) => setSlots(e.target.value)} className={`${fieldClass} w-28 text-right`} />
          </label>
          <AutoExpandFields enabled={autoExpand} step={step} onEnabled={setAutoExpand} onStep={setStep} />
        </div>
        <QuoteView quote={quote.data} />
        {/* The price says the same as the billing when the firm cannot go live, so it is said once. */}
        <ErrorText
          error={
            billing.goLiveProblem && quote.data?.problem !== billing.goLiveProblem
              ? new Error(billing.goLiveProblem)
              : problem
                ? new Error(problem)
                : (activate.error ?? quote.error)
          }
        />
        <button
          type="submit"
          disabled={billing.goLiveProblem !== null || !quote.data || quote.data.problem !== null || activate.isPending || activate.isSuccess}
          className={`${buttonClass} self-start`}
        >
          {activate.isPending || activate.isSuccess ? "Going to payment..." : quote.data ? `Pay ${formatMoney(quote.data.amount)} ${quote.data.currency} and go live` : "Pay and go live"}
        </button>
        {billing.provider === "Test" && <p className="text-xs text-warning">Test payments: you pay on a test page, and no money is taken.</p>}
      </form>
    </Panel>
  );
}

/** The way to live before we have approved the firm, and where it is on that way. */
function ReviewSteps({ billing }: { billing: Billing }) {
  return (
    <ol className="flex list-decimal flex-col gap-1 pl-5 text-sm">
      <li>
        Send your company&apos;s details for review under{" "}
        <Link href="/admin/verification" className="text-accent hover:underline">
          Verification
        </Link>
        {billing.review && <span className="text-muted"> ({reviewStatusLabels[billing.review]})</span>}.
      </li>
      <li>We review your firm, usually within a day, and email you.</li>
      <li>Choose your slots below and pay to go live.</li>
    </ol>
  );
}

/** What a choice of slots costs: the lines paid now, and the price of each month from then on. */
function QuoteView({ quote }: { quote: Quote | undefined }) {
  if (!quote) {
    return null;
  }

  return (
    <div className="flex flex-col gap-2 text-sm">
      {quote.lines.length > 0 && <ChargeLines lines={quote.lines} total={quote.amount} currency={quote.currency} />}
      {quote.problem ? (
        <p role="alert" className="text-loss">
          {quote.problem}
        </p>
      ) : (
        quote.from && (
          <p className="text-muted">
            Then {formatMoney(quote.monthlyPrice)} {quote.currency} a month for {quote.slots} slots, from {monthName(quote.from)}.
          </p>
        )
      )}
    </div>
  );
}

function ChargeLines({ lines, total, currency }: { lines: ChargeLine[]; total: number; currency: string }) {
  return (
    <table className="w-full max-w-lg text-sm">
      <tbody>
        {lines.map((line) => (
          <tr key={line.description} className="border-t border-border">
            <td className="py-1">{line.description}</td>
            <td className="py-1 text-right font-mono tabular-nums">{formatMoney(line.amount)}</td>
          </tr>
        ))}
        <tr className="border-t border-border font-medium">
          <td className="py-1">Total</td>
          <td className="py-1 text-right font-mono tabular-nums">
            {formatMoney(total)} {currency}
          </td>
        </tr>
      </tbody>
    </table>
  );
}

function SlotsUsage({ billing }: { billing: Billing }) {
  const taken = slotsTaken(billing.slots);
  return (
    <div className="flex flex-col gap-2 text-sm">
      <p>{slotsSummary(billing.slots)}</p>
      {taken !== null && (
        <div
          role="progressbar"
          aria-label="Slots taken"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.round(taken)}
          className="h-2 overflow-hidden rounded bg-background"
        >
          <div className={`h-full ${billing.slots.warning ? "bg-warning" : "bg-accent"}`} style={{ width: `${taken}%` }} />
        </div>
      )}
    </div>
  );
}

/** The slots now and from next month, and the form that changes them. */
function SlotsPanel({ billing }: { billing: Billing }) {
  const setSlots = useSetSlots();
  const [slots, setValue] = useState(String(billing.nextMonthSlots ?? billing.slots.slots ?? ""));
  const count = wholeNumber(slots);
  const quote = useBillingQuote(useDebounced(count, 300));
  const kind = quote.data?.kind;

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    if (count !== null) {
      setSlots.mutate(count);
    }
  };

  return (
    <Panel title="Slots">
      <SlotsUsage billing={billing} />
      {billing.nextMonthSlots !== null && billing.nextMonthSlots !== billing.slots.slots && (
        <p className="text-sm text-muted">From the next unpaid month: {billing.nextMonthSlots} slots.</p>
      )}
      <form onSubmit={submit} className="flex flex-col gap-3 border-t border-border pt-4">
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted">Change your slots</span>
          <input aria-label="New number of slots" inputMode="numeric" required value={slots} onChange={(e) => setValue(e.target.value)} className={`${fieldClass} w-28 text-right`} />
        </label>
        <p className="text-xs text-muted">More slots are paid now for the rest of the month. Fewer apply from the next month that is not paid yet.</p>
        <QuoteView quote={quote.data} />
        <ErrorText error={setSlots.error ?? quote.error} />
        {setSlots.isSuccess && <p className="text-sm text-profit">Saved.</p>}
        <button
          type="submit"
          disabled={!quote.data || quote.data.problem !== null || kind === "Unchanged" || setSlots.isPending}
          className={`${buttonClass} self-start`}
        >
          {setSlots.isPending
            ? "Saving..."
            : kind === "MoreSlots" && quote.data
              ? `Buy now for ${formatMoney(quote.data.amount)} ${quote.data.currency}`
              : "Save"}
        </button>
      </form>
    </Panel>
  );
}

function AutoExpand({ billing }: { billing: Billing }) {
  const save = useSetAutoExpand();
  const [enabled, setEnabled] = useState(billing.autoExpandStep !== null);
  const [step, setStep] = useState(String(billing.autoExpandStep ?? 10));
  const [problem, setProblem] = useState<string | null>(null);

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    const value = enabled ? wholeNumber(step) : null;
    setProblem(enabled && value === null ? "Write the slots to buy at a time as a whole number." : null);
    if (!enabled || value !== null) {
      save.mutate(value);
    }
  };

  return (
    <Panel title="Automatic expansion">
      <p className="text-sm text-muted">
        When the last free slot is taken, more are bought at once with your card, so new traders never wait. They are paid for the rest of the month,
        and kept from then on.
      </p>
      <form onSubmit={submit} className="flex flex-wrap items-end gap-4">
        <AutoExpandFields enabled={enabled} step={step} onEnabled={setEnabled} onStep={setStep} />
        <button type="submit" disabled={save.isPending} className={secondaryButtonClass}>
          Save
        </button>
      </form>
      {save.isSuccess && <p className="text-sm text-profit">Saved.</p>}
      <ErrorText error={problem ? new Error(problem) : save.error} />
    </Panel>
  );
}

function AutoExpandFields({
  enabled,
  step,
  onEnabled,
  onStep,
}: {
  enabled: boolean;
  step: string;
  onEnabled: (enabled: boolean) => void;
  onStep: (step: string) => void;
}) {
  return (
    <>
      <label className="flex items-center gap-2 py-2 text-sm">
        <input type="checkbox" checked={enabled} onChange={(e) => onEnabled(e.target.checked)} />
        Buy more slots when the last is taken
      </label>
      {enabled && (
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted">Slots at a time</span>
          <input aria-label="Slots at a time" inputMode="numeric" required value={step} onChange={(e) => onStep(e.target.value)} className={`${fieldClass} w-24 text-right`} />
        </label>
      )}
    </>
  );
}

/** The saved card and the next monthly payment. */
function Payment({ billing }: { billing: Billing }) {
  const changeCard = useChangeCard();
  const next = billing.nextCharge;
  return (
    <Panel title="Payment">
      <dl className="grid gap-x-6 gap-y-3 text-sm sm:grid-cols-2">
        <div className="flex flex-col gap-0.5">
          <dt className="text-muted">Card</dt>
          <dd>{billing.card ? cardLabel(billing.card) : "None"}</dd>
        </div>
        {next && (
          <div className="flex flex-col gap-0.5">
            <dt className="text-muted">Next payment</dt>
            <dd>
              {monthName(next.month)}: {next.slots} slots, {formatMoney(next.amount)} {billing.prices.currency}, charged on {formatDate(next.chargeAt)}
            </dd>
          </div>
        )}
      </dl>
      <button
        type="button"
        disabled={changeCard.isPending || changeCard.isSuccess}
        onClick={() => changeCard.mutate(undefined, { onSuccess: (checkout) => window.location.assign(checkout.checkoutUrl) })}
        className={`${secondaryButtonClass} self-start`}
      >
        {changeCard.isPending || changeCard.isSuccess ? "Going to the card page..." : "Change card"}
      </button>
      <ErrorText error={changeCard.error} />
      {billing.provider === "Test" && <p className="text-xs text-warning">Test payments: no money is taken.</p>}
    </Panel>
  );
}

/** A month whose payment was declined, with the ways to pay it. */
function UnpaidCharges({ billing }: { billing: Billing }) {
  const charges = unpaidCharges(billing);
  if (charges.length === 0) {
    return null;
  }

  return (
    <Panel title="Payment needed">
      {billing.unpaidSince && (
        <p role="alert" className="text-sm text-loss">
          This month is not paid. No new challenges can start, and your traders&apos; accounts are paused until it is.
        </p>
      )}
      <ul className="flex flex-col gap-4">
        {charges.map((charge) => (
          <UnpaidCharge key={charge.id} charge={charge} />
        ))}
      </ul>
    </Panel>
  );
}

function UnpaidCharge({ charge }: { charge: Charge }) {
  const retry = useRetryCharge();
  const pay = usePayCharge();
  return (
    <li className="flex flex-col gap-2 text-sm">
      <p>
        {chargeLabel(charge)}: {formatMoney(charge.amount)} {charge.currency}.{charge.failure && ` The card was declined: ${charge.failure}`}
        {charge.nextAttemptAt && ` We try again on ${formatDate(charge.nextAttemptAt)}.`}
      </p>
      <div className="flex flex-wrap gap-3">
        <button type="button" disabled={retry.isPending} onClick={() => retry.mutate(charge.id)} className={secondaryButtonClass}>
          {retry.isPending ? "Trying..." : "Try the card again"}
        </button>
        <button
          type="button"
          disabled={pay.isPending || pay.isSuccess}
          onClick={() => pay.mutate(charge.id, { onSuccess: (checkout) => window.location.assign(checkout.checkoutUrl) })}
          className={buttonClass}
        >
          {pay.isPending || pay.isSuccess ? "Going to payment..." : "Pay with another card"}
        </button>
      </div>
      <ErrorText error={retry.error ?? pay.error} />
    </li>
  );
}

function Charges({ charges }: { charges: Charge[] }) {
  return (
    <Panel title="Charges">
      {charges.length === 0 ? (
        <p className="text-sm text-muted">No charges yet.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="text-left text-muted">
              <tr>
                <th className="py-2 font-normal">Charge</th>
                <th className="py-2 font-normal">For</th>
                <th className="py-2 text-right font-normal">Slots</th>
                <th className="py-2 text-right font-normal">Amount</th>
                <th className="py-2 pl-6 font-normal">Status</th>
                <th className="py-2 text-right font-normal">Date</th>
              </tr>
            </thead>
            <tbody>
              {charges.map((charge) => (
                <tr key={charge.id} className="border-t border-border">
                  <td className="py-2">
                    #{charge.number} <span className="text-muted">{chargeKindLabels[charge.kind]}</span>
                  </td>
                  <td className="py-2">{chargeLabel(charge)}</td>
                  <td className="py-2 text-right font-mono tabular-nums">{charge.slots}</td>
                  <td className="py-2 text-right font-mono tabular-nums">
                    {formatMoney(charge.amount)} {charge.currency}
                  </td>
                  <td className="py-2 pl-6">{chargeStatusLabels[charge.status]}</td>
                  <td className="py-2 text-right text-muted">{formatDateTime(charge.paidAt ?? charge.createdAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Panel>
  );
}

/** A whole number typed in a field, or null. */
function wholeNumber(text: string): number | null {
  const value = Number(text.trim());
  return text.trim() !== "" && Number.isInteger(value) && value >= 0 ? value : null;
}

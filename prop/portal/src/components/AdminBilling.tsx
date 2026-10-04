"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";

import type { Billing, Charge, ChargeLine, Quote, Vat } from "@/lib/api/types";
import {
  cardLabel,
  chargeKindLabels,
  chargeLabel,
  chargeStatusLabels,
  expansionText,
  invoiceUrl,
  monthName,
  slotsSummary,
  slotsTaken,
  unpaidCharges,
  vatText,
} from "@/lib/billing";
import { formatDate, formatDateTime, formatMoney } from "@/lib/format";
import { useBilling, useBillingQuote, useChangeCard, usePayCharge, useRetryCharge, useSetAutoExpand, useSetSlots, useVerification } from "@/lib/queries";
import { useDebounced } from "@/lib/useDebounced";

import { CompanyDetails } from "./Application";
import { AdminPage, buttonClass, ErrorText, fieldClass, Message, PageHeader, Panel, secondaryButtonClass, Tabs } from "./ui";

type BillingTab = "billing" | "company";

/**
 * What a live firm pays us (ADR 0020): its slots, more or fewer of them, automatic expansion, its card, a month that was
 * declined, and the charges with their invoices. Its company details, as we approved them, are under a tab of their own.
 * A firm in the sandbox has the way to live under Go live instead.
 */
export function AdminBilling({ returnedFromCheckout, tab }: { returnedFromCheckout: boolean; tab: BillingTab }) {
  const router = useRouter();
  const [waiting, setWaiting] = useState(returnedFromCheckout);
  const billing = useBilling(waiting);
  const sandbox = billing.data !== undefined && billing.data.status !== "Live";

  useEffect(() => {
    if (sandbox) {
      router.replace("/admin/go-live");
    }
  }, [sandbox, router]);

  if (billing.isError) {
    return <Message text="The billing cannot be loaded right now. Try again shortly." />;
  }

  if (!billing.data || sandbox) {
    return <Message text="Loading..." />;
  }

  const data = billing.data;
  return (
    <AdminPage narrow>
      <PageHeader title="Plan and billing" description="What you pay us: your slots, your card and your charges. A slot is one open challenge, from when it starts until it ends." />
      <Tabs
        label="Plan and billing"
        tabs={[
          { value: "billing", label: "Billing" },
          { value: "company", label: "Company details" },
        ]}
        value={tab}
        onChange={(next) => router.replace(next === "billing" ? "/admin/billing" : "/admin/billing?tab=company", { scroll: false })}
      />
      {tab === "company" ? (
        <ApprovedCompany />
      ) : (
        <>
          {waiting && <PaymentConfirmation billing={data} onDone={() => setWaiting(false)} />}
          {data.plan === "Paid" ? (
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
        </>
      )}
    </AdminPage>
  );
}

function ApprovedCompany() {
  const verification = useVerification();
  if (verification.isError) {
    return <Message text="Your company details cannot be loaded right now. Try again shortly." />;
  }

  return verification.data ? <CompanyDetails verification={verification.data} /> : <Message text="Loading..." />;
}

/** After a payment page, the provider tells the platform the payment went through. Until then, the page waits. */
function PaymentConfirmation({ billing, onDone }: { billing: Billing; onDone: () => void }) {
  const latest = billing.charges[0];
  const confirmed = !latest || latest.status !== "Pending";
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

/**
 * What a choice of slots costs: the lines paid now with the VAT and the total, and the price of each month from then on.
 * A problem with the choice is said instead of the price; one the same as the firm's, such as waiting for our review,
 * keeps the price.
 */
export function QuoteView({ quote, sameAs = null }: { quote: Quote | undefined; sameAs?: string | null }) {
  if (!quote) {
    return null;
  }

  const priced = !quote.problem || quote.problem === sameAs;
  return (
    <div className="flex flex-col gap-2 text-sm">
      {priced && quote.lines.length > 0 && <ChargeLines lines={quote.lines} vat={quote.vat} vatAmount={quote.vatAmount} total={quote.amount} currency={quote.currency} />}
      {quote.problem ? (
        <p role="alert" className="text-loss">
          {quote.problem}
        </p>
      ) : (
        quote.from && (
          <p className="text-muted">
            Then {formatMoney(quote.monthlyPrice)} {quote.currency} a month without VAT for {quote.slots} slots, from {monthName(quote.from)}.
          </p>
        )
      )}
    </div>
  );
}

/** The lines of a charge, without VAT, then the VAT and the total paid. */
export function ChargeLines({ lines, vat, vatAmount, total, currency }: { lines: ChargeLine[]; vat: Vat; vatAmount: number; total: number; currency: string }) {
  return (
    <table className="w-full max-w-lg text-sm">
      <tbody>
        {lines.map((line) => (
          <tr key={line.description} className="border-t border-border">
            <td className="py-1">{line.description}</td>
            <td className="py-1 text-right font-mono tabular-nums">{formatMoney(line.amount)}</td>
          </tr>
        ))}
        <tr className="border-t border-border text-muted">
          <td className="py-1">{vat.treatment === "ReverseCharge" ? "VAT (reverse charge)" : `VAT ${vat.percent}%`}</td>
          <td className="py-1 text-right font-mono tabular-nums">{formatMoney(vatAmount)}</td>
        </tr>
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

export function SlotsUsage({ billing }: { billing: Billing }) {
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
            : kind === "MoreSlots" && quote.data && !quote.data.problem
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
  const expansion = useBillingQuote(billing.slots.slots, useDebounced(enabled ? wholeNumber(step) : null, 300)).data?.expansion;

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
      {enabled && expansion && <p className="text-sm text-muted">{expansionText(expansion, billing.prices.currency)}</p>}
      {save.isSuccess && <p className="text-sm text-profit">Saved.</p>}
      <ErrorText error={problem ? new Error(problem) : save.error} />
    </Panel>
  );
}

export function AutoExpandFields({
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
              {monthName(next.month)}: {next.slots} slots, {formatMoney(next.amount)} {billing.prices.currency}
              {next.vatAmount > 0 && ` with ${formatMoney(next.vatAmount)} VAT`}, charged on {formatDate(next.chargeAt)}
            </dd>
          </div>
        )}
      </dl>
      <p className="text-xs text-muted">{vatText(billing.vat)}</p>
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

/** The firm's charges, with the invoice of each paid one. */
export function Charges({ charges, title = "Charges" }: { charges: Charge[]; title?: string }) {
  return (
    <Panel title={title}>
      {charges.length === 0 ? (
        <p className="text-sm text-muted">No charges yet.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="text-left text-muted">
              <tr>
                <th className="py-2 font-normal">Charge</th>
                <th className="py-2 font-normal">For</th>
                <th className="py-2 text-right font-normal">Amount</th>
                <th className="py-2 pl-6 font-normal">Status</th>
                <th className="py-2 text-right font-normal">Date</th>
                <th className="py-2 pl-6 font-normal">Invoice</th>
              </tr>
            </thead>
            <tbody>
              {charges.map((charge) => (
                <tr key={charge.id} className="border-t border-border">
                  <td className="py-2 whitespace-nowrap">
                    #{charge.number} <span className="text-muted">{chargeKindLabels[charge.kind]}</span>
                  </td>
                  <td className="py-2">{chargeLabel(charge)}</td>
                  <td className="py-2 text-right font-mono whitespace-nowrap tabular-nums">
                    {formatMoney(charge.amount)} {charge.currency}
                    {charge.vatAmount > 0 && <span className="block font-sans text-xs text-muted">incl. {formatMoney(charge.vatAmount)} VAT</span>}
                  </td>
                  <td className="py-2 pl-6">{chargeStatusLabels[charge.status]}</td>
                  <td className="py-2 text-right whitespace-nowrap text-muted">{formatDateTime(charge.paidAt ?? charge.createdAt)}</td>
                  <td className="py-2 pl-6 whitespace-nowrap">
                    {charge.invoice ? (
                      <a href={invoiceUrl(charge)} className="text-accent hover:underline">
                        {charge.invoice} (PDF)
                      </a>
                    ) : (
                      <span className="text-muted">-</span>
                    )}
                  </td>
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
export function wholeNumber(text: string): number | null {
  const value = Number(text.trim());
  return text.trim() !== "" && Number.isInteger(value) && value >= 0 ? value : null;
}

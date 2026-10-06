"use client";

import Link from "next/link";
import { useEffect, useState } from "react";

import { formatTotals, passRateText } from "@/lib/admin";
import type { OpsFirm as Firm } from "@/lib/api/types";
import { cardLabel, chargeAmountText, chargeLabel, chargeStatusLabels, monthName } from "@/lib/billing";
import { formatDate, formatDateTime, formatMoney } from "@/lib/format";
import { daysSince, latePayouts, mailtoAdmins, opsFirmTabs, stageOf, stageViews, type OpsFirmTab } from "@/lib/ops";
import { useOpsAction, useOpsFirm } from "@/lib/opsQueries";
import { eventText } from "@/lib/verification";

import { Modal } from "./Dialog";
import { ClockIcon, ExternalIcon, MailIcon, PauseIcon } from "./icons";
import { OpsReview } from "./OpsReview";
import { AdminPage, Badge, buttonClass, dangerButtonClass, ErrorText, fieldClass, Message, PageHeader, Panel, secondaryButtonClass, StatTile, Tabs } from "./ui";

/**
 * One firm for our staff (ADRs 0021 and 0024): its review with our checks, how it is doing and pays its traders, what
 * it pays us, its team and its history, and its suspension.
 */
export function OpsFirm({ firmId, initialTab }: { firmId: string; initialTab: OpsFirmTab | null }) {
  const firm = useOpsFirm(firmId);
  const [chosen, setChosen] = useState(initialTab);

  if (firm.isError) {
    return <Message text="This firm cannot be found." />;
  }

  if (!firm.data) {
    return <Message text="Loading..." />;
  }

  const data = firm.data;
  // A firm we set up ourselves was never reviewed. A firm in the sandbox opens on its review, a live one on how it is doing.
  const tabs = opsFirmTabs.filter((t) => t !== "review" || !data.configured);
  const tab = chosen && tabs.includes(chosen) ? chosen : data.configured || data.status === "Live" ? "overview" : "review";
  return <FirmPage firm={data} tab={tab} tabs={tabs} onTab={setChosen} now={firm.dataUpdatedAt} />;
}

const tabLabels: Record<OpsFirmTab, string> = { review: "Review", overview: "Overview", billing: "Billing", team: "Team", history: "History" };

function FirmPage({ firm, tab, tabs, onTab, now }: { firm: Firm; tab: OpsFirmTab; tabs: OpsFirmTab[]; onTab: (tab: OpsFirmTab) => void; now: number }) {
  const [suspending, setSuspending] = useState(false);
  const stage = stageViews[stageOf(firm)];

  // The address keeps the tab, so going back or sharing it opens the same part.
  useEffect(() => {
    window.history.replaceState(null, "", `/ops/firms/${encodeURIComponent(firm.id)}?tab=${tab}`);
  }, [firm.id, tab]);

  return (
    <AdminPage>
      <PageHeader
        back={
          <nav aria-label="Breadcrumb" className="text-sm text-muted">
            <Link href="/ops/firms" className="hover:text-foreground">
              Firms
            </Link>{" "}
            / <span className="text-foreground">{firm.name}</span>
          </nav>
        }
        title={firm.name}
        description={
          <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
            <Badge tone={stage.tone}>{stage.label}</Badge>
            <span>{summaryLine(firm)}</span>
          </span>
        }
        actions={
          <>
            <a href={firm.portalUrl} target="_blank" rel="noopener" className={`${secondaryButtonClass} flex items-center gap-2`}>
              <ExternalIcon className="size-4" />
              Open their portal
            </a>
            {!firm.suspension && (
              <button type="button" onClick={() => setSuspending(true)} className={`${secondaryButtonClass} flex items-center gap-2 border-loss/40 text-loss`}>
                <PauseIcon className="size-4" />
                Suspend
              </button>
            )}
          </>
        }
      />

      {firm.suspension && <SuspendedBanner firm={firm} />}
      <LatePayoutsBanner firm={firm} now={now} />

      <div className="flex flex-col gap-5">
        <Tabs label="The firm" tabs={tabs.map((t) => ({ value: t, label: tabLabels[t], count: t === "history" ? firm.events.length : undefined }))} value={tab} onChange={onTab} />
        <div role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`}>
          {tab === "review" && <OpsReview firm={firm} />}
          {tab === "overview" && <Overview firm={firm} onTab={onTab} />}
          {tab === "billing" && <Billing firm={firm} />}
          {tab === "team" && <Team firm={firm} />}
          {tab === "history" && <History firm={firm} />}
        </div>
      </div>

      <SuspendDialog firm={firm} open={suspending} onClose={() => setSuspending(false)} />
    </AdminPage>
  );
}

/** The firm in a line, for example "acme · signed up 12 Sep 2026 · live since 3 Mar 2026 · 2 administrators". */
function summaryLine(firm: Firm): string {
  return [
    firm.id,
    firm.configured ? "set up by us" : `signed up ${formatDate(firm.createdAt)}`,
    firm.billing.activatedAt ? `live since ${formatDate(firm.billing.activatedAt)}` : firm.submittedAt && firm.status !== "Live" ? `application sent ${formatDateTime(firm.submittedAt)}` : null,
    `${firm.admins.length} ${firm.admins.length === 1 ? "administrator" : "administrators"}`,
  ]
    .filter(Boolean)
    .join(" · ");
}

/** A suspended firm says why, with the way to lift it. */
function SuspendedBanner({ firm }: { firm: Firm }) {
  const lift = useOpsAction(firm.id);
  return (
    <div role="status" className="flex flex-wrap items-center gap-x-4 gap-y-3 rounded-lg border border-loss/40 bg-loss/10 px-4 py-3.5">
      <PauseIcon className="size-5 shrink-0 text-loss" />
      <span className="flex min-w-0 flex-[1_1_20rem] flex-col gap-0.5 text-sm">
        <strong className="font-semibold">Suspended since {formatDateTime(firm.suspension!.at)}</strong>
        <span className="whitespace-pre-line">{firm.suspension!.reason}</span>
      </span>
      <button type="button" disabled={lift.isPending} onClick={() => lift.mutate({ action: "unsuspend", text: "" })} className={buttonClass}>
        Lift the suspension
      </button>
      <ErrorText error={lift.error} />
    </div>
  );
}

/** Traders that have waited too long for the firm's payouts, on every tab of a live firm. */
function LatePayoutsBanner({ firm, now }: { firm: Firm; now: number }) {
  const payouts = firm.figures.payouts;
  const late = firm.status === "Live" ? latePayouts(payouts.waiting, payouts.lateAfterDays, now) : [];
  if (late.length === 0) {
    return null;
  }

  const mailto = mailtoAdmins(firm);
  const approved = late.filter((p) => p.status === "Approved").length;
  const amounts = new Map<string, number>();
  for (const p of late) {
    amounts.set(p.currency, (amounts.get(p.currency) ?? 0) + p.amount);
  }

  return (
    <div role="status" className="flex flex-wrap items-center gap-x-4 gap-y-3 rounded-lg border border-warning/40 bg-warning/10 px-4 py-3.5">
      <ClockIcon className="size-5 shrink-0 text-warning" />
      <span className="flex min-w-0 flex-[1_1_20rem] flex-col gap-0.5 text-sm">
        <strong className="font-semibold">Traders have waited {daysSince(late[0].requestedAt, now)} days for a payout</strong>
        <span>
          {late.length === 1 ? "A payout" : `${late.length} payouts`} of {[...amounts].map(([currency, amount]) => `${formatMoney(amount)} ${currency}`).join(" + ")}{" "}
          {late.length === 1 ? "has" : "have"} waited more than {payouts.lateAfterDays} days, {approved === 0 ? "none approved yet" : `${approved} approved by the firm`}.
          {payouts.platformAverageDaysToPay !== null && ` Firms pay ${payouts.platformAverageDaysToPay} days after a request on average.`}
        </span>
      </span>
      {mailto && (
        <a href={mailto} className={`${secondaryButtonClass} flex items-center gap-2`}>
          <MailIcon className="size-4" />
          Email the administrators
        </a>
      )}
    </div>
  );
}

/** How the firm is doing: its challenges, sales, payouts and pass rate, how it pays its traders, and what it pays us. */
function Overview({ firm, onTab }: { firm: Firm; onTab: (tab: OpsFirmTab) => void }) {
  const { figures, billing } = firm;
  const currency = billing.currency;
  const live = firm.status === "Live";
  return (
    <div className="flex flex-col gap-5">
      {live ? (
        <dl className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <StatTile
            label="Open challenges"
            value={String(billing.slots.used)}
            unit={billing.slots.slots === null ? "no limit" : `of ${billing.slots.slots}`}
            note={`${figures.accounts.evaluation} in evaluation · ${figures.accounts.funded} funded${billing.pausedChallenges > 0 ? ` · ${billing.pausedChallenges} paused` : ""}`}
          />
          <StatTile label="Sales, last 30 days" value={formatTotals(figures.sales.totals, currency)} note={`${figures.sales.orders} ${figures.sales.orders === 1 ? "challenge" : "challenges"} bought in its portal`} />
          <StatTile
            label="Paid to traders, last 30 days"
            value={formatTotals(figures.payouts.summary.paidLast30Days.totals, currency)}
            note={`${figures.payouts.summary.paidLast30Days.count} ${figures.payouts.summary.paidLast30Days.count === 1 ? "payout" : "payouts"} marked as paid`}
          />
          <StatTile
            label="Pass rate, last 90 days"
            value={passRateText(figures.passRate)}
            note={figures.passRate.ended === 0 ? "No evaluation has ended yet" : `${figures.passRate.passed} of ${figures.passRate.ended} ended evaluations`}
          />
        </dl>
      ) : (
        <SandboxNote firm={firm} onTab={onTab} />
      )}
      <div className="grid grid-cols-1 items-start gap-5 lg:grid-cols-[minmax(0,2fr)_minmax(18rem,1fr)]">
        {live ? <TraderPayouts firm={firm} /> : <Panel title="Payouts to traders"><p className="text-sm text-muted">The firm is not live, so its payouts are tests.</p></Panel>}
        <div className="flex flex-col gap-5">
          <PaysUs firm={firm} onTab={onTab} />
          <Team firm={firm} />
        </div>
      </div>
    </div>
  );
}

function SandboxNote({ firm, onTab }: { firm: Firm; onTab: (tab: OpsFirmTab) => void }) {
  return (
    <Panel>
      <p className="text-sm text-muted">
        {firm.name} is not live. It has started {firm.sandboxUse.challenges} test {firm.sandboxUse.challenges === 1 ? "challenge" : "challenges"} and made {firm.sandboxUse.purchases} test{" "}
        {firm.sandboxUse.purchases === 1 ? "purchase" : "purchases"}.{" "}
        {!firm.configured && (
          <button type="button" onClick={() => onTab("review")} className="text-accent hover:underline">
            Go to its review
          </button>
        )}
      </p>
    </Panel>
  );
}

/** How the firm pays its traders. We look at this first: a firm that does not pay its traders hurts every firm on the platform. */
function TraderPayouts({ firm }: { firm: Firm }) {
  const payouts = firm.figures.payouts;
  const { summary } = payouts;
  const currency = firm.billing.currency;
  const platformShare = payouts.platformDecidedLast90Days === 0 ? null : Math.round((payouts.platformRejectedLast90Days / payouts.platformDecidedLast90Days) * 100);
  return (
    <Panel title="How the firm pays its traders">
      <dl className="flex flex-col text-sm">
        <PayoutRow label="Asked for, waiting for the firm">
          {summary.toApprove.count === 0 ? "None" : `${summary.toApprove.count} · ${formatTotals(summary.toApprove.totals, currency)}${summary.toApprove.oldest ? ` · oldest ${formatDate(summary.toApprove.oldest)}` : ""}`}
        </PayoutRow>
        <PayoutRow label="Approved, not paid">
          {summary.toPay.count === 0 ? "None" : `${summary.toPay.count} · ${formatTotals(summary.toPay.totals, currency)}${summary.toPay.oldest ? ` · approved ${formatDate(summary.toPay.oldest)}` : ""}`}
        </PayoutRow>
        <PayoutRow label="From request to paid, last 30 days">
          {summary.averageDaysToPay === null ? "Nothing paid" : `${summary.averageDaysToPay} days`}
          {payouts.platformAverageDaysToPay !== null && <span className="text-muted"> · every firm {payouts.platformAverageDaysToPay}</span>}
        </PayoutRow>
        <PayoutRow label="Rejected, last 90 days">
          {payouts.decidedLast90Days === 0 ? "None decided" : `${payouts.rejectedLast90Days} of ${payouts.decidedLast90Days}`}
          {platformShare !== null && <span className="text-muted"> · every firm {platformShare}%</span>}
        </PayoutRow>
      </dl>
      {payouts.waiting.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[30rem] text-sm">
            <caption className="pb-2 text-left font-medium">Payouts traders wait for, the oldest first</caption>
            <thead className="text-left text-muted">
              <tr>
                <th scope="col" className="py-1.5 font-normal">
                  Account
                </th>
                <th scope="col" className="py-1.5 font-normal">
                  Asked for
                </th>
                <th scope="col" className="py-1.5 font-normal">
                  Status
                </th>
                <th scope="col" className="py-1.5 text-right font-normal">
                  Amount
                </th>
              </tr>
            </thead>
            <tbody>
              {payouts.waiting.map((p) => (
                <tr key={`${p.accountNumber}-${p.requestedAt}`} className="border-t border-border">
                  <td className="py-2.5 font-mono">#{p.accountNumber}</td>
                  <td className="py-2.5">{formatDate(p.requestedAt)}</td>
                  <td className="py-2.5">{p.status === "Approved" && p.approvedAt ? `Approved ${formatDate(p.approvedAt)}` : "To approve"}</td>
                  <td className="py-2.5 text-right font-mono tabular-nums">
                    {formatMoney(p.amount)} {p.currency}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <p className="text-xs text-muted">We see account numbers and amounts, not who the traders are.</p>
    </Panel>
  );
}

function PayoutRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-wrap justify-between gap-x-3 gap-y-1 border-t border-border py-3 first:border-t-0 first:pt-0">
      <dt>{label}</dt>
      <dd>{children}</dd>
    </div>
  );
}

/** What the firm pays us in short, with the way to its charges. */
function PaysUs({ firm, onTab }: { firm: Firm; onTab: (tab: OpsFirmTab) => void }) {
  const { billing } = firm;
  return (
    <Panel
      title="What it pays us"
      actions={
        <button type="button" onClick={() => onTab("billing")} className="text-sm text-accent hover:underline">
          All charges
        </button>
      }
    >
      <PlanFacts firm={firm} />
      {billing.plan === null && <p className="text-sm text-muted">{firm.status === "Live" ? "Nothing yet." : "The firm pays nothing until it goes live."}</p>}
    </Panel>
  );
}

function PlanFacts({ firm }: { firm: Firm }) {
  const { billing } = firm;
  const facts: [string, string][] = [];
  if (billing.plan === "Complimentary") {
    facts.push(["Plan", billing.slots.slots === null ? "Free, no limit" : `Free, ${billing.slots.slots} slots`]);
  } else if (billing.plan === "Paid") {
    facts.push(["Plan", `${billing.slots.slots ?? 0} slots this month`]);
    if (billing.nextCharge) {
      facts.push(["Each month", chargeAmountText(billing.nextCharge, billing.currency)]);
      facts.push(["Next charge", `${chargeAmountText(billing.nextCharge, billing.currency)} on ${formatDate(billing.nextCharge.chargeAt)}, for ${monthName(billing.nextCharge.month)}`]);
    }

    facts.push(["Card", billing.card ? cardLabel(billing.card) : "None saved"]);
    facts.push(["More slots when full", billing.autoExpandStep === null ? "Off" : `${billing.autoExpandStep} at a time`]);
    if (billing.unpaidSince) {
      facts.push(["Unpaid since", formatDateTime(billing.unpaidSince)]);
    }
  }

  if (billing.depositPaid > 0) {
    facts.push(["Deposit paid", `${formatMoney(billing.depositPaid)} ${billing.currency}`]);
  }

  if (facts.length === 0) {
    return null;
  }

  return (
    <dl className="flex flex-col gap-2.5 text-sm">
      {facts.map(([label, value]) => (
        <div key={label} className="flex justify-between gap-3">
          <dt className="text-muted">{label}</dt>
          <dd className="text-right">{value}</dd>
        </div>
      ))}
    </dl>
  );
}

/** The firm's plan and its newest charges. */
function Billing({ firm }: { firm: Firm }) {
  const charges = firm.billing.charges;
  return (
    <div className="grid grid-cols-1 items-start gap-5 lg:grid-cols-[minmax(0,2fr)_minmax(18rem,1fr)]">
      <Panel title="Charges">
        {charges.length === 0 ? (
          <p className="text-sm text-muted">No charges yet.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[34rem] text-sm">
              <thead className="text-left text-muted">
                <tr>
                  <th scope="col" className="py-2 font-normal">
                    Charge
                  </th>
                  <th scope="col" className="py-2 font-normal">
                    For
                  </th>
                  <th scope="col" className="py-2 text-right font-normal">
                    Amount
                  </th>
                  <th scope="col" className="py-2 pl-4 font-normal">
                    Status
                  </th>
                </tr>
              </thead>
              <tbody>
                {charges.map((charge) => (
                  <tr key={charge.id} className="border-t border-border align-top">
                    <td className="py-2.5 font-mono">#{charge.number}</td>
                    <td className="py-2.5">
                      {chargeLabel(charge)}
                      <span className="block text-xs text-muted">{formatDate(charge.createdAt)}</span>
                    </td>
                    <td className="py-2.5 text-right font-mono tabular-nums">
                      {formatMoney(charge.amount)} {charge.currency}
                    </td>
                    <td className="py-2.5 pl-4">
                      <Badge tone={charge.status === "Paid" ? "profit" : charge.status === "Void" ? "muted" : charge.failure ? "loss" : "warning"}>{chargeStatusLabels[charge.status]}</Badge>
                      {charge.failure && charge.status !== "Paid" && <span className="mt-1 block text-xs text-muted">{charge.failure}</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Panel>
      <Panel title="Plan">
        <PlanFacts firm={firm} />
        {firm.billing.plan === null && <p className="text-sm text-muted">{firm.status === "Live" ? "No plan." : "The firm pays nothing until it goes live."}</p>}
      </Panel>
    </div>
  );
}

function Team({ firm }: { firm: Firm }) {
  const mailto = mailtoAdmins(firm);
  return (
    <Panel
      title="Administrators"
      actions={
        mailto && (
          <a href={mailto} className="flex items-center gap-1.5 text-sm text-accent hover:underline">
            <MailIcon className="size-4" />
            Email them
          </a>
        )
      }
    >
      {firm.admins.length === 0 ? (
        <p className="text-sm text-muted">None.</p>
      ) : (
        <ul className="flex flex-col text-sm">
          {firm.admins.map((admin) => (
            <li key={admin.email} className="flex justify-between gap-3 border-t border-border py-2.5 first:border-t-0 first:pt-0">
              <span className="min-w-0 truncate">{admin.email}</span>
              <span className="shrink-0 text-muted">since {formatDate(admin.since)}</span>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  );
}

function History({ firm }: { firm: Firm }) {
  return (
    <Panel title="History">
      {firm.events.length === 0 ? (
        <p className="text-sm text-muted">Nothing yet.</p>
      ) : (
        <ol className="flex flex-col text-sm">
          {firm.events.map((event) => (
            <li key={event.id} className="flex flex-wrap gap-x-4 gap-y-1 border-t border-border py-2.5 first:border-t-0 first:pt-0">
              <span className="w-40 shrink-0 text-muted">{formatDateTime(event.recordedAt)}</span>
              <span className="min-w-0 flex-[1_1_16rem] whitespace-pre-line">{eventText(event)}</span>
              <span className="text-muted">{event.actor}</span>
            </li>
          ))}
        </ol>
      )}
    </Panel>
  );
}

/** Common reasons, so that the firm always learns what to do for us to lift it. */
const suspendReasons = [
  { label: "Payouts not paid", text: "Payouts you approved have not been paid to your traders. Pay them and mark them as paid, then contact us." },
  { label: "Unpaid months", text: "Your monthly charges are not paid. Pay them under Plan and billing, then contact us." },
  { label: "Breaks our terms", text: "Your firm breaks our terms. We have emailed you the details." },
];

/** Suspends the firm, with a reason its administrators see, after saying what it means. */
function SuspendDialog({ firm, open, onClose }: { firm: Firm; open: boolean; onClose: () => void }) {
  const suspend = useOpsAction(firm.id);
  const [reason, setReason] = useState("");
  const openChallenges = firm.billing.slots.used;
  const close = () => {
    suspend.reset();
    onClose();
  };

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    suspend.mutate(
      { action: "suspend", text: reason },
      {
        onSuccess: () => {
          setReason("");
          onClose();
        },
      },
    );
  };

  return (
    <Modal
      open={open}
      onClose={close}
      title={`Suspend ${firm.name}?`}
      footer={
        <>
          <button type="button" onClick={close} className={secondaryButtonClass}>
            Keep it running
          </button>
          <button type="submit" form="suspend-firm" disabled={suspend.isPending || reason.trim() === ""} className={dangerButtonClass}>
            {suspend.isPending ? "Suspending..." : `Suspend ${firm.name}`}
          </button>
        </>
      }
    >
      <form id="suspend-firm" onSubmit={submit} className="flex flex-col gap-4">
        <ul className="flex list-disc flex-col gap-1.5 rounded-md border border-loss/40 bg-loss/10 py-3 pl-8 pr-4 text-sm">
          <li>No new challenge can start, and its shop closes.</li>
          <li>
            {openChallenges === 0 ? "Its challenges pause" : `Its ${openChallenges} open ${openChallenges === 1 ? "challenge pauses" : "challenges pause"}`}. Traders can still close their positions,
            and the paused days do not count.
          </li>
          <li>Its administrators get the reason by email and see it on every page of their admin panel.</li>
          <li>Its months are charged as usual.</li>
        </ul>
        <div className="flex flex-col gap-2">
          <span className="text-sm text-muted">Common reasons</span>
          <div className="flex flex-wrap gap-2">
            {suspendReasons.map((r) => (
              <button
                key={r.label}
                type="button"
                aria-pressed={reason === r.text}
                onClick={() => setReason(r.text)}
                className={`rounded-full border px-3 py-1.5 text-sm ${reason === r.text ? "border-loss bg-loss/10 text-foreground" : "border-border hover:border-muted"}`}
              >
                {r.label}
              </button>
            ))}
          </div>
        </div>
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">Reason the firm sees</span>
          <textarea rows={4} required value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Say what they must do for us to lift it." className={fieldClass} />
        </label>
        <p className="text-sm text-muted">You can lift the suspension at any time. Its challenges then go on where they stopped, unless a month is unpaid.</p>
        <ErrorText error={suspend.error} />
      </form>
    </Modal>
  );
}

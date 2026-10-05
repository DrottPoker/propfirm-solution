"use client";

import Link from "next/link";
import { Fragment, useId, useState } from "react";

import { accountStatus } from "@/lib/admin";
import type { AccountDetails, Step } from "@/lib/api/types";
import { canCancel, expiryLabels, failureLabels, kindOf, kindsOf } from "@/lib/challenge";
import { breachClosesText, isTrading } from "@/lib/dashboard";
import { formatDate, formatDateTime, formatMoney, timeZoneName } from "@/lib/format";
import { useAccountCommand, useChallenges, useFirmAccount, useHistory, useTraderSummary } from "@/lib/queries";
import { describeInput, describeOutputs } from "@/lib/ruleLog";

import { AccountHistory } from "./AccountHistory";
import { teamOnlyText } from "./BeforeApproval";
import { ChallengeRules } from "./ChallengeRules";
import { Modal } from "./Dialog";
import { ShieldCheckIcon } from "./icons";
import { PayoutTable } from "./Payouts";
import { StageTiles } from "./StageSteps";
import { KeyFigures, Objectives } from "./TraderAccount";
import { TraderCard } from "./TraderCard";
import { AdminPage, Badge, buttonClass, dangerButtonClass, ErrorText, fieldClass, Message, Panel, secondaryButtonClass, Tabs } from "./ui";

type Tab = "overview" | "trading" | "payouts" | "log";

/** What the firm was told about the email to the trader, when it started the challenge. */
/** How emailing the trader went after the challenge started. Withheld: we have not approved the firm, and the trader is not one of its administrators. */
export type Emailed = "Invitation" | "Notice" | "failed" | "withheld" | null;

/**
 * One of the firm's accounts: what the firm must decide about it, where it stands, its trader, its trading history, its
 * payouts and every step of the rule engine.
 */
export function AdminAccount({ accountId, emailed }: { accountId: string; emailed: Emailed }) {
  const details = useFirmAccount(accountId);
  const challenges = useChallenges();
  const trader = useTraderSummary(accountId);
  const [tab, setTab] = useState<Tab>("overview");
  // Whether the firm has ticked every check of the trader, which approving funding or a payout asks about otherwise.
  const traderChecked = trader.data ? trader.data.checks.every((c) => c.checkedAt !== null) : undefined;

  if (details.isError) {
    return <Message text={details.error.message} />;
  }

  if (!details.data) {
    return <Message text="Loading..." />;
  }

  const data = details.data;
  const challengeName = (id: string) => challenges.data?.find((c) => c.id === id)?.name ?? id;
  return (
    <AdminPage>
      <Header details={data} />
      {emailed && <EmailedNotice emailed={emailed} email={data.account.email} />}
      <Notices details={data} />
      {data.account.status === "AwaitingFunding" && <FundingDecision details={data} traderChecked={traderChecked} />}

      <Tabs
        label="Account"
        tabs={[
          { value: "overview", label: "Overview" },
          { value: "trading", label: "Trading" },
          { value: "payouts", label: "Payouts", count: data.payouts.length },
          { value: "log", label: "Rule log" },
        ]}
        value={tab}
        onChange={setTab}
      />

      <div role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`} className="flex flex-col gap-6">
        {tab === "overview" && (
          <>
            <StageTiles details={data} audience="firm" />
            <KeyFigures details={data} paidOutLabel="Paid out" />
            <div className="grid grid-cols-1 items-start gap-6 lg:grid-cols-[minmax(0,2fr)_minmax(19rem,1fr)]">
              <Objectives details={data} />
              <TraderCard account={data.account} challengeName={challengeName} />
            </div>
            <ChallengeRules details={data} audience="firm" />
          </>
        )}
        {tab === "trading" && <AccountHistory details={data} now={details.dataUpdatedAt} role="admin" />}
        {tab === "payouts" &&
          (data.payouts.length === 0 ? (
            <p className="text-sm text-muted">{data.account.funded ? "The trader has not asked for a payout yet." : "Payouts come once the account is funded."}</p>
          ) : (
            <Panel>
              <PayoutTable payouts={data.payouts} traderChecked={traderChecked} />
            </Panel>
          ))}
        {tab === "log" && <RuleLog details={data} />}
      </div>
    </AdminPage>
  );
}

function Header({ details }: { details: AccountDetails }) {
  const { account, challenge } = details;
  const status = accountStatus(account);
  const [cancelling, setCancelling] = useState(false);
  return (
    <div className="flex flex-col gap-3">
      <nav aria-label="Breadcrumb" className="text-sm text-muted">
        <Link href="/admin/accounts" className="hover:text-foreground">
          Accounts
        </Link>{" "}
        <span aria-hidden="true">/</span> <span className="text-foreground">#{account.number}</span>
      </nav>
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex min-w-0 flex-col gap-1.5">
          <div className="flex flex-wrap items-center gap-3">
            <h1 className="text-2xl font-semibold tracking-tight">{challenge.name}</h1>
            <Badge tone={status.tone}>{status.label}</Badge>
          </div>
          <p className="text-sm text-muted">
            Account #{account.number} ·{" "}
            <Link href={`/admin/accounts?search=${encodeURIComponent(account.email)}`} className="text-accent hover:underline">
              {account.email}
            </Link>
            {account.reference && <> · Ref. {account.reference}</>}
            {account.tradingAccountId && <> · Trading account {account.tradingAccountId}</>} · Started {formatDate(account.createdAt, details.challenge.tradingDay.timeZone)}
          </p>
        </div>
        {canCancel(account) && (
          <button type="button" onClick={() => setCancelling(true)} className={`${secondaryButtonClass} text-loss`}>
            Cancel account
          </button>
        )}
      </div>
      {cancelling && <CancelDialog details={details} onClose={() => setCancelling(false)} />}
    </div>
  );
}

function EmailedNotice({ emailed, email }: { emailed: Exclude<Emailed, null>; email: string }) {
  const [shown, setShown] = useState(true);
  if (!shown) {
    return null;
  }

  const failed = emailed === "failed" || emailed === "withheld";
  return (
    <p role="status" className={`flex flex-wrap items-center gap-x-3 gap-y-1 rounded-lg border px-4 py-3 text-sm ${failed ? "border-warning/40 bg-warning/10" : "border-profit/40 bg-profit/10"}`}>
      <span className="flex-1">
        {emailed === "failed"
          ? `The challenge started, but the email to ${email} could not be sent. Try again from the trader's card below.`
          : emailed === "withheld"
            ? `The challenge started, but we did not email ${email}. ${teamOnlyText}`
          : emailed === "Invitation"
            ? `The challenge started. We emailed ${email} an invitation to choose a password for your portal.`
            : `The challenge started. We emailed ${email} that it has started.`}
      </span>
      <button type="button" onClick={() => setShown(false)} className="text-muted underline hover:text-foreground">
        Close
      </button>
    </p>
  );
}

/** Why the account has stopped, or what is happening to it right now. */
function Notices({ details }: { details: AccountDetails }) {
  const { account, breach, expiry, endedAt } = details;
  const timeZone = details.challenge.tradingDay.timeZone;
  const notices: { tone: string; text: string }[] = [];
  switch (account.status) {
    case "OpeningAccount":
      notices.push({ tone: "text-warning", text: `The trading account for ${account.stageName} is being opened.` });
      break;
    case "Failed":
      notices.push({
        tone: "text-loss",
        text: breach
          ? [
              `Failed on ${formatDateTime(breach.time, timeZone)}: equity ${formatMoney(breach.equity)} fell below the ${failureLabels[breach.reason]} at ${formatMoney(breach.level)}.`,
              breachClosesText(details),
            ]
              .filter(Boolean)
              .join(" ")
          : expiry
            ? `Ended on ${formatDate(expiry.day)}: ${expiryLabels[expiry.reason]}`
            : "Failed.",
      });
      break;
    case "Cancelled":
      notices.push({ tone: "text-muted", text: `Cancelled by the firm${endedAt ? ` on ${formatDateTime(endedAt, timeZone)}` : ""}.` });
      break;
    default:
      break;
  }

  if (account.paused && account.status !== "Failed" && account.status !== "Cancelled") {
    notices.push({
      tone: "text-warning",
      text: "Paused while your month is unpaid. The trader can close positions but not open new ones, and the days do not count.",
    });
  }

  if (isTrading(details) && !details.live) {
    notices.push({ tone: "text-muted", text: "The account cannot be valued right now. The figures are as the trading platform last reported them." });
  }

  return notices.length === 0 ? null : (
    <div className="flex flex-col gap-2">
      {notices.map((notice) => (
        <p key={notice.text} role="status" className={`rounded-lg border border-border bg-panel px-4 py-3 text-sm ${notice.tone}`}>
          {notice.text}
        </p>
      ))}
    </div>
  );
}

/** The trader passed every evaluation stage. The firm approves the funded account once its own checks are done. */
function FundingDecision({ details, traderChecked }: { details: AccountDetails; traderChecked: boolean | undefined }) {
  const { account, challenge, stages } = details;
  const [confirming, setConfirming] = useState(false);
  const command = useAccountCommand(account.id);
  const lastPassed = stages.filter((s) => s.progress === "Passed").at(-1)?.passedAt;

  return (
    <section aria-labelledby="decision" className="flex flex-wrap items-center gap-x-6 gap-y-4 rounded-lg border border-warning/45 bg-warning/5 px-5 py-4">
      <span aria-hidden="true" className="grid size-10 shrink-0 place-items-center rounded-lg bg-warning/15 text-warning">
        <ShieldCheckIcon className="size-5" />
      </span>
      <div className="flex min-w-0 flex-[1_1_24rem] flex-col gap-1">
        <h2 id="decision" className="font-semibold">
          Passed every evaluation stage{lastPassed ? ` on ${formatDate(lastPassed, challenge.tradingDay.timeZone)}` : ""}
        </h2>
        <p className="text-sm text-muted">
          Approve the funded account when your checks of the trader are done. It starts with {formatMoney(challenge.initialBalance)} {challenge.currency} and{" "}
          {challenge.funded.profitSplitPercent}% of the profit to the trader. Until then, the trader sees that the account is under review.
        </p>
      </div>
      <button type="button" onClick={() => setConfirming(true)} className={buttonClass}>
        Approve funded account
      </button>
      {confirming && (
        <Modal
          open
          onClose={() => setConfirming(false)}
          title="Approve the funded account?"
          description={`${account.email} gets a funded account of ${formatMoney(challenge.initialBalance)} ${challenge.currency}, with ${challenge.funded.profitSplitPercent}% of the profit to the trader.`}
          footer={
            <>
              <button type="button" onClick={() => setConfirming(false)} className={secondaryButtonClass}>
                Not yet
              </button>
              <button
                type="button"
                disabled={command.isPending}
                onClick={() => command.mutate({ kind: "approve-funding" }, { onSuccess: () => setConfirming(false) })}
                className={buttonClass}
              >
                {command.isPending ? "Approving..." : "Approve funded account"}
              </button>
            </>
          }
        >
          {traderChecked === false && (
            <p role="note" className="rounded-lg border border-warning/40 bg-warning/10 px-3.5 py-3 text-sm">
              You have not ticked that you checked the trader&apos;s ID and address. Tick them on the trader card under Overview once you have.
            </p>
          )}
          <ErrorText error={command.error} />
        </Modal>
      )}
    </section>
  );
}

function CancelDialog({ details, onClose }: { details: AccountDetails; onClose: () => void }) {
  const { account } = details;
  const command = useAccountCommand(account.id);
  const formId = useId();
  const [reason, setReason] = useState("");

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    command.mutate({ kind: "cancel", reason: reason.trim() }, { onSuccess: onClose });
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={`Cancel account #${account.number}?`}
      description={`${account.email} · ${details.challenge.name}. Its trading account is closed, and this cannot be undone. A refund is not made here.`}
      footer={
        <>
          <button type="button" onClick={onClose} className={secondaryButtonClass}>
            Keep the account
          </button>
          <button type="submit" form={formId} disabled={command.isPending} className={dangerButtonClass}>
            {command.isPending ? "Cancelling..." : "Cancel account"}
          </button>
        </>
      }
    >
      <form id={formId} onSubmit={submit} className="flex flex-col gap-3">
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">
            Reason <span className="font-normal text-muted">(optional)</span>
          </span>
          <input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="For example: refunded" className={fieldClass} />
        </label>
        <ErrorText error={command.error} />
      </form>
    </Modal>
  );
}

/**
 * Every input to the account and what the rule engine decided, in plain sentences: the audit trail. The rule engine's
 * own names, the inputs as stored and the trading platform's event behind each, its evidence, are behind Show details.
 */
function RuleLog({ details }: { details: AccountDetails }) {
  const history = useHistory(details.account.id);
  const [open, setOpen] = useState<number | null>(null);
  const timeZone = details.challenge.tradingDay.timeZone;
  const stages = [...details.challenge.evaluation, details.challenge.funded];
  const context = { currency: details.challenge.currency, stageName: (stage: number) => stages[stage]?.name ?? `Stage ${stage + 1}` };
  return (
    <Panel title="Rule log">
      <p className="text-sm text-muted">Everything that happened to the account and what the rules did about it, in order. Times are in {timeZoneName(timeZone)}.</p>
      <ErrorText error={history.error} />
      <div className="overflow-x-auto">
        <table className="w-full min-w-[40rem] text-sm">
          <thead className="text-left text-muted">
            <tr>
              <th className="py-2 font-normal">Time</th>
              <th className="py-2 pl-4 font-normal">What happened</th>
              <th className="py-2 pl-4 font-normal">What the rules did</th>
              <th className="py-2 pl-4 font-normal">
                <span className="sr-only">Details</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {(history.data ?? []).map((step) => {
              const decisions = describeOutputs(step.outputs, context);
              return (
                <Fragment key={step.step}>
                  <tr className="border-t border-border align-top">
                    <td className="whitespace-nowrap py-2 text-muted">{formatDateTime(step.recordedAt, timeZone)}</td>
                    <td className="py-2 pl-4">{describeInput(step.input, context)}</td>
                    <td className="py-2 pl-4">
                      {decisions.length === 0 ? (
                        <span className="text-muted">Nothing to do</span>
                      ) : (
                        <ul className="flex flex-col gap-0.5">
                          {decisions.map((decision, index) => (
                            <li key={index}>{decision}</li>
                          ))}
                        </ul>
                      )}
                    </td>
                    <td className="py-2 pl-4 text-right">
                      <button
                        type="button"
                        aria-expanded={open === step.step}
                        onClick={() => setOpen(open === step.step ? null : step.step)}
                        className="whitespace-nowrap text-xs text-accent hover:underline"
                      >
                        {open === step.step ? "Hide details" : "Show details"}
                      </button>
                    </td>
                  </tr>
                  {open === step.step && (
                    <tr>
                      <td colSpan={4} className="pb-3">
                        <StepDetails step={step} />
                      </td>
                    </tr>
                  )}
                </Fragment>
              );
            })}
          </tbody>
        </table>
      </div>
    </Panel>
  );
}

function StepDetails({ step }: { step: Step }) {
  const outputs = kindsOf(step.outputs);
  return (
    <div className="flex flex-col gap-2 rounded-lg border border-border bg-background p-3.5 text-xs">
      <p>
        Step {step.step}: {kindOf(step.input)}
        {outputs.length > 0 && ` \u2192 ${outputs.join(", ")}`}
        {step.sourceEvent ? ` · evidence ${kindOf(step.sourceEvent)}` : ""}
      </p>
      <pre className="max-h-72 overflow-auto whitespace-pre-wrap font-mono text-muted">
        {JSON.stringify({ input: step.input, decisions: step.outputs, evidence: step.sourceEvent ?? null }, null, 2)}
      </pre>
    </div>
  );
}

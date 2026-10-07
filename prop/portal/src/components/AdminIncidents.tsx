"use client";

import Link from "next/link";
import { useId, useState } from "react";
import { toast } from "sonner";

import type { FirmIncident, FirmIncidentSummary, IncidentAccount } from "@/lib/api/types";
import { formatDateTime, formatMoney, timeZoneName } from "@/lib/format";
import {
  type AccountFilter,
  balanceChoices,
  decisionText,
  durationText,
  filterAccounts,
  incidentSummary,
  kindLabels,
  periodText,
  statusLabels,
  statusTones,
  whatHappened,
} from "@/lib/incidents";
import { useCredit, useFirmIncident, useFirmIncidents, useIncidentNote, useReinstate } from "@/lib/queries";

import { Modal } from "./Dialog";
import { ExternalIcon, IncidentIcon } from "./icons";
import { AdminPage, Badge, buttonClass, EmptyState, ErrorText, fieldClass, FilterTabs, Loading, Message, PageHeader, Panel, secondaryButtonClass, StatTile } from "./ui";

// The browser's time zone, since an incident is the same moment for every account of the firm.
const localZone = () => timeZoneName(Intl.DateTimeFormat().resolvedOptions().timeZone);

const whatTones = { loss: "text-loss", warning: "text-warning", default: "", muted: "text-muted" } as const;

/** The incidents of the last 90 days that concern the firm: outages of the trading platform, as we published them (ADR 0053). */
export function AdminIncidents() {
  const incidents = useFirmIncidents();
  if (incidents.isPending) {
    return <Loading label="Loading incidents" />;
  }

  if (incidents.isError) {
    return <Message text={incidents.error.message} />;
  }

  const rows = incidents.data.incidents;
  return (
    <AdminPage>
      <PageHeader
        title="Incidents"
        description="Outages of the trading platform that reached your firm. For each one you see which of your traders' accounts it reached, and you can reinstate a phase it ended or credit an account."
        actions={
          <a href="/status" target="_blank" rel="noopener" className={`${secondaryButtonClass} flex items-center gap-1.5`}>
            Your status page
            <ExternalIcon className="size-4" />
          </a>
        }
      />
      <section aria-label="Incidents" className="flex flex-col rounded-xl border border-border bg-panel shadow-card">
        {rows.length === 0 ? (
          <EmptyState
            icon={<IncidentIcon className="size-6" />}
            title="No incidents in the last 90 days"
            text="When the trading platform has an outage, we publish it here and on your status page, and email you."
          />
        ) : (
          <ul className="flex flex-col">
            {rows.map((incident) => (
              <IncidentRow key={incident.id} incident={incident} />
            ))}
          </ul>
        )}
      </section>
    </AdminPage>
  );
}

function IncidentRow({ incident }: { incident: FirmIncidentSummary }) {
  return (
    <li className="border-t border-border first:border-t-0">
      <Link href={`/admin/incidents/${incident.id}`} className="flex flex-col gap-1.5 px-5 py-4 hover:bg-background/50 sm:flex-row sm:items-center sm:gap-4">
        <span className="flex min-w-0 flex-1 flex-col gap-1">
          <span className="flex flex-wrap items-center gap-x-2.5 gap-y-1">
            <span className="font-medium">{incident.title}</span>
            <Badge tone={statusTones[incident.status]}>{statusLabels[incident.status]}</Badge>
          </span>
          <span className="text-sm text-muted">
            {kindLabels[incident.kind]}: {periodText(incident.startedAt, incident.endedAt)} {localZone()}
            {incident.endedAt && <> ({durationText(incident.startedAt, incident.endedAt)})</>}
          </span>
        </span>
        <span className="shrink-0 text-sm text-muted">
          {incident.decisions === 0 ? "No decisions yet" : `${incident.decisions} ${incident.decisions === 1 ? "decision" : "decisions"}`}
        </span>
      </Link>
    </li>
  );
}

/**
 * An incident as the firm sees it: what happened and how long, its accounts the incident reached with what happened to
 * each, the firm's decisions, and its own note to its traders.
 */
export function AdminIncident({ incidentId }: { incidentId: string }) {
  const incident = useFirmIncident(incidentId);
  const [filter, setFilter] = useState<AccountFilter | null>(null);
  const [acting, setActing] = useState<{ kind: "reinstate" | "credit"; account: IncidentAccount } | null>(null);
  if (incident.isPending) {
    return <Loading label="Loading the incident" />;
  }

  if (incident.isError) {
    return <Message text={incident.error.message} />;
  }

  const data = incident.data;
  const summary = incidentSummary(data);
  const needing = filterAccounts(data, "needs").length;
  const shown = filter ?? (needing > 0 ? "needs" : "all");
  const rows = filterAccounts(data, shown);
  const latest = data.updates.at(-1);
  return (
    <AdminPage>
      <PageHeader
        back={
          <nav aria-label="Breadcrumb" className="text-sm text-muted">
            <Link href="/admin/incidents" className="hover:text-foreground">
              Incidents
            </Link>{" "}
            <span aria-hidden="true">/</span> <span className="text-foreground">{data.title}</span>
          </nav>
        }
        title={data.title}
        description={
          <span className="flex flex-col gap-2">
            <span className="flex flex-wrap items-center gap-2 text-foreground">
              <Badge tone={statusTones[data.status]}>{statusLabels[data.status]}</Badge>
              {periodText(data.startedAt, data.endedAt, undefined, true)} {localZone()}
              {data.endedAt && <>, {durationText(data.startedAt, data.endedAt)}</>}.
            </span>
            <span>{latest && latest.text !== data.publicText ? latest.text : data.publicText}</span>
          </span>
        }
        actions={
          <a href="/status" target="_blank" rel="noopener" className={`${secondaryButtonClass} flex items-center gap-1.5`}>
            See the status page
            <ExternalIcon className="size-4" />
          </a>
        }
      />

      {data.impactKnown ? (
        <dl className="grid grid-cols-2 gap-3 lg:grid-cols-4">
          <StatTile label="Accounts with open positions" value={summary.withPositions} />
          <StatTile label="Broke a loss limit" value={summary.breached} tone={summary.breached > 0 ? "text-loss" : ""} highlight={summary.breached > 0} />
          <StatTile label="Accounts with refused requests" value={summary.withRefusals} />
          <StatTile label="Reinstated" value={summary.breached > 0 ? `${summary.reinstated} of ${summary.breached}` : summary.reinstated} />
        </dl>
      ) : (
        <p role="note" className="rounded-xl border border-warning/40 bg-warning/10 px-4 py-3 text-sm">
          Which of your accounts the incident reached cannot be told right now. The trading platform looks back 30 days, over at most a day, and may be
          unreachable for a moment. Try again shortly.
        </p>
      )}

      {data.impactKnown && (
        <Panel
          title="Accounts in the incident"
          actions={
            <FilterTabs
              label="Accounts to show"
              options={[
                { value: "needs" as const, label: "Needs a decision", count: needing, highlight: true },
                { value: "refused" as const, label: "Refused requests", count: filterAccounts(data, "refused").length },
                { value: "all" as const, label: "All", count: data.accounts.length },
              ]}
              value={shown}
              onChange={setFilter}
            />
          }
        >
          {rows.length === 0 ? (
            <p className="text-sm text-muted">{shown === "needs" ? "Every account has its decision." : "None of your accounts here."}</p>
          ) : (
            <div className="-mx-5 overflow-x-auto">
              <table className="w-full min-w-[56rem] text-sm">
                <thead className="text-left text-muted">
                  <tr>
                    <th scope="col" className="py-2 pl-5 font-normal">
                      Account
                    </th>
                    <th scope="col" className="py-2 pl-4 font-normal">
                      What happened
                    </th>
                    <th scope="col" className="py-2 pl-4 text-right font-normal">
                      Equity when it began
                    </th>
                    <th scope="col" className="py-2 pl-4 text-right font-normal">
                      Now
                    </th>
                    <th scope="col" className="py-2 pr-5 pl-4 text-right font-normal">
                      Decision
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((account) => (
                    <AccountRow key={account.tradingAccountId} incident={data} account={account} onAct={(kind) => setActing({ kind, account })} />
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Panel>
      )}

      <div className="grid gap-6 lg:grid-cols-2">
        <NoteToTraders incident={data} />
        <WhatWasDone incident={data} />
      </div>

      {acting?.kind === "reinstate" && <ReinstateDialog incident={data} account={acting.account} onClose={() => setActing(null)} />}
      {acting?.kind === "credit" && <CreditDialog incident={data} account={acting.account} onClose={() => setActing(null)} />}
    </AdminPage>
  );
}

function AccountRow({ incident, account, onAct }: { incident: FirmIncident; account: IncidentAccount; onAct: (kind: "reinstate" | "credit") => void }) {
  const what = whatHappened(account);
  const decided = incident.decisions.filter((d) => d.accountId === account.accountId);
  const ended = account.status === "Failed";
  return (
    <tr className="border-t border-border align-top">
      <td className="py-3 pl-5">
        <Link href={`/admin/accounts/${account.accountId}`} className="font-medium hover:text-accent">
          #{account.number} {account.challengeName}, {account.stageName}
        </Link>
        <span className="block text-xs text-muted">{account.traderEmail}</span>
      </td>
      <td className={`max-w-sm py-3 pl-4 leading-relaxed ${whatTones[what.tone]}`}>{what.text}</td>
      <td className="py-3 pl-4 text-right tabular-nums">{account.equityAtStart === null ? "-" : formatMoney(account.equityAtStart)}</td>
      <td className="py-3 pl-4 text-right text-muted tabular-nums">
        {ended ? "Ended at" : "Trading,"} {formatMoney(account.equity)}
      </td>
      <td className="py-2.5 pr-5 pl-4 text-right">
        <div className="flex flex-col items-end gap-1.5">
          {decided.map((d) => (
            <span key={d.id} className="text-xs text-profit">
              {decisionText(d)}
            </span>
          ))}
          {account.canReinstate && (
            <button type="button" onClick={() => onAct("reinstate")} className={`${buttonClass} px-3 py-1.5 text-sm`}>
              Reinstate
            </button>
          )}
          {account.canCredit && (
            <button type="button" onClick={() => onAct("credit")} className={`${secondaryButtonClass} px-3 py-1.5 text-sm`}>
              Credit
            </button>
          )}
          {!account.canReinstate && !account.canCredit && decided.length === 0 && <span className="text-xs text-muted">No decision needed</span>}
        </div>
      </td>
    </tr>
  );
}

/** An amount as typed, such as "98,212.50", or null when it is not a positive amount with at most 2 decimals. */
function amountOf(text: string): number | null {
  const cleaned = text.replace(/[\s,]/g, "");
  if (!/^\d+(\.\d{1,2})?$/.test(cleaned)) {
    return null;
  }

  const amount = Number(cleaned);
  return amount > 0 ? amount : null;
}

/**
 * Opens a stage again that a loss limit broken during the incident ended: on the same trading account, with a balance the
 * firm chooses, and the trading days kept or not. The reason is kept with the decision.
 */
function ReinstateDialog({ incident, account, onClose }: { incident: FirmIncident; account: IncidentAccount; onClose: () => void }) {
  const reinstate = useReinstate(incident.id);
  const formId = useId();
  const choices = balanceChoices(account);
  const [choice, setChoice] = useState<string>(choices[0].key);
  const [other, setOther] = useState("");
  const [keepDays, setKeepDays] = useState(true);
  const [reason, setReason] = useState(`${incident.title}, ${periodText(incident.startedAt, incident.endedAt).replace(/^Since/, "since")} ${localZone()}.`);
  const balance = choice === "other" ? amountOf(other) : (choices.find((c) => c.key === choice)?.amount ?? null);
  const limit = account.breach?.floorId === "daily" ? "daily loss limit" : account.breach?.floorId === "max-loss" ? "max loss limit" : "a loss limit";

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    if (balance === null || reason.trim() === "") {
      return;
    }

    reinstate.mutate(
      { accountId: account.accountId, balance, keepTradingDays: keepDays, reason: reason.trim() },
      {
        onSuccess: () => {
          toast.success(`#${account.number} is reinstated with ${formatMoney(balance)} ${account.currency}.`);
          onClose();
        },
      },
    );
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={`Reinstate #${account.number} ${account.challengeName}, ${account.stageName}`}
      description={
        <>
          {account.traderEmail}&apos;s phase ended
          {account.breach && ` at ${formatDateTime(account.breach.at)}`}, when equity broke the {limit}. Reinstating opens it again on the same trading account, with
          the loss limits set again. The trader gets an email, unless you turned that off in Notifications.
        </>
      }
      footer={
        <>
          <button type="button" onClick={onClose} className={secondaryButtonClass}>
            Cancel
          </button>
          <button type="submit" form={formId} disabled={reinstate.isPending || balance === null || reason.trim() === ""} className={buttonClass}>
            {reinstate.isPending ? "Reinstating..." : "Reinstate the account"}
          </button>
        </>
      }
    >
      <form id={formId} onSubmit={submit} className="flex flex-col gap-4">
        <fieldset className="flex flex-col gap-2">
          <legend className="mb-2 text-sm font-medium">Start with</legend>
          {choices.map((c) => (
            <label key={c.key} className={`flex items-start gap-3 rounded-lg border px-3.5 py-3 text-sm ${choice === c.key ? "border-accent bg-accent/5" : "border-border"}`}>
              <input type="radio" name="balance" checked={choice === c.key} onChange={() => setChoice(c.key)} className="mt-1 accent-[var(--accent)]" />
              <span className="flex flex-1 flex-col gap-0.5">
                <span className="flex justify-between gap-3">
                  <span className="font-medium">{c.label}</span>
                  <span className="font-semibold tabular-nums">{formatMoney(c.amount)}</span>
                </span>
                <span className="text-muted">{c.note}</span>
              </span>
            </label>
          ))}
          <label className={`flex items-center gap-3 rounded-lg border px-3.5 py-2.5 text-sm ${choice === "other" ? "border-accent bg-accent/5" : "border-border"}`}>
            <input type="radio" name="balance" checked={choice === "other"} onChange={() => setChoice("other")} className="accent-[var(--accent)]" />
            <span className="flex-1 font-medium">Another amount</span>
            <input
              inputMode="decimal"
              aria-label="Another amount"
              placeholder="0.00"
              value={other}
              onFocus={() => setChoice("other")}
              onChange={(e) => setOther(e.target.value)}
              className={`${fieldClass} w-32 py-1.5 text-right`}
            />
          </label>
        </fieldset>
        {account.tradingDays > 0 && (
          <label className="flex items-center gap-2.5 text-sm">
            <input type="checkbox" checked={keepDays} onChange={(e) => setKeepDays(e.target.checked)} className="accent-[var(--accent)]" />
            Keep the {account.tradingDays} {account.tradingDays === 1 ? "trading day" : "trading days"} counted so far
          </label>
        )}
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">Reason</span>
          <textarea value={reason} onChange={(e) => setReason(e.target.value)} rows={2} maxLength={500} required className={`${fieldClass} resize-y`} />
          <span className="text-xs text-muted">Kept with your name on the incident.</span>
        </label>
        <ErrorText error={reinstate.error} />
      </form>
    </Modal>
  );
}

/** Puts an amount on an account the incident reached that still trades, for example for a close that was refused. */
function CreditDialog({ incident, account, onClose }: { incident: FirmIncident; account: IncidentAccount; onClose: () => void }) {
  const credit = useCredit(incident.id);
  const formId = useId();
  const [amount, setAmount] = useState("");
  const [reason, setReason] = useState("");
  const value = amountOf(amount);

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    if (value === null || reason.trim() === "") {
      return;
    }

    credit.mutate(
      { accountId: account.accountId, amount: value, reason: reason.trim() },
      {
        onSuccess: () => {
          toast.success(`${formatMoney(value)} ${account.currency} is on its way to #${account.number}.`);
          onClose();
        },
      },
    );
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={`Credit #${account.number} ${account.challengeName}, ${account.stageName}`}
      description={`${account.traderEmail}. The amount goes on the trading account once, and the balance goes up by it. ${whatHappened(account).text}`}
      footer={
        <>
          <button type="button" onClick={onClose} className={secondaryButtonClass}>
            Cancel
          </button>
          <button type="submit" form={formId} disabled={credit.isPending || value === null || reason.trim() === ""} className={buttonClass}>
            {credit.isPending ? "Crediting..." : value === null ? "Credit the account" : `Credit ${formatMoney(value)} ${account.currency}`}
          </button>
        </>
      }
    >
      <form id={formId} onSubmit={submit} className="flex flex-col gap-4">
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">Amount, {account.currency}</span>
          <input inputMode="decimal" placeholder="0.00" value={amount} onChange={(e) => setAmount(e.target.value)} required className={`${fieldClass} w-40`} />
        </label>
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">Reason</span>
          <textarea value={reason} onChange={(e) => setReason(e.target.value)} rows={2} maxLength={500} required className={`${fieldClass} resize-y`} />
          <span className="text-xs text-muted">Kept with your name on the incident.</span>
        </label>
        <ErrorText error={credit.error} />
      </form>
    </Modal>
  );
}

/** The firm's own words to its traders, on its status page and in the terminal while the incident goes on. */
function NoteToTraders({ incident }: { incident: FirmIncident }) {
  const save = useIncidentNote(incident.id);
  const fieldId = useId();
  const [text, setText] = useState(incident.note ?? "");
  const changed = text.trim() !== (incident.note ?? "");
  return (
    <Panel title="Your note to traders">
      <form
        onSubmit={(event) => {
          event.preventDefault();
          save.mutate(text.trim(), { onSuccess: () => toast.success(text.trim() ? "Your note is published." : "Your note is removed.") });
        }}
        className="flex flex-col gap-3"
      >
        <label htmlFor={fieldId} className="text-sm text-muted">
          Shown under our text on your status page{incident.status === "Open" ? ", and in the terminal while the incident goes on" : ""}.
        </label>
        <textarea
          id={fieldId}
          value={text}
          onChange={(e) => setText(e.target.value)}
          rows={4}
          maxLength={2000}
          placeholder="For example: accounts that broke a limit because prices were missing are reinstated."
          className={`${fieldClass} resize-y`}
        />
        <ErrorText error={save.error} />
        <div className="flex justify-end">
          <button type="submit" disabled={!changed || save.isPending} className={buttonClass}>
            {save.isPending ? "Saving..." : text.trim() === "" && incident.note ? "Remove the note" : "Publish the note"}
          </button>
        </div>
      </form>
    </Panel>
  );
}

/** What was said and done, newest first: our updates and the firm's decisions. */
function WhatWasDone({ incident }: { incident: FirmIncident }) {
  const entries = [
    ...incident.updates.map((u, index) => ({
      at: u.at,
      text: index === 0 ? "We reported the incident. You were emailed." : u.status === "Resolved" ? `We marked it resolved: ${u.text}` : `We said: ${u.text}`,
    })),
    ...incident.decisions.map((d) => ({ at: d.decidedAt, text: `${d.decidedBy}: ${decisionText(d)}. Reason: ${d.reason}` })),
  ].sort((a, b) => Date.parse(b.at) - Date.parse(a.at));
  return (
    <Panel title="What was done">
      <ol className="flex flex-col gap-3 text-sm">
        {entries.map((entry) => (
          <li key={`${entry.at}-${entry.text}`} className="grid grid-cols-[7.5rem_minmax(0,1fr)] gap-3">
            <span className="text-muted tabular-nums">{formatDateTime(entry.at)}</span>
            <span className="leading-relaxed">{entry.text}</span>
          </li>
        ))}
      </ol>
    </Panel>
  );
}

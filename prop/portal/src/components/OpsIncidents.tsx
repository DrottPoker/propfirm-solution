"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useId, useState } from "react";
import { toast } from "sonner";

import type { IncidentKind, IncidentRequest, IncidentStatus, OpsIncident, OpsIncidents as Incidents } from "@/lib/api/types";
import { formatDateTime, timeZoneName } from "@/lib/format";
import { agoText, durationText, fromLocalInput, incidentKinds, kindLabels, periodText, statusLabels, statusTones, toLocalInput } from "@/lib/incidents";
import { useDismissIncident, useOpsFirms, useOpsIncidents, usePostIncidentUpdate, usePublishIncident, useSaveIncident } from "@/lib/opsQueries";

import { ConfirmDialog } from "./Dialog";
import { AlertIcon, IncidentIcon, PlusIcon } from "./icons";
import { AdminPage, Badge, buttonClass, EmptyState, ErrorText, fieldClass, Loading, Message, PageHeader, Panel, secondaryButtonClass } from "./ui";

// What a found outage says until we change it, the same as the platform writes.
const outageText =
  "No prices are coming from the price feed. New orders, closes and stop changes are refused until prices return, so nothing is filled at an old price.";

const localZone = () => timeZoneName(Intl.DateTimeFormat().resolvedOptions().timeZone);

/**
 * Our incidents (ADR 0053): outages the platform found by itself wait as drafts, and those we published show on the
 * firms' status pages and in their terminals. Beside them, how the price feed is doing now and which firms an outage
 * would reach.
 */
export function OpsIncidents() {
  const incidents = useOpsIncidents();
  if (incidents.isPending) {
    return <Loading label="Loading incidents" />;
  }

  if (incidents.isError) {
    return <Message text={incidents.error.message} />;
  }

  const data = incidents.data;
  const found = data.incidents.filter((i) => i.detected && i.status === "Draft");
  return (
    <AdminPage>
      <PageHeader
        title="Incidents"
        description="Outages of the trading platform. Firms see an incident once it is published: on their status page, in their traders' terminals and by email."
        actions={
          <Link href="/ops/incidents/new" className={`${buttonClass} flex items-center gap-1.5`}>
            <PlusIcon className="size-4" />
            Declare an incident
          </Link>
        }
      />
      {found.map((incident) => (
        <FoundAlert key={incident.id} incident={incident} />
      ))}
      <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,1fr)_20rem]">
        <section aria-label="Incidents" className="flex flex-col rounded-xl border border-border bg-panel shadow-card">
          {data.incidents.length === 0 ? (
            <EmptyState icon={<IncidentIcon className="size-6" />} title="No incidents in the last 90 days" />
          ) : (
            <ul className="flex flex-col">
              {data.incidents.map((incident) => (
                <li key={incident.id} className="border-t border-border first:border-t-0">
                  <Link href={`/ops/incidents/${incident.id}`} className="flex flex-col gap-1 px-5 py-4 hover:bg-background/50">
                    <span className="flex flex-wrap items-center gap-2.5">
                      <span className="font-medium">{incident.title}</span>
                      <Badge tone={statusTones[incident.status]}>{statusLabels[incident.status]}</Badge>
                      {incident.detected && <Badge tone="accent">Found by the platform</Badge>}
                    </span>
                    <span className="text-sm text-muted">
                      {kindLabels[incident.kind]} · {periodText(incident.startedAt, incident.endedAt)} {localZone()}
                      {incident.endedAt && <> · {durationText(incident.startedAt, incident.endedAt)}</>} ·{" "}
                      {incident.firms === null ? "Every firm" : `${incident.firms.length} ${incident.firms.length === 1 ? "firm" : "firms"}`}
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </section>
        <RightNow data={data} now={incidents.dataUpdatedAt} />
      </div>
    </AdminPage>
  );
}

function FoundAlert({ incident }: { incident: OpsIncident }) {
  return (
    <div role="alert" className="flex flex-wrap items-start gap-3 rounded-xl border border-warning/40 bg-warning/10 px-4 py-3.5">
      <AlertIcon className="mt-0.5 size-5 text-warning" />
      <div className="flex flex-1 flex-col gap-0.5 text-sm">
        <strong className="font-semibold text-warning">
          No prices from the feed since {formatDateTime(incident.startedAt)}
          {incident.endedAt ? `, back ${formatDateTime(incident.endedAt)}` : ""}
        </strong>
        <span>The platform noticed the gap by itself and emailed our staff. Firms see nothing until we publish it.</span>
      </div>
      <Link href={`/ops/incidents/${incident.id}`} className={`${buttonClass} text-sm`}>
        Check and publish
      </Link>
    </div>
  );
}

/** How the price feed is doing now, and the firms with traders in open positions. */
function RightNow({ data, now }: { data: Incidents; now: number }) {
  const accounts = data.firms.reduce((sum, f) => sum + f.accountsWithPositions, 0);
  return (
    <aside aria-label="What the platform sees" className="flex flex-col gap-4">
      <Panel title="Right now">
        {data.feed ? (
          <dl className="grid grid-cols-[minmax(0,1fr)_auto] gap-x-3 gap-y-2 text-sm">
            <dt className="text-muted">Price feed</dt>
            <dd>{data.feed.feed}</dd>
            <dt className="text-muted">Last price</dt>
            <dd className="font-mono tabular-nums">{data.feed.lastPriceAt ? agoText(data.feed.lastPriceAt, now) : "none yet"}</dd>
            <dt className="text-muted">Open markets without prices</dt>
            <dd className={data.feed.withoutPrices > 0 ? "text-warning" : ""}>
              {data.feed.withoutPrices} of {data.feed.openMarkets}
            </dd>
            <dt className="text-muted">Accounts with open positions</dt>
            <dd>{accounts}</dd>
          </dl>
        ) : (
          <p className="text-sm text-warning">The trading platform cannot be asked right now.</p>
        )}
      </Panel>
      <Panel title="Firms with open positions">
        {data.firms.length === 0 ? (
          <p className="text-sm text-muted">No trader has a position open.</p>
        ) : (
          <ul className="flex flex-col gap-1.5 text-sm">
            {data.firms.slice(0, 8).map((firm) => (
              <li key={firm.firmId} className="flex justify-between gap-3">
                <Link href={`/ops/firms/${encodeURIComponent(firm.firmId)}`} className="truncate hover:text-accent">
                  {firm.name}
                </Link>
                <span className="text-muted">
                  {firm.accountsWithPositions} {firm.accountsWithPositions === 1 ? "account" : "accounts"}
                </span>
              </li>
            ))}
          </ul>
        )}
      </Panel>
    </aside>
  );
}

/** A new incident, or one we have: what it says and whom it concerns, publishing it, and how it goes on. */
export function OpsIncident({ incidentId }: { incidentId: string | null }) {
  const incidents = useOpsIncidents();
  if (incidents.isPending) {
    return <Loading label="Loading the incident" />;
  }

  if (incidents.isError) {
    return <Message text={incidents.error.message} />;
  }

  const incident = incidentId === null ? null : (incidents.data.incidents.find((i) => i.id === incidentId) ?? null);
  if (incidentId !== null && incident === null) {
    return <Message text="There is no such incident, or it was dismissed." />;
  }

  return (
    <AdminPage>
      <PageHeader
        back={
          <nav aria-label="Breadcrumb" className="text-sm text-muted">
            <Link href="/ops/incidents" className="hover:text-foreground">
              Incidents
            </Link>{" "}
            / <span className="text-foreground">{incident?.title ?? "Declare an incident"}</span>
          </nav>
        }
        title={incident === null ? "Declare an incident" : incident.title}
        description={
          incident && (
            <span className="flex flex-wrap items-center gap-2">
              <Badge tone={statusTones[incident.status]}>{statusLabels[incident.status]}</Badge>
              {periodText(incident.startedAt, incident.endedAt, undefined, true)} {localZone()}
              {incident.publishedAt && <> · published {formatDateTime(incident.publishedAt)}</>}
            </span>
          )
        }
      />
      {incident?.detected && incident.status === "Draft" && <FoundAlert incident={incident} />}
      <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,1fr)_20rem]">
        <div className="flex flex-col gap-6">
          <IncidentForm key={incident?.id ?? "new"} incident={incident} />
          {incident?.publishedAt && <PostUpdate incident={incident} />}
          {incident && incident.updates.length > 0 && <Updates incident={incident} />}
        </div>
        <RightNow data={incidents.data} now={incidents.dataUpdatedAt} />
      </div>
    </AdminPage>
  );
}

function IncidentForm({ incident }: { incident: OpsIncident | null }) {
  const router = useRouter();
  const save = useSaveIncident(incident?.id ?? null);
  const publish = usePublishIncident();
  const firms = useOpsFirms("All", "");
  const ids = { kind: useId(), started: useId(), ended: useId(), title: useId(), text: useId(), note: useId() };
  const [kind, setKind] = useState<IncidentKind>(incident?.kind ?? "PriceFeedOutage");
  const [started, setStarted] = useState(toLocalInput(incident?.startedAt ?? new Date().toISOString()));
  const [ended, setEnded] = useState(incident?.endedAt ? toLocalInput(incident.endedAt) : "");
  const [title, setTitle] = useState(incident?.title ?? kindLabels.PriceFeedOutage);
  const [text, setText] = useState(incident?.publicText ?? outageText);
  const [note, setNote] = useState(incident?.internalNote ?? "");
  const [chosen, setChosen] = useState<string[] | null>(incident?.firms ?? null);
  const [publishing, setPublishing] = useState(false);
  const [dismissing, setDismissing] = useState(false);
  const allFirms = firms.data?.firms ?? [];
  const reach = chosen === null ? allFirms.filter((f) => f.status !== "Provisioning").length : chosen.length;

  const request = (): IncidentRequest | null => {
    const startedAt = fromLocalInput(started);
    return startedAt === null
      ? null
      : { kind, title: title.trim(), publicText: text.trim(), internalNote: note.trim(), startedAt, endedAt: fromLocalInput(ended), firms: chosen };
  };

  // Publishing saves the form first, so what firms see is what is on the screen.
  const saveAndPublish = async () => {
    const body = request();
    if (body === null) {
      return;
    }

    try {
      const saved = await save.mutateAsync(body);
      await publish.mutateAsync(saved.id);
      toast.success("The incident is published.");
      setPublishing(false);
      if (incident === null) {
        router.replace(`/ops/incidents/${saved.id}`);
      }
    } catch {
      // The form shows the error.
    }
  };

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    const body = request();
    if (body) {
      save.mutate(body, {
        onSuccess: (saved) => {
          toast.success(saved.publishedAt ? "The incident is changed. Terminals show the change." : "The draft is saved. Firms see nothing yet.");
          if (incident === null) {
            router.replace(`/ops/incidents/${saved.id}`);
          }
        },
      });
    }
  };

  return (
    <Panel>
      <form onSubmit={submit} className="flex flex-col gap-4">
        <div className="grid gap-3 sm:grid-cols-2">
          <label htmlFor={ids.kind} className="flex flex-col gap-1.5 text-sm">
            <span className="font-medium">What happened</span>
            <select
              id={ids.kind}
              value={kind}
              onChange={(e) => {
                const next = e.target.value as IncidentKind;
                if (title === kindLabels[kind]) {
                  setTitle(kindLabels[next]);
                }

                setKind(next);
              }}
              className={fieldClass}
            >
              {incidentKinds.map((k) => (
                <option key={k} value={k}>
                  {kindLabels[k]}
                </option>
              ))}
            </select>
          </label>
          <fieldset className="flex flex-col gap-1.5 text-sm">
            <legend className="mb-1.5 font-medium">Firms</legend>
            <label className="flex items-center gap-2">
              <input type="radio" checked={chosen === null} onChange={() => setChosen(null)} className="accent-[var(--accent)]" />
              Every firm
            </label>
            <label className="flex items-center gap-2">
              <input type="radio" checked={chosen !== null} onChange={() => setChosen(chosen ?? [])} className="accent-[var(--accent)]" />
              Chosen firms
            </label>
          </fieldset>
          <label htmlFor={ids.started} className="flex flex-col gap-1.5 text-sm">
            <span className="font-medium">Started, {localZone()}</span>
            <input id={ids.started} type="datetime-local" step={1} value={started} onChange={(e) => setStarted(e.target.value)} required className={`${fieldClass} font-mono`} />
            <span className="text-xs text-muted">The last price before the gap.</span>
          </label>
          <label htmlFor={ids.ended} className="flex flex-col gap-1.5 text-sm">
            <span className="font-medium">Ended, {localZone()}</span>
            <input id={ids.ended} type="datetime-local" step={1} value={ended} onChange={(e) => setEnded(e.target.value)} className={`${fieldClass} font-mono`} />
            <span className="text-xs text-muted">Empty while it goes on. Filled in when prices return.</span>
          </label>
        </div>
        {chosen !== null && (
          <fieldset className="flex max-h-48 flex-col gap-1.5 overflow-y-auto rounded-lg border border-border p-3 text-sm">
            <legend className="sr-only">Firms to publish to</legend>
            {allFirms.map((firm) => (
              <label key={firm.id} className="flex items-center gap-2">
                <input
                  type="checkbox"
                  checked={chosen.includes(firm.id)}
                  onChange={(e) => setChosen(e.target.checked ? [...chosen, firm.id] : chosen.filter((id) => id !== firm.id))}
                  className="accent-[var(--accent)]"
                />
                {firm.name} <span className="text-muted">{firm.id}</span>
              </label>
            ))}
          </fieldset>
        )}
        <label htmlFor={ids.title} className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">Title for the status pages</span>
          <input id={ids.title} value={title} onChange={(e) => setTitle(e.target.value)} maxLength={120} required className={fieldClass} />
        </label>
        <label htmlFor={ids.text} className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">What traders and firms read</span>
          <textarea id={ids.text} value={text} onChange={(e) => setText(e.target.value)} rows={4} maxLength={2000} required className={`${fieldClass} resize-y`} />
        </label>
        <label htmlFor={ids.note} className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">Note for staff only</span>
          <textarea id={ids.note} value={note} onChange={(e) => setNote(e.target.value)} rows={2} maxLength={2000} className={`${fieldClass} resize-y`} />
        </label>
        <ErrorText error={save.error ?? publish.error} />
        <div className="flex flex-wrap justify-end gap-2">
          {incident !== null && incident.publishedAt === null && (
            <button type="button" onClick={() => setDismissing(true)} className={`${secondaryButtonClass} mr-auto text-loss`}>
              Dismiss the draft
            </button>
          )}
          <button type="submit" disabled={save.isPending} className={incident?.publishedAt ? buttonClass : secondaryButtonClass}>
            {save.isPending ? "Saving..." : incident?.publishedAt ? "Save the changes" : "Save without publishing"}
          </button>
          {incident?.publishedAt == null && (
            <button type="button" disabled={chosen !== null && chosen.length === 0} onClick={() => setPublishing(true)} className={buttonClass}>
              {chosen === null ? "Publish to every firm" : `Publish to ${reach} ${reach === 1 ? "firm" : "firms"}`}
            </button>
          )}
        </div>
        {incident?.publishedAt == null && (
          <p className="text-right text-xs text-muted">
            Publishing shows it on each firm&apos;s status page and in their terminals, and emails each firm&apos;s administrators.
          </p>
        )}
      </form>
      <ConfirmDialog
        open={publishing}
        onClose={() => setPublishing(false)}
        onConfirm={saveAndPublish}
        title={`Publish "${title.trim()}"?`}
        description={`${chosen === null ? "Every firm sees" : `${reach} ${reach === 1 ? "firm sees" : "firms see"}`} it on their status page${ended ? "" : " and in their traders' terminals until it is resolved"}, and their administrators are emailed. It cannot be taken back, only resolved.`}
        confirmLabel="Publish"
        pendingLabel="Publishing..."
        pending={save.isPending || publish.isPending}
      >
        <ErrorText error={save.error ?? publish.error} />
      </ConfirmDialog>
      {incident && <DismissDialog incident={incident} open={dismissing} onClose={() => setDismissing(false)} />}
    </Panel>
  );
}

function DismissDialog({ incident, open, onClose }: { incident: OpsIncident; open: boolean; onClose: () => void }) {
  const dismiss = useDismissIncident(incident.id);
  const router = useRouter();
  return (
    <ConfirmDialog
      open={open}
      onClose={onClose}
      onConfirm={() =>
        dismiss.mutate(undefined, {
          onSuccess: () => {
            toast.success("The draft is dismissed.");
            router.replace("/ops/incidents");
          },
        })
      }
      title="Dismiss the draft?"
      description={
        incident.detected
          ? "For a false alarm. While the gap the platform found goes on, it is not found again."
          : "Nobody outside our staff has seen it. It is gone from the list."
      }
      confirmLabel="Dismiss"
      pendingLabel="Dismissing..."
      pending={dismiss.isPending}
      danger
    >
      <ErrorText error={dismiss.error} />
    </ConfirmDialog>
  );
}

/** Says how a published incident goes on, or that it is over. */
function PostUpdate({ incident }: { incident: OpsIncident }) {
  const post = usePostIncidentUpdate(incident.id);
  const fieldId = useId();
  const [status, setStatus] = useState<IncidentStatus>(incident.status === "Resolved" ? "Resolved" : "Open");
  const [text, setText] = useState("");
  return (
    <Panel title="Post an update">
      <form
        onSubmit={(event) => {
          event.preventDefault();
          post.mutate(
            { status, text: text.trim() },
            {
              onSuccess: () => {
                toast.success(status === "Resolved" ? "The incident is resolved. Terminals no longer show it." : "The update is published.");
                setText("");
              },
            },
          );
        }}
        className="flex flex-col gap-3"
      >
        <div role="radiogroup" aria-label="Where it is" className="flex flex-wrap gap-4 text-sm">
          {(["Open", "Resolved"] as const).map((s) => (
            <label key={s} className="flex items-center gap-2">
              <input type="radio" checked={status === s} onChange={() => setStatus(s)} className="accent-[var(--accent)]" />
              {s === "Open" ? "Still going on" : "Resolved"}
            </label>
          ))}
        </div>
        <label htmlFor={fieldId} className="sr-only">
          The update, as traders and firms read it
        </label>
        <textarea
          id={fieldId}
          value={text}
          onChange={(e) => setText(e.target.value)}
          rows={3}
          maxLength={2000}
          required
          placeholder={status === "Resolved" ? "For example: prices are back since 16:25:01." : "For example: the provider expects prices back within the hour."}
          className={`${fieldClass} resize-y`}
        />
        <ErrorText error={post.error} />
        <div className="flex justify-end">
          <button type="submit" disabled={post.isPending || text.trim() === ""} className={buttonClass}>
            {post.isPending ? "Publishing..." : status === "Resolved" ? "Resolve the incident" : "Publish the update"}
          </button>
        </div>
      </form>
    </Panel>
  );
}

function Updates({ incident }: { incident: OpsIncident }) {
  return (
    <Panel title="What we said">
      <ol className="flex flex-col gap-3 text-sm">
        {[...incident.updates].reverse().map((u) => (
          <li key={u.at} className="grid grid-cols-[7.5rem_minmax(0,1fr)] gap-3">
            <span className="flex flex-col">
              <span className={u.status === "Resolved" ? "text-profit" : "text-warning"}>{statusLabels[u.status]}</span>
              <span className="text-xs text-muted tabular-nums">{formatDateTime(u.at)}</span>
            </span>
            <span className="flex flex-col gap-0.5">
              <span className="leading-relaxed">{u.text}</span>
              {u.by && <span className="text-xs text-muted">{u.by}</span>}
            </span>
          </li>
        ))}
      </ol>
    </Panel>
  );
}

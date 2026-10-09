"use client";

import { formatBytes, formatClock, formatCount, formatCountOf, formatMilliseconds, formatRate, formatTime, formatWhen } from "@/lib/format";
import { useEngine } from "@/lib/queries";
import { useNow } from "@/lib/useNow";

import { Bars } from "./charts";
import { ErrorText, Facts, FigureCard, FigureGrid, Loading, PageHeader, Panel, Status } from "./ui";

const kinds: { kind: string; label: string }[] = [
  { kind: "Prices", label: "Prices" },
  { kind: "Orders", label: "Orders" },
  { kind: "Closes", label: "Closes" },
  { kind: "StopChanges", label: "Stop changes" },
  { kind: "OwnLimits", label: "Own limits and locks" },
  { kind: "FirmCommands", label: "From firms' systems" },
];

/** The engine loop, its journal, the terminals watching and the partners (ADR 0057). */
export function EnginePage() {
  const engine = useEngine();
  const now = useNow(5_000);

  if (engine.isPending) {
    return <Loading label="Loading the engine" />;
  }

  if (!engine.data) {
    return <ErrorText error={engine.error} />;
  }

  const data = engine.data;
  const minutes = data.minutes;
  const recent = minutes.slice(-5);
  const saves = recent.reduce((sum, m) => sum + m.saves, 0);
  const average = saves === 0 ? 0 : recent.reduce((sum, m) => sum + m.averageSaveMilliseconds * m.saves, 0) / saves;
  const slowest = Math.max(0, ...minutes.map((m) => m.slowestSaveMilliseconds));
  const maxQueue = Math.max(0, ...minutes.map((m) => m.maxQueue));
  const totals = kinds.map((k) => ({ ...k, count: minutes.reduce((sum, m) => sum + (m.inputs[k.kind] ?? 0), 0) }));
  const refused = minutes.reduce((sum, m) => sum + m.refused, 0);
  const most = Math.max(1, ...totals.map((t) => t.count));

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Engine"
        text="One queue, one engine in memory and a journal in Postgres. Every input is saved before anything is sent, so a restart picks up exactly where it stopped."
        actions={<Status tone={data.healthy ? "profit" : "loss"}>{data.healthy ? "Healthy" : data.journalFailed ? "The journal cannot be written" : "Starting"}</Status>}
      />

      <FigureGrid label="Right now" count={4}>
        <FigureCard label="Waiting in the queue" value={formatCount(data.queueLength)} sub={`${formatCount(maxQueue)} at most this hour`} />
        <FigureCard label="Time to save" value={formatMilliseconds(average)} sub={`On average, ${formatMilliseconds(slowest)} at worst this hour`} />
        <FigureCard label="Inputs per second" value={formatRate(data.inputsPerSecond)} sub={`${formatRate(data.pricesPerSecond)} of them prices`} />
        <FigureCard label="Terminals open" value={formatCount(data.terminalsOpen)} sub={`Watching ${formatCountOf(data.accountsWatched, "account")}`} />
      </FigureGrid>

      <Panel title="Time to save, minute by minute" aside="The slowest save each minute, last hour">
        <figure className="flex flex-col gap-1.5">
          <Bars values={minutes.map((m) => m.slowestSaveMilliseconds)} label="The slowest save of the journal each minute in the last hour" notice={(index) => minutes[index].slowestSaveMilliseconds >= 1000} height={80} />
          <figcaption className="flex justify-between text-xs text-muted">
            <span>{formatClock(minutes[0].start)}</span>
            <span>A second or more needs us</span>
            <span>{formatClock(minutes[minutes.length - 1].start)}</span>
          </figcaption>
        </figure>
      </Panel>

      <div className="grid items-start gap-4 lg:grid-cols-3">
        <Panel title="Journal">
          <Facts
            items={[
              { label: "Last input", value: <span className="font-mono text-[13px]">{formatCount(data.lastInput)}</span> },
              { label: "Last event", value: <span className="font-mono text-[13px]">{formatCount(data.lastEvent)}</span> },
              { label: "Size on disk", value: data.journalBytes === null ? "Not known" : formatBytes(data.journalBytes) },
              { label: "Grows by", value: data.journalBytesPerDay === null ? "Not known" : `About ${formatBytes(data.journalBytesPerDay)} a day` },
            ]}
          />
          <p className="text-sm text-muted">Every price is kept, and nothing is archived yet.</p>
        </Panel>

        <Panel title="Snapshots">
          <p className="text-sm leading-relaxed text-muted">
            One every {formatCount(data.snapshotInterval)} inputs, the last {formatCount(data.snapshotsKept)} kept. A restart loads the newest and replays what came after it.
          </p>
          <ul className="flex flex-col">
            {data.snapshots.map((snapshot) => (
              <li key={snapshot.inputSequence} className="flex justify-between gap-3 border-t border-border py-2">
                <span className="font-mono text-[13px]">{formatCount(snapshot.inputSequence)}</span>
                <span className="text-muted">{snapshot.createdAt ? formatTime(snapshot.createdAt) : "-"}</span>
              </li>
            ))}
          </ul>
        </Panel>

        <Panel title="This run">
          <Facts
            items={[
              { label: "Started", value: data.start ? formatWhen(data.start.at, now) : "Starting" },
              {
                label: "Start took",
                value: data.start ? `${(data.start.milliseconds / 1000).toFixed(1)} s, replaying ${formatCount(data.start.replayed)} inputs after the snapshot` : "-",
              },
              { label: "Version", value: <span className="break-all font-mono text-[13px]">{data.version}</span> },
              {
                label: "Configuration",
                value: data.start?.configurationChanged ? "Changed since the snapshot, and the inputs after it gave the same events" : "Same as the snapshot's",
              },
            ]}
          />
        </Panel>
      </div>

      <div className="grid items-start gap-4 lg:grid-cols-[3fr_2fr]">
        <Panel title="Inputs in the last hour">
          <ul className="flex flex-col gap-2.5">
            {totals.map((total) => (
              <li key={total.kind} className="grid grid-cols-[10rem_minmax(0,1fr)_5rem] items-center gap-3">
                <span>{total.label}</span>
                <span className="h-2 overflow-hidden rounded bg-raised">
                  <span className={`block h-full rounded ${total.kind === "Prices" ? "bg-accent" : "bg-brass"}`} style={{ width: `${Math.max(total.count > 0 ? 1 : 0, (total.count / most) * 100)}%` }} />
                </span>
                <span className="text-right text-muted">{formatCount(total.count)}</span>
              </li>
            ))}
            <li className="grid grid-cols-[10rem_minmax(0,1fr)_5rem] items-center gap-3">
              <span>Refused</span>
              <span className="h-2 overflow-hidden rounded bg-raised">
                <span className="block h-full rounded bg-warning" style={{ width: `${Math.max(refused > 0 ? 1 : 0, (refused / most) * 100)}%` }} />
              </span>
              <span className="text-right text-muted">{formatCount(refused)}</span>
            </li>
          </ul>
        </Panel>

        <Panel title="Partners">
          <p className="text-sm text-muted">Systems that make servers for others. Set in the configuration.</p>
          {data.partners.length === 0 ? (
            <p className="text-muted">None.</p>
          ) : (
            <ul className="flex flex-col">
              {data.partners.map((partner) => (
                <li key={partner.id} className="grid grid-cols-[minmax(0,1fr)_auto] gap-x-3 gap-y-0.5 border-t border-border py-2.5">
                  <span className="font-semibold">{partner.name}</span>
                  <span className="text-right text-muted">{formatCountOf(partner.servers, "server")}</span>
                  <span className="font-mono text-xs text-muted">{partner.id}</span>
                  <span className="text-right text-sm text-muted">{partner.lastCallAt ? `Last call ${formatWhen(partner.lastCallAt, now)}` : "No call since the start"}</span>
                </li>
              ))}
            </ul>
          )}
        </Panel>
      </div>
    </div>
  );
}

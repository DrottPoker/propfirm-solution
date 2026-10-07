"use client";

import Link from "next/link";

import type { ServicePart, StatusIncident, StatusPage as Status } from "@/lib/api/types";
import { formatDay, timeZoneName } from "@/lib/format";
import { partLabels, periodText, statusDays, statusLabels } from "@/lib/incidents";
import { useMe, useStatusPage } from "@/lib/queries";

import { FirmName } from "./FirmName";
import { AlertIcon, CheckIcon } from "./icons";
import { Loading, Message, secondaryButtonClass } from "./ui";

const parts: ServicePart[] = ["Trading", "Prices", "Terminal", "Portal"];

/**
 * The firm's status page, for anyone (ADR 0053): whether trading works now, each part of the service day by day for
 * 30 days, and the incidents of the last 90 days with what we said and the firm's own words.
 */
export function StatusPage() {
  const status = useStatusPage();
  const me = useMe("trader");
  if (status.isError) {
    return <Message text="The status cannot be loaded right now. Try again shortly." />;
  }

  if (!status.data) {
    return <Loading label="Loading the status" />;
  }

  const data = status.data;
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  const zone = timeZoneName(timeZone);
  return (
    <main className="mx-auto flex w-full max-w-4xl flex-col gap-8 px-4 py-6 sm:px-6 sm:py-8">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <FirmName size="lg" />
        <Link href={me.data ? "/" : "/login"} className={`${secondaryButtonClass} text-sm`}>
          {me.data ? "Your accounts" : "Log in"}
        </Link>
      </header>

      <div className="stagger flex flex-col gap-8">
        <div className="flex flex-col gap-1.5">
          <h1 className="font-serif text-[2.6rem] leading-[1.05] tracking-tight">Status</h1>
          <p className="text-muted">Whether trading at {data.firmName} works right now, and what has happened before.</p>
        </div>

        <Now status={data} zone={zone} />

        <section aria-labelledby="parts-heading" className="flex flex-col rounded-2xl border border-border bg-panel px-5 shadow-card">
          <h2 id="parts-heading" className="sr-only">
            Parts of the service
          </h2>
          {parts.map((part) => (
            <PartDays key={part} part={part} status={data} timeZone={timeZone} />
          ))}
          <p className="py-3.5 text-xs text-muted">Each bar is a day. Amber is a day with an incident.</p>
        </section>

        <section aria-labelledby="incidents-heading" className="flex flex-col gap-4">
          <h2 id="incidents-heading" className="text-xl font-semibold">
            Incidents
          </h2>
          {data.incidents.map((incident) => (
            <Incident key={incident.id} incident={incident} firmName={data.firmName} zone={zone} />
          ))}
          <p className="text-sm text-muted">{data.incidents.length === 0 ? "No incidents in the last 90 days." : "No other incidents in the last 90 days."}</p>
        </section>
      </div>
    </main>
  );
}

function Now({ status, zone }: { status: Status; zone: string }) {
  const checked = new Date(status.now).toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit" });
  const down = status.parts.filter((p) => !p.running).map((p) => partLabels[p.part].name.toLowerCase());
  return (
    <section
      aria-label="Now"
      className={`flex items-center gap-4 rounded-2xl border px-5 py-5 ${status.allRunning ? "border-profit/35 bg-profit/[0.07]" : "border-warning/40 bg-warning/10"}`}
    >
      <span className={`grid size-10 shrink-0 place-items-center rounded-full ${status.allRunning ? "bg-profit/15 text-profit" : "bg-warning/20 text-warning"}`}>
        {status.allRunning ? <CheckIcon className="size-5" /> : <AlertIcon className="size-5" />}
      </span>
      <div className="flex flex-col gap-0.5">
        <strong className="text-lg font-semibold">{status.allRunning ? "Everything is running" : `An incident affects ${down.join(" and ")}`}</strong>
        <span className="text-sm text-muted">
          Checked {checked} {zone}
        </span>
      </div>
    </section>
  );
}

function PartDays({ part, status, timeZone }: { part: ServicePart; status: Status; timeZone: string }) {
  const days = statusDays(status.incidents, part, new Date(status.now), timeZone);
  const running = status.parts.find((p) => p.part === part)?.running ?? true;
  const withIncidents = days.filter((d) => d.incidents.length > 0);
  const summary =
    withIncidents.length === 0
      ? "No incidents in 30 days"
      : `Incidents on ${withIncidents.length} of the last 30 days: ${withIncidents.map((d) => formatDay(d.date)).join(", ")}`;
  return (
    <div className="flex flex-col gap-2.5 border-b border-border py-4">
      <div className="flex flex-wrap justify-between gap-3">
        <span className="flex flex-col gap-0.5">
          <span className="font-semibold">{partLabels[part].name}</span>
          <span className="text-sm text-muted">{partLabels[part].about}</span>
        </span>
        <span className={`text-sm font-medium ${running ? "text-profit" : "text-warning"}`}>{running ? "Running" : "Incident"}</span>
      </div>
      <div role="img" aria-label={summary} className="grid grid-cols-[repeat(30,minmax(0,1fr))] gap-[3px]">
        {days.map((day) => (
          <span
            key={day.date}
            title={`${formatDay(day.date)}: ${day.incidents.length === 0 ? "no incidents" : day.incidents.join(", ")}`}
            className={`h-7 rounded-[3px] ${day.incidents.length === 0 ? "bg-profit/70" : "bg-warning"}`}
          />
        ))}
      </div>
      <div className="flex justify-between text-xs text-muted">
        <span>30 days ago</span>
        <span>Today</span>
      </div>
    </div>
  );
}

function Incident({ incident, firmName, zone }: { incident: StatusIncident; firmName: string; zone: string }) {
  const updates = [...incident.updates].reverse();
  return (
    <article className="flex flex-col gap-4 rounded-2xl border border-border bg-panel px-5 py-5 shadow-card">
      <div className="flex flex-wrap items-baseline justify-between gap-3">
        <h3 className="text-lg font-semibold">{incident.title}</h3>
        <span className="text-sm text-muted">
          {periodText(incident.startedAt, incident.endedAt)} {zone}
        </span>
      </div>
      <ol className="flex flex-col gap-3.5">
        {updates.map((update) => (
          <li key={update.at} className="grid gap-1 sm:grid-cols-[6rem_minmax(0,1fr)] sm:gap-4">
            <span className="flex gap-2 sm:flex-col sm:gap-0.5">
              <span className={`text-sm font-semibold ${update.status === "Resolved" ? "text-profit" : "text-warning"}`}>{statusLabels[update.status]}</span>
              <span className="font-mono text-xs text-muted tabular-nums">{new Date(update.at).toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit" })}</span>
            </span>
            <span className="leading-relaxed">{update.text}</span>
          </li>
        ))}
      </ol>
      <p className="text-xs text-muted">From the trading platform {firmName} uses.</p>
      {incident.firmNote && (
        <div className="flex flex-col gap-1 rounded-xl bg-background px-4 py-3.5">
          <span className="text-sm font-semibold">From {firmName}</span>
          <span className="leading-relaxed whitespace-pre-line text-foreground/85">{incident.firmNote}</span>
        </div>
      )}
    </article>
  );
}

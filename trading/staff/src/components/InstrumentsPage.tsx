"use client";

import { useState } from "react";

import type { StaffInstrument } from "@/lib/api/types";
import { closureText, zoneCity } from "@/lib/closures";
import { feedInText, formatCount, formatLots } from "@/lib/format";
import { useInstruments } from "@/lib/queries";
import { weekLabel, weekSegments } from "@/lib/week";

import { WarningIcon } from "./icons";
import { ErrorText, Loading, PageHeader, Panel, TableBox, tdClass, thClass } from "./ui";

const categories = ["Forex", "Metals", "Indices", "Commodities", "Crypto"] as const;

/** The instruments on the platform and when each can be traded (ADR 0057). Set in the service's configuration. */
export function InstrumentsPage() {
  const instruments = useInstruments();
  const [category, setCategory] = useState<string>("All");

  if (instruments.isPending) {
    return <Loading label="Loading the instruments" />;
  }

  if (!instruments.data) {
    return <ErrorText error={instruments.error} />;
  }

  const data = instruments.data;
  // By category, in the configuration's order within each.
  const sorted = [...data.instruments].sort((a, b) => categories.indexOf(a.category) - categories.indexOf(b.category));
  const shown = category === "All" ? sorted : sorted.filter((i) => i.category === category);
  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Instruments"
        text={`The ${formatCount(data.instruments.length)} instruments on the platform and when each can be traded. They are set in the service's configuration, so a change means a new release.`}
      />

      {!data.followsTradingHours && (
        <p className="rounded-xl border border-border bg-panel px-4 py-3 leading-relaxed text-muted">
          With {feedInText(data.feed)}, every market is always open, whatever the hours below say.
        </p>
      )}

      <div role="group" aria-label="Category" className="flex flex-wrap gap-1 self-start rounded-xl border border-border bg-panel p-1">
        {["All", ...categories].map((name) => {
          const count = name === "All" ? data.instruments.length : data.instruments.filter((i) => i.category === name).length;
          return count === 0 ? null : (
            <button
              key={name}
              type="button"
              aria-pressed={category === name}
              onClick={() => setCategory(name)}
              className={`h-9 rounded-lg px-3 text-sm ${category === name ? "bg-raised font-semibold text-foreground" : "text-muted hover:text-foreground"}`}
            >
              {name} <span className="font-normal text-muted">{formatCount(count)}</span>
            </button>
          );
        })}
      </div>

      <Panel title="Every instrument" aside={`Trading hours with ${feedInText(data.feed)}`}>
        <TableBox minWidth={980}>
          <thead>
            <tr className="text-right">
              <th scope="col" className={`${thClass} text-left`}>
                Symbol
              </th>
              <th scope="col" className={`${thClass} text-left`}>
                Currencies
              </th>
              <th scope="col" className={thClass}>
                Contract
              </th>
              <th scope="col" className={thClass}>
                Decimals
              </th>
              <th scope="col" className={thClass}>
                Lots
              </th>
              <th scope="col" className={`${thClass} text-left`}>
                Hours
              </th>
              <th scope="col" className={`${thClass} w-1/5 text-left`}>
                This week, Sunday to Saturday
              </th>
              <th scope="col" className={thClass}>
                Servers
              </th>
            </tr>
          </thead>
          <tbody>
            {shown.map((instrument) => (
              <InstrumentRow key={instrument.symbol} instrument={instrument} weekStart={data.weekStart} />
            ))}
          </tbody>
        </TableBox>
      </Panel>

      <div className="grid items-start gap-4 lg:grid-cols-[3fr_2fr]">
        <Panel title="Trading hours" aside="In the market's own time zone">
          {data.hours.length === 0 ? (
            <p className="text-muted">Every market is always open.</p>
          ) : (
            <TableBox minWidth={560}>
              <thead>
                <tr>
                  <th scope="col" className={thClass}>
                    Name
                  </th>
                  <th scope="col" className={thClass}>
                    Time zone
                  </th>
                  <th scope="col" className={thClass}>
                    Sessions
                  </th>
                  <th scope="col" className={`${thClass} text-right`}>
                    Symbols
                  </th>
                </tr>
              </thead>
              <tbody>
                {data.hours.map((hours) => (
                  <tr key={hours.name}>
                    <th scope="row" className={`${tdClass} font-mono text-xs font-medium`}>
                      {hours.name}
                    </th>
                    <td className={`${tdClass} text-muted`}>{zoneCity(hours.timeZone)}</td>
                    <td className={tdClass}>{hours.sessions.join(", ")}</td>
                    <td className={`${tdClass} text-right text-muted`} title={hours.symbols.join(", ")}>
                      {formatCount(hours.symbols.length)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </TableBox>
          )}
        </Panel>

        <Panel title="Closed days ahead">
          {data.closures.length === 0 ? (
            <p className="text-muted">None in the configuration.</p>
          ) : (
            <ul className="flex flex-col">
              {data.closures.map((closure, index) => (
                <li key={index} className="flex flex-col gap-0.5 border-t border-border py-2.5 first:border-t-0 first:pt-0">
                  <span className="font-semibold">{closureText(closure.from, closure.to)}</span>
                  <span className="text-sm text-muted">
                    {closure.hours} in {zoneCity(closure.timeZone)} time, {closure.symbols.length === 1 ? closure.symbols[0] : `${formatCount(closure.symbols.length)} symbols`}
                  </span>
                </li>
              ))}
            </ul>
          )}
          <div className="flex items-start gap-2.5 rounded-xl border border-warning/35 bg-warning/10 px-4 py-3">
            <WarningIcon className="mt-0.5 size-4 text-warning" />
            <span className="text-sm leading-relaxed">Only the holidays in the configuration are here. The full calendar, with early closes, must come from the licensed provider before launch.</span>
          </div>
        </Panel>
      </div>
    </div>
  );
}

function InstrumentRow({ instrument, weekStart }: { instrument: StaffInstrument; weekStart: string }) {
  const week = weekSegments(weekStart, instrument.thisWeek);
  return (
    <tr className="text-right">
      <th scope="row" className={`${tdClass} text-left font-normal`}>
        <span className="font-semibold">{instrument.symbol}</span> <span className="text-xs text-muted">{instrument.category}</span>
      </th>
      <td className={`${tdClass} text-left text-muted`}>
        {instrument.baseCurrency} / {instrument.quoteCurrency}
      </td>
      <td className={tdClass}>{formatCount(instrument.contractSize)}</td>
      <td className={`${tdClass} text-muted`}>{instrument.digits}</td>
      <td className={`${tdClass} text-muted`}>
        {formatLots(instrument.volumeMin)} to {formatCount(instrument.volumeMax)}
      </td>
      <td className={`${tdClass} text-left font-mono text-xs`}>{instrument.hours ?? <span className="font-sans text-sm text-muted">Always open</span>}</td>
      <td className={tdClass}>
        <span role="img" aria-label={weekLabel(week)} className="grid grid-cols-7 gap-0.5">
          {week.map((day, index) => (
            <span key={index} className="relative h-2.5 overflow-hidden rounded-sm bg-raised">
              {day.map((segment, part) => (
                <span key={part} className="absolute inset-y-0 bg-accent" style={{ left: `${segment.from * 100}%`, width: `${(segment.to - segment.from) * 100}%` }} />
              ))}
            </span>
          ))}
        </span>
      </td>
      <td className={`${tdClass} text-muted`}>{formatCount(instrument.servers)}</td>
    </tr>
  );
}

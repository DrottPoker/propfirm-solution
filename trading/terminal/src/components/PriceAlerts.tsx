"use client";

import { useState } from "react";

import { formatTime, timeZoneName } from "@/lib/format";
import { useNow } from "@/lib/marketHours";
import { ageText, feedStoppedFor, priceAgeMs, priceIsOld } from "@/lib/priceAge";
import { noticeLine } from "@/lib/notices";
import { useMarketHours, useNotice } from "@/lib/queries";
import { useTradingStore } from "@/lib/store";
import { useTimeZone } from "@/lib/timeZone";

import { InfoIcon, OldPriceIcon } from "./icons";

/**
 * Since when no price has come for any open market, and for how long, while connected. Null while prices come
 * (ADR 0053).
 */
export function useFeedStopped(accountId: string): { since: Date; ms: number } | null {
  const connected = useTradingStore((s) => s.connection === "connected");
  const receivedAt = useTradingStore((s) => s.receivedAt);
  const markets = useMarketHours(accountId).data;
  const now = useNow(5_000);
  const ms = connected && now ? feedStoppedFor(receivedAt, markets, now.getTime()) : null;
  return ms === null || !now ? null : { since: new Date(now.getTime() - ms), ms };
}

/** How long no price has come for the symbol, when its market is open and that is a minute or more. Null otherwise. */
export function useOldPrice(accountId: string, symbol: string, now: Date | null): number | null {
  const receivedAt = useTradingStore((s) => s.receivedAt);
  const markets = useMarketHours(accountId).data;
  if (!now || !priceIsOld(symbol, receivedAt, markets, now.getTime())) {
    return null;
  }

  return priceAgeMs(symbol, receivedAt, now.getTime());
}

/**
 * Under the account bar: that the price feed has stopped and what that means for orders, and the firm's notice for its
 * terminals, such as what it does about an outage (ADR 0053).
 */
export function PriceAlerts({ accountId }: { accountId: string }) {
  const stopped = useFeedStopped(accountId);
  const notice = useNotice(accountId).data ?? null;
  const timeZone = useTimeZone();
  const [expanded, setExpanded] = useState(false);

  if (stopped === null && !notice) {
    return null;
  }

  return (
    <div className="mx-2 mt-2 flex flex-col gap-2">
      {stopped !== null && (
        <div role="alert" className="flex animate-enter items-start gap-3 rounded-xl border border-warning/40 bg-warning/10 px-4 py-3 text-sm shadow-card">
          <OldPriceIcon className="mt-0.5 size-5 shrink-0 text-warning" />
          <div className="flex min-w-0 flex-col gap-0.5">
            <p className="font-semibold text-warning">
              No new prices since {formatTime(stopped.since, timeZone)} {timeZoneName(timeZone)}, {ageText(stopped.ms)} ago
            </p>
            <p>
              The price feed has stopped. Until prices return, new orders, closes and stop changes are refused, so nothing is filled at an old
              price. Stops and loss limits are checked again on the first new price.
            </p>
          </div>
        </div>
      )}
      {/* One line, so it does not push the chart down. A press on the text shows all of it when it does not fit. */}
      {notice && (
        <div
          role={notice.level === "Warning" ? "alert" : "status"}
          className={`flex animate-enter items-center gap-2.5 rounded-lg border px-3 py-1.5 text-sm ${notice.level === "Warning" ? "border-warning/40 bg-warning/10" : "border-border bg-panel"}`}
        >
          <InfoIcon className={`size-4 shrink-0 ${notice.level === "Warning" ? "text-warning" : "text-accent"}`} />
          <button
            type="button"
            aria-expanded={expanded}
            onClick={() => setExpanded((e) => !e)}
            title={expanded ? undefined : `${notice.title}: ${noticeLine(notice.text)}`}
            className={`min-w-0 flex-1 text-left ${expanded ? "" : "truncate"}`}
          >
            <span className="font-semibold">{notice.title}</span>
            <span className="text-muted"> · </span>
            {noticeLine(notice.text)}
          </button>
          {notice.url && (
            <a
              href={notice.url}
              target="_blank"
              rel="noreferrer"
              className="shrink-0 text-xs font-medium text-accent transition-colors duration-150 hover:text-foreground hover:underline"
            >
              Read more
            </a>
          )}
        </div>
      )}
    </div>
  );
}

/** A symbol's price that has not changed for a while, with how old it is: a small tag, or only a clock where space is short. */
export function OldPriceTag({ ageMs, compact = false }: { ageMs: number; compact?: boolean }) {
  const title = `No new price for ${ageText(ageMs)}. Orders are refused until one comes.`;
  if (compact) {
    return (
      <span title={title} className="flex items-center gap-0.5 text-[11px] text-warning">
        <OldPriceIcon />
        {ageText(ageMs)}
      </span>
    );
  }

  return (
    <span title={title} className="rounded border border-warning/40 px-1 py-px text-[10px] font-medium tracking-wide text-warning uppercase">
      No prices
    </span>
  );
}

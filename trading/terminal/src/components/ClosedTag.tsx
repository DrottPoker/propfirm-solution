"use client";

import type { MarketHours } from "@/lib/api/types";
import { timeZoneName } from "@/lib/format";
import { opensText } from "@/lib/marketHours";
import { useTimeZone } from "@/lib/timeZone";

import { MarketClosedIcon } from "./icons";

/**
 * A closed market, with when it opens on hover: a small grey tag, or only a moon where space is short, such as a row
 * of the watchlist.
 */
export function ClosedTag({ market, compact = false }: { market: MarketHours; compact?: boolean }) {
  const timeZone = useTimeZone();
  const title = `The market is closed. ${opensText(market, timeZone, timeZoneName(timeZone))}.`;
  if (compact) {
    return (
      <span title={title} className="text-muted">
        <MarketClosedIcon />
        <span className="sr-only">Market closed</span>
      </span>
    );
  }

  return (
    <span title={title} className="text-[11px] font-medium text-muted">
      Closed
    </span>
  );
}

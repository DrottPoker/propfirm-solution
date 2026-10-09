"use client";

import { useNow } from "@/lib/marketHours";
import { ageText, feedStoppedFor, priceAgeMs, priceIsOld } from "@/lib/priceAge";
import { useMarketHours } from "@/lib/queries";
import { useTradingStore } from "@/lib/store";

import { OldPriceIcon } from "./icons";

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
    <span title={title} className="text-[11px] font-medium text-warning">
      No prices
    </span>
  );
}

import { orderLockText, tradesUsed, tradesUsedText } from "./ownLimits";
import { isClosed, useNow } from "./marketHours";
import { priceAgeMs, priceTooOld } from "./priceAge";
import { useMarket, useMarketHours, useMe } from "./queries";
import { useTradingStore } from "./store";

/**
 * Why no order for a symbol is taken now, whatever is typed: no connection to the trading service, no prices yet, an
 * account that takes no orders, a closed market, an old price (ADR 0053), or the trader's own lock or trades a day
 * (ADR 0054).
 */
export type OrderBlock = "offline" | "waiting" | "inactive" | "closed" | "stale" | "locked" | "trades";

/** Why the symbol takes no new orders now, or null, and how old its last price is. */
export function useOrderBlock(accountId: string, symbol: string | null): { block: OrderBlock | null; priceAge: number | null } {
  const connected = useTradingStore((s) => s.connection === "connected");
  const hasQuote = useTradingStore((s) => symbol !== null && s.prices[symbol] !== undefined);
  const status = useTradingStore((s) => s.account?.status);
  const own = useTradingStore((s) => (s.account?.status === "Active" ? s.account.ownLimits : null));
  const closed = isClosed(useMarket(accountId, symbol));
  // Orders are refused while the latest price is older than the service allows, so they are not offered.
  const maxPriceAgeMs = (useMe().data?.maxPriceAgeSeconds ?? 5) * 1000;
  const receivedAt = useTradingStore((s) => s.receivedAt);
  const markets = useMarketHours(accountId).data;
  const now = useNow(1_000);
  const tooOld = now !== null && symbol !== null && priceTooOld(symbol, receivedAt, markets, maxPriceAgeMs, now.getTime());
  const priceAge = now === null || symbol === null ? null : priceAgeMs(symbol, receivedAt, now.getTime());

  const block: OrderBlock | null = !connected
    ? "offline"
    : !hasQuote
      ? "waiting"
      : status !== "Active"
        ? "inactive"
        : closed
          ? "closed"
          : tooOld
            ? "stale"
            : own?.lock
              ? "locked"
              : own && tradesUsed(own)
                ? "trades"
                : null;
  return { block, priceAge };
}

/** The block in one sentence, for a button's tooltip or a menu. */
export function orderBlockText(block: OrderBlock, symbol: string): string {
  const own = useTradingStore.getState().account?.ownLimits;
  switch (block) {
    case "offline":
      return "Not connected to the trading service. Orders wait until the connection is back.";
    case "waiting":
      return "Waiting for prices.";
    case "inactive":
      return "This account takes no new orders.";
    case "closed":
      return `The ${symbol} market is closed.`;
    case "stale":
      return `No new ${symbol} price. Orders wait for one, so nothing fills at an old price.`;
    case "locked":
      return own?.lock ? orderLockText(own.lock, own.tradingDay.timeZone) : "Trading is locked for the rest of the day.";
    case "trades":
      return own ? tradesUsedText(own, own.tradingDay.timeZone) : "You have opened your trades for today.";
  }
}

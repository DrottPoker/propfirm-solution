"use client";

import type { InstrumentInfo, MarketPeriod } from "@/lib/api/types";
import { formatMoney, formatUnits, timeZoneName } from "@/lib/format";
import { isClosed, sessionLines } from "@/lib/marketHours";
import { pipSize } from "@/lib/orderInput";
import { useMarket } from "@/lib/queries";
import { distanceUnit, markupPips, pipsText } from "@/lib/ticket";
import { useTimeZone } from "@/lib/timeZone";

/**
 * An instrument's trading conditions on the account (ADR 0058): leverage, the markup on the spread, commission, the
 * contract, what a pip is, and the trading hours this week. In the order ticket's Conditions and under a short
 * watchlist.
 */
export function InstrumentConditions({ accountId, instrument }: { accountId: string; instrument: InstrumentInfo }) {
  const market = useMarket(accountId, instrument.symbol);
  const timeZone = useTimeZone();
  const unitName = distanceUnit(instrument);

  return (
    <div className="flex flex-col gap-1.5 text-xs">
      <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1.5">
        <dt className="text-muted">Leverage</dt>
        <dd className="text-right">1:{instrument.leverage}</dd>
        <dt className="text-muted">Markup on the spread</dt>
        <dd className="text-right">{pipsText(markupPips(instrument.spreadMarkupPoints, instrument.digits), unitName)}</dd>
        <dt className="text-muted">Commission</dt>
        <dd className="text-right">{formatMoney(instrument.commissionPerLotPerSide)} per lot and side</dd>
        <dt className="text-muted">Contract</dt>
        <dd className="text-right">
          {formatUnits(instrument.contractSize)} {instrument.baseCurrency} a lot
        </dd>
        <dt className="text-muted">One {unitName.slice(0, -1)}</dt>
        <dd className="text-right">{pipSize(instrument.digits).toFixed(Math.max(0, instrument.digits - 1))}</dd>
      </dl>
      <TradingHours sessions={market?.sessions ?? null} known={market !== undefined} closed={isClosed(market)} timeZone={timeZone} />
    </div>
  );
}

/**
 * Whether the market is open, and the periods it is open this week in the account's time zone. A market that never
 * closes, like crypto, says so.
 */
function TradingHours({ sessions, known, closed, timeZone }: { sessions: MarketPeriod[] | null; known: boolean; closed: boolean; timeZone: string }) {
  const state = !known ? "-" : sessions === null ? "Around the clock" : closed ? "Closed" : "Open";
  return (
    <div className="flex flex-col gap-1 border-t border-border pt-2">
      <p className="flex justify-between gap-3">
        <span className="text-muted">Trading hours</span>
        <span>{state}</span>
      </p>
      {sessions && sessions.length > 0 && (
        <ul className="flex flex-col gap-0.5 text-right text-[11px] text-muted" aria-label={`Open periods, ${timeZoneName(timeZone)}`}>
          {sessionLines(sessions, timeZone).map((line) => (
            <li key={line}>{line}</li>
          ))}
        </ul>
      )}
    </div>
  );
}

"use client";

import type { InstrumentInfo } from "@/lib/api/types";
import { formatPrice } from "@/lib/format";
import { useTradingStore } from "@/lib/store";

export function Watchlist({
  instruments,
  selected,
  onSelect,
}: {
  instruments: InstrumentInfo[];
  selected: string | null;
  onSelect: (symbol: string) => void;
}) {
  const prices = useTradingStore((s) => s.prices);

  return (
    <aside className="overflow-y-auto bg-panel">
      <table className="w-full text-sm">
        <thead className="text-xs text-muted">
          <tr>
            <th className="px-2 py-2 text-left font-normal">Symbol</th>
            <th className="px-2 py-2 text-right font-normal">Bid</th>
            <th className="px-2 py-2 text-right font-normal">Ask</th>
          </tr>
        </thead>
        <tbody>
          {instruments.map((instrument) => {
            const price = prices[instrument.symbol];
            const isSelected = instrument.symbol === selected;
            return (
              <tr
                key={instrument.symbol}
                className={`cursor-pointer ${isSelected ? "bg-accent/15" : "hover:bg-white/5"}`}
                onClick={() => onSelect(instrument.symbol)}
              >
                <td className="px-2 py-1.5">
                  <button type="button" className="text-left" aria-pressed={isSelected}>
                    {instrument.symbol}
                  </button>
                </td>
                <td className="px-2 py-1.5 text-right font-mono tabular-nums">{formatPrice(price?.bid, instrument.digits)}</td>
                <td className="px-2 py-1.5 text-right font-mono tabular-nums">{formatPrice(price?.ask, instrument.digits)}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </aside>
  );
}

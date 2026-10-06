"use client";

import { useState } from "react";

import type { InstrumentInfo } from "@/lib/api/types";
import { dayFigures } from "@/lib/daySummary";
import { loadFavorites, saveFavorites, toggleFavorite } from "@/lib/favorites";
import { formatPrice, formatSignedPercent } from "@/lib/format";
import { categoriesOf, categoryOf, type Category } from "@/lib/instruments";
import { useDaySummary } from "@/lib/queries";
import { useTradingStore, type PriceMove } from "@/lib/store";

import { SearchIcon, StarIcon } from "./icons";
import { Sparkline } from "./Sparkline";

type Filter = "All" | Category | "Favorites";

export function Watchlist({
  accountId,
  instruments,
  selected,
  onSelect,
}: {
  accountId: string;
  instruments: InstrumentInfo[];
  selected: string | null;
  onSelect: (symbol: string) => void;
}) {
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState<Filter>("All");
  const [favorites, setFavorites] = useState(loadFavorites);

  const filters: Filter[] = ["All", ...categoriesOf(instruments), "Favorites"];
  const query = search.trim().toUpperCase();
  const shown = instruments.filter(
    (i) =>
      i.symbol.includes(query) &&
      (filter === "All" || (filter === "Favorites" ? favorites.includes(i.symbol) : categoryOf(i) === filter)),
  );

  const onToggleFavorite = (symbol: string) => {
    const next = toggleFavorite(favorites, symbol);
    setFavorites(next);
    saveFavorites(next);
  };

  return (
    <aside className="flex min-h-0 flex-col overflow-hidden rounded-xl border border-border bg-panel shadow-card">
      <div className="flex flex-col gap-3 p-3">
        <h2 className="font-semibold">Watchlist</h2>
        <label className="flex items-center gap-2 rounded-lg border border-border bg-raised px-2.5 py-1.5 text-muted transition-colors duration-150 focus-within:border-accent">
          <SearchIcon className="size-4 shrink-0" />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search symbols..."
            aria-label="Search symbols"
            className="w-full min-w-0 bg-transparent text-sm text-foreground outline-none placeholder:text-muted"
          />
        </label>
        <div className="flex gap-1 text-xs" role="group" aria-label="Show symbols">
          {filters.map((f) => (
            <button
              key={f}
              type="button"
              onClick={() => setFilter(f)}
              aria-pressed={f === filter}
              className={`flex-1 rounded-md px-2 py-1.5 transition duration-150 active:translate-y-px ${f === filter ? "bg-accent/15 font-medium text-accent" : "text-muted hover:bg-raised hover:text-foreground"}`}
            >
              {f}
            </button>
          ))}
        </div>
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto">
        {shown.length === 0 ? (
          <p className="px-3 py-4 text-sm text-muted">
            {filter === "Favorites" && query === "" ? "Star a symbol to keep it here." : "No symbols match."}
          </p>
        ) : (
          <table className="w-full text-xs">
            <thead className="sticky top-0 bg-panel text-[11px] text-muted">
              <tr>
                <th className="py-1.5 pl-3 text-left font-normal">Symbol</th>
                <th className="px-1 py-1.5 text-right font-normal">Bid</th>
                <th className="px-1 py-1.5 text-right font-normal">Ask</th>
                <th className="px-1 py-1.5 text-right font-normal">24h</th>
                <th className="pr-3 font-normal">
                  <span className="sr-only">Last 24 hours</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {shown.map((instrument) => (
                <Row
                  key={instrument.symbol}
                  accountId={accountId}
                  instrument={instrument}
                  isSelected={instrument.symbol === selected}
                  isFavorite={favorites.includes(instrument.symbol)}
                  onSelect={onSelect}
                  onToggleFavorite={onToggleFavorite}
                />
              ))}
            </tbody>
          </table>
        )}
      </div>
    </aside>
  );
}

function Row({
  accountId,
  instrument,
  isSelected,
  isFavorite,
  onSelect,
  onToggleFavorite,
}: {
  accountId: string;
  instrument: InstrumentInfo;
  isSelected: boolean;
  isFavorite: boolean;
  onSelect: (symbol: string) => void;
  onToggleFavorite: (symbol: string) => void;
}) {
  const { symbol, digits } = instrument;
  const price = useTradingStore((s) => s.prices[symbol]);
  const move = useTradingStore((s) => s.moves[symbol]);
  const day = dayFigures(useDaySummary(accountId, symbol).data, price?.bid);

  return (
    <tr
      className={`cursor-pointer border-l-2 transition-colors duration-150 ${isSelected ? "border-accent bg-accent/10" : "border-transparent hover:bg-raised"}`}
      onClick={() => onSelect(symbol)}
    >
      <td className="py-2 pl-2.5">
        <span className="flex items-center gap-1.5">
          <button
            type="button"
            aria-label={isFavorite ? `Remove ${symbol} from favorites` : `Add ${symbol} to favorites`}
            aria-pressed={isFavorite}
            onClick={(e) => {
              e.stopPropagation();
              onToggleFavorite(symbol);
            }}
            className={`transition-colors duration-150 ${isFavorite ? "text-accent" : "text-muted/60 hover:text-foreground"}`}
          >
            <StarIcon filled={isFavorite} />
          </button>
          <button type="button" className="font-medium" aria-pressed={isSelected}>
            {symbol}
          </button>
        </span>
      </td>
      <td className={`px-1 py-2 text-right font-mono tabular-nums ${move === "up" ? "text-profit" : move === "down" ? "text-loss" : ""}`}>
        <Flash bid={price?.bid} move={move}>
          {formatPrice(price?.bid, digits)}
        </Flash>
      </td>
      <td className="px-1 py-2 text-right font-mono tabular-nums">
        <Flash bid={price?.bid} move={move}>
          {formatPrice(price?.ask, digits)}
        </Flash>
      </td>
      <td
        className={`px-1 py-2 text-right font-mono tabular-nums ${day.change === null ? "text-muted" : day.change >= 0 ? "text-profit" : "text-loss"}`}
        title={day.change === null ? "Less than 24 hours of prices" : "Change of the bid over 24 hours"}
      >
        {formatSignedPercent(day.changePercent)}
      </td>
      <td className="py-2 pr-3">
        <Sparkline points={day.points} />
      </td>
    </tr>
  );
}

const flashes: Record<PriceMove, string> = { up: "animate-flash-up", down: "animate-flash-down" };

/**
 * A price that lights up briefly green when the bid rose and red when it fell. Keyed by the bid, so each new bid
 * starts the flash again; the row renders on every price anyway, so this costs no extra renders.
 */
function Flash({ bid, move, children }: { bid: number | undefined; move: PriceMove | undefined; children: React.ReactNode }) {
  return (
    <span key={bid} className={`-mx-1 inline-block rounded px-1 ${move ? flashes[move] : ""}`}>
      {children}
    </span>
  );
}

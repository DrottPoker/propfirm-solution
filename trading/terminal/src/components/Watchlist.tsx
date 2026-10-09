"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import type { InstrumentInfo } from "@/lib/api/types";
import { dayFigures, type DaySummary } from "@/lib/daySummary";
import { formatPrice, formatSignedPercent } from "@/lib/format";
import { categoriesOf, type Category } from "@/lib/instruments";
import { isClosed, useNow } from "@/lib/marketHours";
import { useDaySummary, useMarket } from "@/lib/queries";
import { useSettings } from "@/lib/settings";
import { useTradingStore, type PriceMove } from "@/lib/store";
import { readSetting, writeSetting } from "@/lib/syncedSettings";
import { pipsOf } from "@/lib/ticket";
import { instrumentsOf, matchesSearch, maxListName, moveSymbol, nextSort, useWatchlists, type ListChoice, type WatchlistSort } from "@/lib/watchlists";

import { ClosedTag } from "./ClosedTag";
import { ChevronDownIcon, CloseIcon, MoreIcon, SearchIcon, StarIcon } from "./icons";
import { InstrumentConditions } from "./InstrumentConditions";
import { Menu } from "./Menu";
import { OldPriceTag, useOldPrice } from "./PriceAlerts";
import { Sparkline } from "./Sparkline";

const choiceKey = "trading.watchlistList";

// A list with this many instruments or fewer leaves room under it for the chosen one's conditions (ADR 0058).
const shortList = 8;

function encode(choice: ListChoice): string {
  return choice.kind === "category" ? `category:${choice.category}` : choice.kind === "own" ? `own:${choice.id}` : choice.kind;
}

function decode(value: string | null): ListChoice {
  if (value === "favorites") {
    return { kind: "favorites" };
  }

  if (value?.startsWith("category:")) {
    return { kind: "category", category: value.slice(9) as Category };
  }

  return value?.startsWith("own:") ? { kind: "own", id: value.slice(4) } : { kind: "all" };
}

/**
 * The instruments to trade, by list: every one, a category, the starred favorites or the trader's own lists, each
 * searched by symbol or name (ADR 0058). Prices keep fixed columns and only their colour moves for a moment when they
 * change. The favorites and own lists keep the trader's order, which rows are dragged into; any list sorts by a
 * column's heading. Under a short list, the chosen instrument's conditions fill the space.
 */
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
  const [choice, setChoice] = useState<ListChoice>(() => decode(readSetting(choiceKey)));
  const [sort, setSort] = useState<WatchlistSort>(null);
  const [naming, setNaming] = useState<{ id: string | null; name: string } | null>(null);
  const [confirmingRemove, setConfirmingRemove] = useState(false);
  const favorites = useWatchlists((s) => s.favorites);
  const lists = useWatchlists((s) => s.lists);
  const store = useWatchlists();
  const showSpread = useSettings((s) => s.watchlistSpread);
  const showChart = useSettings((s) => s.watchlistChart);
  const priceTicks = useSettings((s) => s.priceTicks);
  const changeSetting = useSettings((s) => s.change);
  const queryClient = useQueryClient();
  // One clock for every row, for the age of prices that stopped coming, and for sorting by moving figures.
  const now = useNow(5_000);
  const [dragging, setDragging] = useState<string | null>(null);
  const [dropBefore, setDropBefore] = useState<string | null | undefined>(undefined);

  const categories = categoriesOf(instruments);
  const ownList = choice.kind === "own" ? lists.find((l) => l.id === choice.id) : undefined;
  // A list that is gone, for example deleted on another device, falls back to every instrument.
  const current: ListChoice = choice.kind === "own" && !ownList ? { kind: "all" } : choice;
  const listName =
    current.kind === "all"
      ? "All instruments"
      : current.kind === "category"
        ? current.category
        : current.kind === "favorites"
          ? "Favorites"
          : (ownList?.name ?? "");
  const ordered = current.kind === "favorites" || current.kind === "own";

  const choose = (next: ListChoice) => {
    setChoice(next);
    setSort(null);
    setConfirmingRemove(false);
    writeSetting(choiceKey, encode(next));
  };

  const rows = instrumentsOf(current, instruments, favorites, lists).filter((i) => matchesSearch(i, search));
  const shown = sort ? sortRows(rows, sort, (symbol) => queryClient.getQueryData<DaySummary>(["day", accountId, symbol])) : rows;
  const canDrag = ordered && sort === null && search.trim() === "";
  const selectedInstrument = instruments.find((i) => i.symbol === selected) ?? null;

  const symbolsOfList = (): string[] => (current.kind === "favorites" ? favorites : (ownList?.symbols ?? []));
  const saveOrder = (symbols: string[]) => (current.kind === "favorites" ? store.setFavorites(symbols) : ownList && store.setListSymbols(ownList.id, symbols));
  const toggleFavorite = (symbol: string) => store.setFavorites(favorites.includes(symbol) ? favorites.filter((s) => s !== symbol) : [...favorites, symbol]);
  const toggleInList = (id: string, symbol: string) => {
    const list = lists.find((l) => l.id === id);
    if (list) {
      store.setListSymbols(id, list.symbols.includes(symbol) ? list.symbols.filter((s) => s !== symbol) : [...list.symbols, symbol]);
    }
  };

  const saveName = () => {
    const name = naming?.name.trim() ?? "";
    if (!naming || name === "") {
      setNaming(null);
      return;
    }

    if (naming.id) {
      store.renameList(naming.id, name);
    } else {
      choose({ kind: "own", id: store.createList(name) });
    }
    setNaming(null);
  };

  const listItems = [
    { value: "all", label: "All instruments" },
    ...categories.map((c) => ({ value: `category:${c}`, label: c })),
    { value: "favorites", label: "Favorites", hint: favorites.length === 0 ? "Star an instrument to keep it here" : undefined },
    ...lists.map((l) => ({ value: `own:${l.id}`, label: l.name, hint: `${l.symbols.length} ${l.symbols.length === 1 ? "instrument" : "instruments"}` })),
    { value: "new", label: "New list", action: true },
    ...(ownList
      ? [
          { value: "rename", label: `Rename ${ownList.name}`, action: true },
          { value: "remove", label: `Delete ${ownList.name}`, action: true },
        ]
      : []),
  ];

  return (
    <aside aria-label="Watchlist" className="@container flex min-h-0 flex-col overflow-hidden bg-panel">
      <div className="flex flex-col gap-2 px-3 pt-3 pb-2">
        <div className="flex items-center justify-between gap-2">
          <Menu
            label="Lists"
            buttonLabel={`Watchlist: ${listName}`}
            title="Choose a list"
            button={
              <span className="flex min-w-0 items-center gap-1.5">
                <span className="truncate font-semibold">{listName}</span>
                <ChevronDownIcon className="size-3.5 shrink-0 text-muted" />
              </span>
            }
            items={listItems}
            value={encode(current)}
            onChoose={(value) => {
              if (value === "new") {
                setNaming({ id: null, name: "" });
              } else if (value === "rename" && ownList) {
                setNaming({ id: ownList.id, name: ownList.name });
              } else if (value === "remove") {
                setConfirmingRemove(true);
              } else {
                choose(decode(value));
              }
            }}
            className="-ml-1 flex min-w-0 items-center rounded-md px-1 py-0.5 transition-colors duration-150 hover:bg-raised"
          />
          <Menu
            label="Columns"
            buttonLabel="Watchlist columns"
            title="Columns and colours"
            button={<MoreIcon className="size-4" />}
            align="end"
            items={[
              {
                value: "spread",
                label: showSpread ? "Hide the spread" : "Show the spread",
                hint: "In pips. A narrow watchlist shows it instead of the change",
                action: true,
              },
              {
                value: "chart",
                label: showChart ? "Hide the 24 hour chart" : "Show the 24 hour chart",
                hint: "Shown when the watchlist is wide enough",
                action: true,
              },
              { value: "ticks", label: priceTicks ? "Keep prices one colour" : "Colour prices as they move", action: true },
            ]}
            onChoose={(value) =>
              value === "spread"
                ? changeSetting("watchlistSpread", !showSpread)
                : value === "chart"
                  ? changeSetting("watchlistChart", !showChart)
                  : changeSetting("priceTicks", !priceTicks)
            }
            className="flex size-7 shrink-0 items-center justify-center rounded-md text-muted transition-colors duration-150 hover:bg-raised hover:text-foreground"
          />
        </div>

        {naming ? (
          <form
            className="flex items-center gap-2"
            onSubmit={(e) => {
              e.preventDefault();
              saveName();
            }}
          >
            <input
              autoFocus
              value={naming.name}
              maxLength={maxListName}
              onChange={(e) => setNaming({ ...naming, name: e.target.value })}
              onKeyDown={(e) => e.key === "Escape" && setNaming(null)}
              placeholder="Name of the list"
              aria-label="Name of the list"
              className="h-8 min-w-0 flex-1 rounded-lg border border-accent bg-raised px-2.5 text-sm outline-none"
            />
            <button type="submit" className="h-8 rounded-lg bg-accent px-3 text-xs font-semibold text-accent-foreground">
              {naming.id ? "Save" : "Create"}
            </button>
          </form>
        ) : confirmingRemove && ownList ? (
          <div role="alertdialog" aria-label={`Delete ${ownList.name}`} className="flex items-center gap-2 text-xs">
            <span className="min-w-0 flex-1 truncate text-muted">Delete the list {ownList.name}?</span>
            <button
              type="button"
              onClick={() => {
                store.removeList(ownList.id);
                choose({ kind: "all" });
              }}
              className="h-7 rounded-md bg-loss/15 px-2.5 font-medium text-loss"
            >
              Delete
            </button>
            <button type="button" onClick={() => setConfirmingRemove(false)} className="h-7 rounded-md px-2.5 font-medium text-muted hover:text-foreground">
              Keep
            </button>
          </div>
        ) : (
          <label className="flex h-8 items-center gap-2 rounded-lg border border-border bg-raised px-2.5 text-muted transition-colors duration-150 focus-within:border-accent">
            <SearchIcon className="size-4 shrink-0" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search name or symbol"
              aria-label="Search name or symbol"
              className="w-full min-w-0 bg-transparent text-sm text-foreground outline-none placeholder:text-muted"
            />
            {search && (
              <button type="button" aria-label="Clear the search" onClick={() => setSearch("")} className="text-muted hover:text-foreground">
                <CloseIcon className="size-3.5" />
              </button>
            )}
          </label>
        )}
      </div>

      <div className={`min-h-0 overflow-y-auto ${shown.length <= shortList ? "shrink-0" : "flex-1"}`}>
        {shown.length === 0 ? (
          <p className="px-3 py-4 text-sm text-muted">
            {search.trim() !== ""
              ? "Nothing matches the search."
              : current.kind === "favorites"
                ? "Star an instrument to keep it here."
                : current.kind === "own"
                  ? "Add instruments from any list with the menu on their row."
                  : "No instruments."}
          </p>
        ) : (
          <table className="w-full table-fixed text-xs">
            <colgroup>
              <col />
              <col className="w-16" />
              <col className="w-16" />
              {/* A narrow watchlist that shows the spread leaves out the change over 24 hours instead. */}
              {showSpread && <col className="w-10" />}
              <col className={`w-12 ${showSpread ? "@max-[18rem]:hidden" : ""}`} />
              {showChart && <col className="w-16 @max-[21rem]:hidden" />}
              <col className="w-0" />
            </colgroup>
            <thead className="sticky top-0 z-[1] bg-panel text-[11px] text-muted">
              <tr>
                <SortHeading label="Symbol" column="symbol" sort={sort} onSort={setSort} className="pl-3 text-left" />
                <th scope="col" className="px-1 py-1.5 text-right font-normal">
                  Bid
                </th>
                <th scope="col" className="px-1 py-1.5 text-right font-normal">
                  Ask
                </th>
                {showSpread && <SortHeading label="Spread" column="spread" sort={sort} onSort={setSort} className="text-right" />}
                <SortHeading label="24h" column="change" sort={sort} onSort={setSort} className={`text-right ${showSpread ? "@max-[18rem]:hidden" : ""}`} />
                {showChart && (
                  <th scope="col" className="pr-3 font-normal @max-[21rem]:hidden">
                    <span className="sr-only">Last 24 hours</span>
                  </th>
                )}
                <th scope="col" className="p-0">
                  <span className="sr-only">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody onDragLeave={() => setDropBefore(undefined)}>
              {shown.map((instrument) => (
                <Row
                  key={instrument.symbol}
                  accountId={accountId}
                  instrument={instrument}
                  isSelected={instrument.symbol === selected}
                  isFavorite={favorites.includes(instrument.symbol)}
                  lists={lists.map((l) => ({ id: l.id, name: l.name, has: l.symbols.includes(instrument.symbol) }))}
                  now={now}
                  showSpread={showSpread}
                  showChart={showChart}
                  priceTicks={priceTicks}
                  draggable={canDrag}
                  dropMark={dragging !== null && dropBefore === instrument.symbol}
                  onSelect={onSelect}
                  onToggleFavorite={toggleFavorite}
                  onToggleList={toggleInList}
                  onNewList={() => setNaming({ id: null, name: "" })}
                  onDragStart={() => setDragging(instrument.symbol)}
                  onDragEnd={() => {
                    setDragging(null);
                    setDropBefore(undefined);
                  }}
                  onDragOver={() => setDropBefore(instrument.symbol)}
                  onDrop={() => {
                    if (dragging && dragging !== instrument.symbol) {
                      saveOrder(moveSymbol(symbolsOfList(), dragging, instrument.symbol));
                    }
                    setDragging(null);
                    setDropBefore(undefined);
                  }}
                />
              ))}
            </tbody>
          </table>
        )}
        {canDrag && shown.length > 1 && <p className="px-3 py-2 text-[11px] text-muted">Drag the rows into your own order.</p>}
      </div>

      {shown.length <= shortList && selectedInstrument && (
        <section aria-label={`About ${selectedInstrument.symbol}`} className="mt-auto min-h-0 overflow-y-auto border-t border-border px-3 py-3">
          <h3 className="mb-0.5 text-xs font-semibold">{selectedInstrument.symbol}</h3>
          <p className="mb-2.5 text-[11px] text-muted">{selectedInstrument.name}</p>
          <InstrumentConditions accountId={accountId} instrument={selectedInstrument} />
        </section>
      )}
    </aside>
  );
}

/** The rows by a column: the symbol, the change over 24 hours or the spread in pips, with the unknown last. */
function sortRows(rows: readonly InstrumentInfo[], sort: NonNullable<WatchlistSort>, daySummary: (symbol: string) => DaySummary | undefined): InstrumentInfo[] {
  const prices = useTradingStore.getState().prices;
  const valueOf = (i: InstrumentInfo): number | string | null => {
    if (sort.column === "symbol") {
      return i.symbol;
    }

    const price = prices[i.symbol];
    if (sort.column === "spread") {
      return price ? pipsOf(price.ask - price.bid, i.digits) : null;
    }

    return dayFigures(daySummary(i.symbol), price?.bid).changePercent;
  };

  return [...rows].sort((a, b) => {
    const x = valueOf(a);
    const y = valueOf(b);
    if (x === null || y === null) {
      return x === y ? 0 : x === null ? 1 : -1;
    }

    const order = typeof x === "string" ? x.localeCompare(String(y)) : x - Number(y);
    return sort.descending ? -order : order;
  });
}

function SortHeading({
  label,
  column,
  sort,
  onSort,
  className = "",
}: {
  label: string;
  column: NonNullable<WatchlistSort>["column"];
  sort: WatchlistSort;
  onSort: (sort: WatchlistSort) => void;
  className?: string;
}) {
  const active = sort?.column === column;
  return (
    <th scope="col" aria-sort={active ? (sort.descending ? "descending" : "ascending") : "none"} className={`px-1 py-1.5 font-normal ${className}`}>
      <button
        type="button"
        onClick={() => onSort(nextSort(sort, column))}
        title={`Sort by ${label.toLowerCase()}`}
        className={`inline-flex items-center gap-0.5 transition-colors duration-150 hover:text-foreground ${active ? "text-foreground" : ""}`}
      >
        {label}
        {active && <ChevronDownIcon className={`size-3 ${sort.descending ? "" : "rotate-180"}`} />}
      </button>
    </th>
  );
}

function Row({
  accountId,
  instrument,
  isSelected,
  isFavorite,
  lists,
  now,
  showSpread,
  showChart,
  priceTicks,
  draggable,
  dropMark,
  onSelect,
  onToggleFavorite,
  onToggleList,
  onNewList,
  onDragStart,
  onDragEnd,
  onDragOver,
  onDrop,
}: {
  accountId: string;
  instrument: InstrumentInfo;
  isSelected: boolean;
  isFavorite: boolean;
  lists: { id: string; name: string; has: boolean }[];
  now: Date | null;
  showSpread: boolean;
  showChart: boolean;
  priceTicks: boolean;
  draggable: boolean;
  dropMark: boolean;
  onSelect: (symbol: string) => void;
  onToggleFavorite: (symbol: string) => void;
  onToggleList: (id: string, symbol: string) => void;
  onNewList: () => void;
  onDragStart: () => void;
  onDragEnd: () => void;
  onDragOver: () => void;
  onDrop: () => void;
}) {
  const { symbol, digits } = instrument;
  const price = useTradingStore((s) => s.prices[symbol]);
  const move = useTradingStore((s) => s.moves[symbol]);
  const day = dayFigures(useDaySummary(accountId, symbol).data, price?.bid);
  const market = useMarket(accountId, symbol);
  // The prices of a closed market are its last ones, so they are dimmed, as are prices that stopped coming (ADR 0053).
  const closed = isClosed(market);
  const oldFor = useOldPrice(accountId, symbol, now);
  const dimmed = closed || oldFor !== null;
  const spread = price ? pipsOf(price.ask - price.bid, digits) : null;

  return (
    <tr
      draggable={draggable}
      onDragStart={(e) => {
        e.dataTransfer.effectAllowed = "move";
        e.dataTransfer.setData("text/plain", symbol);
        onDragStart();
      }}
      onDragEnd={onDragEnd}
      onDragOver={(e) => {
        if (draggable) {
          e.preventDefault();
          onDragOver();
        }
      }}
      onDrop={(e) => {
        e.preventDefault();
        onDrop();
      }}
      className={`group cursor-pointer transition-colors duration-150 ${isSelected ? "bg-accent/[0.13] shadow-[inset_2px_0_0_var(--color-accent)]" : "hover:bg-raised"} ${dropMark ? "shadow-[inset_0_2px_0_var(--color-accent)]" : ""}`}
      onClick={() => onSelect(symbol)}
    >
      <td className="py-1.5 pl-3">
        <span className="flex min-w-0 flex-col leading-tight">
          <span className="flex min-w-0 items-center gap-1">
            <button type="button" className={`truncate font-medium ${isSelected ? "text-accent" : ""}`} aria-pressed={isSelected}>
              {symbol}
            </button>
            {isFavorite && (
              <span title="In your favorites" className="shrink-0 text-accent">
                <StarIcon filled className="size-2.5" />
              </span>
            )}
            {market && closed && <ClosedTag market={market} compact />}
            {oldFor !== null && <OldPriceTag ageMs={oldFor} compact />}
          </span>
          <span className="truncate text-[10px] text-muted">{instrument.name}</span>
        </span>
      </td>
      <td className={`px-1 py-1.5 text-right tabular-nums ${dimmed ? "opacity-60" : ""}`}>
        <Tick bid={price?.bid} move={priceTicks ? move : undefined}>
          {formatPrice(price?.bid, digits)}
        </Tick>
      </td>
      <td className={`px-1 py-1.5 text-right tabular-nums ${dimmed ? "opacity-60" : ""}`}>
        <Tick bid={price?.bid} move={priceTicks ? move : undefined}>
          {formatPrice(price?.ask, digits)}
        </Tick>
      </td>
      {showSpread && <td className="live px-1 py-1.5 text-right text-muted tabular-nums">{spread === null ? "-" : spread.toFixed(1)}</td>}
      <td
        className={`live px-1 py-1.5 text-right tabular-nums ${showSpread ? "@max-[18rem]:hidden" : ""} ${day.change === null ? "text-muted" : day.change >= 0 ? "text-profit" : "text-loss"}`}
        title={day.change === null ? "Less than 24 hours of prices" : "Change of the bid over 24 hours"}
      >
        {formatSignedPercent(day.changePercent)}
      </td>
      {showChart && (
        <td className="overflow-hidden py-1.5 pr-3 pl-1 @max-[21rem]:hidden">
          <Sparkline points={day.points} />
        </td>
      )}
      <td className="relative p-0">
        {/* The star and the lists come on hover and focus at the end of the row, over the change, so the list stays calm
            and the symbol and prices stay in view (ADR 0058). */}
        <span
          onClick={(e) => e.stopPropagation()}
          className="absolute inset-y-1 right-0 flex items-center gap-0.5 rounded-md bg-raised px-0.5 opacity-0 shadow-card transition-opacity duration-150 group-hover:opacity-100 focus-within:opacity-100"
        >
          <button
            type="button"
            aria-label={isFavorite ? `Remove ${symbol} from favorites` : `Add ${symbol} to favorites`}
            aria-pressed={isFavorite}
            onClick={() => onToggleFavorite(symbol)}
            className={`flex size-5 items-center justify-center rounded transition-colors duration-150 hover:bg-panel ${isFavorite ? "text-accent" : "text-muted hover:text-foreground"}`}
          >
            <StarIcon filled={isFavorite} className="size-3.5" />
          </button>
          <Menu
            label={`${symbol} in your lists`}
            buttonLabel={`${symbol} in your lists`}
            title="Add to a list"
            button={<MoreIcon className="size-3.5" />}
            items={[
              ...lists.map((l) => ({ value: l.id, label: l.has ? `Remove from ${l.name}` : `Add to ${l.name}`, action: true })),
              { value: "new", label: "New list", action: true },
            ]}
            onChoose={(value) => (value === "new" ? onNewList() : onToggleList(value, symbol))}
            className="flex size-5 items-center justify-center rounded text-muted transition-colors duration-150 hover:bg-panel hover:text-foreground"
          />
        </span>
      </td>
    </tr>
  );
}

const ticks: Record<PriceMove, string> = { up: "animate-tick-up", down: "animate-tick-down" };

/**
 * A price whose text turns green for a moment when the bid rose and red when it fell, then back. Keyed by the bid, so
 * each new bid starts it again; the row renders on every price anyway, so this costs no extra renders.
 */
function Tick({ bid, move, children }: { bid: number | undefined; move: PriceMove | undefined; children: React.ReactNode }) {
  return (
    <span key={bid} className={`live ${move ? ticks[move] : ""}`}>
      {children}
    </span>
  );
}

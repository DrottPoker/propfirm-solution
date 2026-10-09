"use client";

import { useCallback, useId, useRef, useState } from "react";

import { useAlerts } from "@/lib/alerts";
import { formatPrice } from "@/lib/format";
import { parsePrice } from "@/lib/orderInput";
import { useTradingStore } from "@/lib/store";

import { BellIcon } from "./icons";
import { useDismiss } from "./useDismiss";

/**
 * The bell above the chart, which sets a price alert on the symbol (ADR 0058): a price, from the bid now, that the
 * terminal tells the trader of once the bid reaches it.
 */
export function AlertButton({
  symbol,
  digits,
  className,
  onAdd,
}: {
  symbol: string;
  digits: number;
  className: string;
  onAdd: (symbol: string, price: number) => void;
}) {
  const [open, setOpen] = useState(false);
  const [price, setPrice] = useState("");
  const ref = useRef<HTMLDivElement>(null);
  const id = useId();
  const close = useCallback(() => setOpen(false), []);
  useDismiss(open, ref, close);
  const count = useAlerts((s) => s.alerts.filter((a) => a.symbol === symbol).length);
  const parsed = parsePrice(price, digits);
  const valid = parsed.ok && parsed.value !== null;

  const add = () => {
    if (parsed.ok && parsed.value !== null) {
      onAdd(symbol, parsed.value);
      setOpen(false);
    }
  };

  return (
    <div ref={ref} className="relative shrink-0">
      <button
        type="button"
        aria-expanded={open}
        aria-controls={id}
        title={count > 0 ? `Price alerts on ${symbol}: ${count}` : `Set a price alert on ${symbol}`}
        onClick={() => {
          setPrice(formatPrice(useTradingStore.getState().prices[symbol]?.bid, digits).replace("-", ""));
          setOpen((o) => !o);
        }}
        className={`${className} relative`}
      >
        <BellIcon className={`size-4 ${count > 0 ? "text-accent" : ""}`} />
        <span className="sr-only">Price alert</span>
      </button>
      {open && (
        <form
          id={id}
          onSubmit={(e) => {
            e.preventDefault();
            add();
          }}
          className="absolute top-full left-0 z-30 mt-1 flex w-64 animate-pop flex-col gap-2 rounded-lg border border-border bg-panel p-3 text-xs shadow-float"
        >
          <label className="flex flex-col gap-1">
            <span className="text-muted">Tell me when the {symbol} bid reaches</span>
            <input
              autoFocus
              value={price}
              onChange={(e) => setPrice(e.target.value)}
              inputMode="decimal"
              className="rounded-md border border-border bg-raised px-2 py-1.5 text-sm outline-none focus:border-accent"
            />
          </label>
          {!valid && price.trim() !== "" && <span className="text-warning">Enter a price with at most {digits} decimals.</span>}
          <button
            type="submit"
            disabled={!valid}
            className="h-8 rounded-md bg-accent font-semibold text-accent-foreground transition duration-150 hover:brightness-110 active:translate-y-px disabled:opacity-40"
          >
            Set alert
          </button>
          <span className="text-muted">Also with a right click on the chart. They are listed under Alerts.</span>
        </form>
      )}
    </div>
  );
}

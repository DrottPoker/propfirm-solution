"use client";

import { alertText, useAlerts } from "@/lib/alerts";
import type { DigitsOf } from "@/lib/events";
import { formatPrice } from "@/lib/format";
import { useTradingStore } from "@/lib/store";

import { CloseIcon } from "./icons";

/**
 * The trader's price alerts (ADR 0058): what each waits for, where the bid is now and how far it has to go. New ones are
 * set on the chart, with the bell or a right click at a price.
 */
export function AlertsList({ digitsOf }: { digitsOf: DigitsOf }) {
  const alerts = useAlerts((s) => s.alerts);
  const remove = useAlerts((s) => s.remove);
  const prices = useTradingStore((s) => s.prices);

  if (alerts.length === 0) {
    return <p className="px-3 py-4 text-center text-muted">No alerts. Set one on the chart with the bell, or with a right click at a price.</p>;
  }

  return (
    <table className="w-full">
      <thead className="sticky top-0 bg-panel text-xs text-muted">
        <tr>
          {["Symbol", "Waits for", "Bid now", "To go", ""].map((h, i) => (
            <th key={`${h}-${i}`} className={`px-3 py-2 font-normal whitespace-nowrap ${i >= 2 && i < 4 ? "text-right" : "text-left"}`}>
              {h}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {alerts.map((alert) => {
          const digits = digitsOf(alert.symbol);
          const bid = prices[alert.symbol]?.bid;
          return (
            <tr key={alert.id} className="border-t border-border transition-colors duration-150 hover:bg-raised/50">
              <td className="px-3 py-1.5 font-medium">{alert.symbol}</td>
              <td className="px-3 py-1.5">{alertText(alert, digits).replace(`${alert.symbol} bid `, "Bid ")}</td>
              <td className="px-3 py-1.5 text-right">{formatPrice(bid, digits)}</td>
              <td className="px-3 py-1.5 text-right text-muted">{bid === undefined ? "-" : formatPrice(Math.abs(alert.price - bid), digits)}</td>
              <td className="px-3 py-1.5 text-right">
                <button
                  type="button"
                  onClick={() => remove(alert.id)}
                  aria-label={`Remove the alert, ${alertText(alert, digits)}`}
                  title="Remove the alert"
                  className="rounded-md p-1 text-muted transition-colors duration-150 hover:bg-raised hover:text-loss"
                >
                  <CloseIcon className="size-3.5" />
                </button>
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}

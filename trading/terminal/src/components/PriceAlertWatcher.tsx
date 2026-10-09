"use client";

import { useEffect } from "react";
import { toast } from "sonner";

import { alertText, reached, useAlerts } from "@/lib/alerts";
import type { DigitsOf } from "@/lib/events";
import { formatPrice, formatTime, timeZoneName } from "@/lib/format";
import { notifyInBackground } from "@/lib/notify";
import { useSettings } from "@/lib/settings";
import { playAlertSound } from "@/lib/sound";
import { useTradingStore } from "@/lib/store";

/** How long the note of a fired alert stays: it was asked for, so it may stay a while. */
const alertDuration = 20_000;

/**
 * Watches the trader's price alerts while the terminal is open (ADR 0058). An alert fires once the bid reaches its price
 * from the side it was set on: a note, a sound unless turned off, the computer's notification in the background, and
 * the alert goes.
 */
export function usePriceAlertWatcher(digitsOf: DigitsOf, timeZone: string) {
  useEffect(
    () =>
      useTradingStore.subscribe((state, previous) => {
        if (state.prices === previous.prices) {
          return;
        }

        const { alerts, remove } = useAlerts.getState();
        for (const alert of alerts) {
          const price = state.prices[alert.symbol];
          if (!price || price === previous.prices[alert.symbol] || !reached(alert, price.bid)) {
            continue;
          }

          remove(alert.id);
          const digits = digitsOf(alert.symbol);
          const title = `${alert.symbol} reached ${formatPrice(alert.price, digits)}`;
          const description = `Your alert: ${alertText(alert, digits)}. Bid ${formatPrice(price.bid, digits)} at ${formatTime(price.timestamp, timeZone)} ${timeZoneName(timeZone)}.`;
          toast.info(title, { id: `alert-${alert.id}`, description, duration: alertDuration });
          notifyInBackground(title, description);
          if (useSettings.getState().alertSound) {
            playAlertSound();
          }
        }
      }),
    [digitsOf, timeZone],
  );
}

"use client";

import { useEffect } from "react";
import { toast } from "sonner";

import type { DigitsOf } from "@/lib/events";
import { formatSignedMoney } from "@/lib/format";
import { newEvents, tradeNotice } from "@/lib/notices";
import { useSettings } from "@/lib/settings";
import { playCloseSound, playFillSound } from "@/lib/sound";
import { useTradingStore } from "@/lib/store";

import { ClosedIcon } from "./icons";

/** How long a refusal stays: longer than a fill, since it has to be read and acted on. */
const errorDuration = 8_000;

/** Shows that something the trader asked for failed, for example "Refused: not enough free margin". */
export function showError(text: string) {
  toast.error(text, { duration: errorDuration });
}

/**
 * Tells the trader of fills, pending orders placed and closes as they happen, in a note that comes and goes in a
 * corner, with a short sound on fills when the trader turned it on.
 */
export function useTradeNotices(digitsOf: DigitsOf) {
  useEffect(
    () =>
      newEvents.subscribe((envelopes) => {
        const { events, account } = useTradingStore.getState();
        const history = events.map((e) => e.event);
        for (const { event } of envelopes) {
          const notice = tradeNotice(event, digitsOf, history);
          if (!notice) {
            continue;
          }

          if (notice.result === undefined) {
            toast.success(notice.title, { id: notice.id });
          } else {
            toast(notice.title, {
              id: notice.id,
              icon: <ClosedIcon className={`size-5 ${notice.result >= 0 ? "text-profit" : "text-loss"}`} />,
              description: (
                <>
                  Result{" "}
                  <span className={`font-mono ${notice.result >= 0 ? "text-profit" : "text-loss"}`}>
                    {formatSignedMoney(notice.result)} {account?.currency}
                  </span>{" "}
                  after commission
                </>
              ),
            });
          }

          const settings = useSettings.getState();
          if (notice.kind === "filled" && settings.fillSound) {
            playFillSound();
          } else if (notice.kind === "closed" && settings.closeSound) {
            playCloseSound();
          }
        }
      }),
    [digitsOf],
  );
}

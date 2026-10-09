import { useEffect, useRef, useState } from "react";

import { useTradingStore } from "./store";

/** How long a lost connection waits before it is told, so a quick reconnect shows nothing. */
export const lostAfterMs = 2_000;

/** How long a terminal that never connected waits before it says the service cannot be reached. */
export const unreachableAfterMs = 5_000;

/** How long "Connected again" stays. */
export const backForMs = 3_000;

/**
 * What the trader is told about the connection to the trading service (ADR 0058): that it was lost, that the service
 * cannot be reached when the terminal never connected, or for a moment that it is back.
 */
export type ConnectionNotice = "lost" | "unreachable" | "back" | null;

export function useConnectionNotice(): ConnectionNotice {
  // Only whether it is connected: the states in between, such as trying again, do not start the wait over.
  const connected = useTradingStore((s) => s.connection === "connected");
  const [notice, setNotice] = useState<ConnectionNotice>(null);
  const everConnected = useRef(false);
  const told = useRef(false);

  useEffect(() => {
    if (connected) {
      everConnected.current = true;
      if (!told.current) {
        return;
      }

      told.current = false;
      const show = setTimeout(() => setNotice("back"), 0);
      const hide = setTimeout(() => setNotice(null), backForMs);
      return () => {
        clearTimeout(show);
        clearTimeout(hide);
      };
    }

    // "Connected again" goes at once when the connection is lost again.
    const clear = setTimeout(() => setNotice((current) => (current === "back" ? null : current)), 0);
    const tell = setTimeout(
      () => {
        told.current = true;
        setNotice(everConnected.current ? "lost" : "unreachable");
      },
      everConnected.current ? lostAfterMs : unreachableAfterMs,
    );
    return () => {
      clearTimeout(clear);
      clearTimeout(tell);
    };
  }, [connected]);

  return notice;
}

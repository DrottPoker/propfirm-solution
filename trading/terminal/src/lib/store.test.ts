import { describe, expect, it } from "vitest";

import type { EventEnvelope } from "./api/types";
import { applyPrices, mergeEvents } from "./store";

function envelope(sequence: number): EventEnvelope {
  return {
    sequence,
    event: { kind: "EquityFloorRemoved", accountId: "demo", floorId: `f${sequence}`, timestamp: "2026-10-05T08:00:00+00:00" },
  };
}

describe("mergeEvents", () => {
  it("keeps events unique and in sequence order", () => {
    const merged = mergeEvents([envelope(1), envelope(3)], [envelope(3), envelope(2), envelope(4)]);

    expect(merged.map((e) => e.sequence)).toEqual([1, 2, 3, 4]);
  });

  it("returns the same list when nothing arrives", () => {
    const current = [envelope(1)];

    expect(mergeEvents(current, [])).toBe(current);
  });
});

describe("applyPrices", () => {
  const price = (symbol: string, bid: number) => ({ symbol, bid, ask: bid + 0.0002, timestamp: "2026-10-05T08:00:00+00:00" });

  it("remembers which way each bid moved", () => {
    const first = applyPrices({ prices: {}, moves: {} }, [price("EURUSD", 1.1), price("GBPUSD", 1.3)]);
    expect(first.moves).toEqual({});

    const second = applyPrices(first, [price("EURUSD", 1.2), price("GBPUSD", 1.2)]);
    expect(second.moves).toEqual({ EURUSD: "up", GBPUSD: "down" });
    expect(second.prices.EURUSD.bid).toBe(1.2);
  });

  it("keeps the last move when the bid is unchanged", () => {
    const moved = applyPrices({ prices: { EURUSD: price("EURUSD", 1.1) }, moves: {} }, [price("EURUSD", 1.0)]);

    expect(applyPrices(moved, [price("EURUSD", 1.0)]).moves).toEqual({ EURUSD: "down" });
  });
});

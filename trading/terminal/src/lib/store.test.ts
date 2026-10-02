import { describe, expect, it } from "vitest";

import type { EventEnvelope } from "./api/types";
import { mergeEvents } from "./store";

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

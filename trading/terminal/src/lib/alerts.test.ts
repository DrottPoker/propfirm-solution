import { describe, expect, it } from "vitest";

import { alertText, directionTo, parseAlerts, reached, type PriceAlert } from "./alerts";

const alert = (price: number, direction: PriceAlert["direction"]): PriceAlert => ({
  id: "a",
  symbol: "EURUSD",
  price,
  direction,
  createdAt: "2026-10-09T08:00:00Z",
});

describe("price alerts", () => {
  it("waits for the bid to rise to a price above it, and to fall to one below it", () => {
    expect(directionTo(1.13, 1.12)).toBe("up");
    expect(directionTo(1.11, 1.12)).toBe("down");
  });

  it("fires once the bid reaches the price from its side", () => {
    expect(reached(alert(1.13, "up"), 1.1299)).toBe(false);
    expect(reached(alert(1.13, "up"), 1.13)).toBe(true);
    expect(reached(alert(1.11, "down"), 1.1101)).toBe(false);
    expect(reached(alert(1.11, "down"), 1.1099)).toBe(true);
  });

  it("says what it waits for", () => {
    expect(alertText(alert(1.13, "up"), 5)).toBe("EURUSD bid rises to 1.13000");
  });

  it("keeps alerts and leaves out anything else stored", () => {
    const raw = JSON.stringify([alert(1.13, "up"), { ...alert(1.1, "up"), direction: "sideways", id: "b" }, { ...alert(-1, "up"), id: "c" }, "x"]);

    expect(parseAlerts(raw).map((a) => a.id)).toEqual(["a"]);
    expect(parseAlerts("{")).toEqual([]);
  });
});

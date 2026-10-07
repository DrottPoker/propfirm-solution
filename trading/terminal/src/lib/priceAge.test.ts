import { describe, expect, it } from "vitest";

import type { MarketHours } from "./api/types";
import { ageText, feedStoppedFor, priceIsOld, priceTooOld } from "./priceAge";

const now = 1_000_000;
const market = (symbol: string, isOpen: boolean): MarketHours => ({ symbol, isOpen, nextChange: null, sessions: null });

describe("feedStoppedFor", () => {
  it("counts from the newest price of an open market", () => {
    const received = { EURUSD: now - 90_000, BTCUSD: now - 61_000 };

    expect(feedStoppedFor(received, [], now)).toBe(61_000);
    expect(feedStoppedFor({ ...received, BTCUSD: now - 5_000 }, [], now)).toBeNull();
  });

  // A weekend: forex is closed, so its old prices mean nothing.
  it("waits for no price from a closed market", () => {
    const received = { EURUSD: now - 3_600_000, BTCUSD: now - 1_000 };

    expect(feedStoppedFor(received, [market("EURUSD", false)], now)).toBeNull();
    expect(feedStoppedFor({ EURUSD: now - 3_600_000 }, [market("EURUSD", false)], now)).toBeNull();
    expect(feedStoppedFor({}, [], now)).toBeNull();
  });
});

describe("priceTooOld", () => {
  it("is old past the service's limit, while the market is open", () => {
    const received = { EURUSD: now - 6_000 };

    expect(priceTooOld("EURUSD", received, [], 5_000, now)).toBe(true);
    expect(priceTooOld("EURUSD", received, [], 10_000, now)).toBe(false);
    expect(priceTooOld("EURUSD", received, [market("EURUSD", false)], 5_000, now)).toBe(false);
    expect(priceTooOld("GBPUSD", received, [], 5_000, now)).toBe(false);
    expect(priceIsOld("EURUSD", received, [], now)).toBe(false);
  });
});

describe("ageText", () => {
  it.each([
    [45_900, "45 s"],
    [240_000, "4 min"],
    [7_200_000, "2 h"],
    [7_800_000, "2 h 10 min"],
  ])("says %d ms as %j", (ms, expected) => {
    expect(ageText(ms)).toBe(expected);
  });
});

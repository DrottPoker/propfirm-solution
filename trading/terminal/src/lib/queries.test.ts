import { QueryClient } from "@tanstack/react-query";
import { afterEach, describe, expect, it, vi } from "vitest";

import { meKey, onTheClock, reloadCandles } from "./queries";

describe("reloadCandles", () => {
  it("marks the account's charts and day summaries to be loaded again, and nothing else", () => {
    const queryClient = new QueryClient();
    const keys = {
      chart: ["candles", "A1", "EURUSD", "M1"],
      day: ["day", "A1", "EURUSD"],
      otherAccount: ["candles", "A2", "EURUSD", "M1"],
      me: meKey,
    };
    for (const key of Object.values(keys)) {
      queryClient.setQueryData(key, []);
    }

    reloadCandles(queryClient, "A1");

    expect(Object.fromEntries(Object.entries(keys).map(([name, key]) => [name, queryClient.getQueryState(key)?.isInvalidated]))).toEqual({
      chart: true,
      day: true,
      otherAccount: false,
      me: false,
    });
  });
});

describe("onTheClock", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("waits until the next whole multiple of the period", () => {
    vi.useFakeTimers();
    vi.setSystemTime(Date.UTC(2026, 9, 5, 8, 0, 3, 250));

    expect(onTheClock(10_000)()).toBe(6_750);
  });

  it("waits a whole period on the boundary", () => {
    vi.useFakeTimers();
    vi.setSystemTime(Date.UTC(2026, 9, 5, 8, 0, 10));

    expect(onTheClock(10_000)()).toBe(10_000);
  });
});

import { afterEach, describe, expect, it, vi } from "vitest";

import { onTheClock } from "./queries";

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

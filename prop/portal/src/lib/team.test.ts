import { describe, expect, it } from "vitest";

import { lastLoginText } from "./team";

// Times in the local time zone, so the calendar days are the same wherever the tests run.
const at = (day: number, hour: number, month = 9) => new Date(2026, month, day, hour, 0);
const now = at(6, 9).getTime();

describe("the last login on the Team page", () => {
  it("says when an administrator never logged in", () => {
    expect(lastLoginText(null, now)).toBe("Never logged in");
  });

  it("counts calendar days, not hours", () => {
    expect(lastLoginText(at(6, 0).toISOString(), now)).toBe("Logged in today");
    expect(lastLoginText(at(5, 23).toISOString(), now)).toBe("Last logged in yesterday");
    expect(lastLoginText(at(3, 22).toISOString(), now)).toBe("Last logged in 3 days ago");
  });

  it("gives the date after a month", () => {
    expect(lastLoginText(at(7, 12, 8).toISOString(), now)).toBe("Last logged in 29 days ago");
    expect(lastLoginText(at(1, 12, 7).toISOString(), now)).toBe("Last logged in on 1 Aug 2026");
  });

  it("says today for a login a moment ahead of the browser's clock", () => {
    expect(lastLoginText(at(6, 9).toISOString(), now - 5_000)).toBe("Logged in today");
  });
});

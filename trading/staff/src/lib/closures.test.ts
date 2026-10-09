import { describe, expect, it } from "vitest";

import { closureText, zoneCity } from "./closures";

describe("closures", () => {
  it("says a whole day, part of one, or several", () => {
    expect(closureText("2026-12-25T00:00:00", "2026-12-26T00:00:00")).toBe("25 Dec 2026");
    expect(closureText("2026-12-24T13:15:00", "2026-12-24T18:00:00")).toBe("24 Dec 2026, 13:15 to 18:00");
    expect(closureText("2026-12-24T13:15:00", "2026-12-25T00:00:00")).toBe("24 Dec 2026, 13:15 to 24:00");
    expect(closureText("2026-12-24T13:15:00", "2026-12-27T18:00:00")).toBe("24 Dec 13:15 to 27 Dec 18:00");
    expect(closureText("2026-12-31T00:00:00", "2027-01-02T00:00:00")).toBe("31 Dec to 1 Jan 2027");
  });

  it("names the city of a time zone", () => {
    expect(zoneCity("America/New_York")).toBe("New York");
    expect(zoneCity("UTC")).toBe("UTC");
  });
});

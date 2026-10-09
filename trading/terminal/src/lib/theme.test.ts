import { describe, expect, it } from "vitest";

import { parseTheme, parseTimeZoneChoice } from "./settings";
import { resolveTheme } from "./theme";

describe("the theme", () => {
  it("follows the computer only when chosen", () => {
    expect(resolveTheme("system", true)).toBe("light");
    expect(resolveTheme("system", false)).toBe("dark");
    expect(resolveTheme("dark", true)).toBe("dark");
    expect(resolveTheme("light", false)).toBe("light");
  });

  it("reads stored choices, with Kronant's dark theme and the account's time zone by default", () => {
    expect(parseTheme("light")).toBe("light");
    expect(parseTheme("pink")).toBe("dark");
    expect(parseTimeZoneChoice("utc")).toBe("utc");
    expect(parseTimeZoneChoice(null)).toBe("account");
  });
});

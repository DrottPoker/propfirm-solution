import { describe, expect, it } from "vitest";

import { isSettingsPath, isUnder, liveOrBilling } from "./adminNav";

describe("the admin panel's places", () => {
  it("knows the settings and the pages under them", () => {
    expect(isSettingsPath("/admin/settings")).toBe(true);
    expect(isSettingsPath("/admin/design")).toBe(true);
    expect(isSettingsPath("/admin/team")).toBe(true);
    expect(isSettingsPath("/admin/accounts")).toBe(false);
    expect(isSettingsPath("/admin/designs")).toBe(false);
  });

  it("matches a place and the pages under it, not one that only starts the same", () => {
    expect(isUnder("/admin/accounts/abc", "/admin/accounts")).toBe(true);
    expect(isUnder("/admin/accountsx", "/admin/accounts")).toBe(false);
  });

  it("offers Go live in the sandbox and Plan and billing once live", () => {
    expect(liveOrBilling("Sandbox").label).toBe("Go live");
    expect(liveOrBilling("Live").label).toBe("Plan and billing");
  });
});

import { describe, expect, it } from "vitest";

import { loginWithNext, safeNext } from "./next";

describe("the page after logging in", () => {
  it("is a path on this site", () => {
    expect(safeNext("/accounts/1?tab=trades")).toBe("/accounts/1?tab=trades");
    expect(safeNext("/terminal?account=demo-firm-1001-1")).toBe("/terminal?account=demo-firm-1001-1");
  });

  it("is never another site", () => {
    expect(safeNext("https://evil.test/")).toBeNull();
    expect(safeNext("//evil.test/")).toBeNull();
    expect(safeNext("/\\evil.test/")).toBeNull();
    expect(safeNext(["/a", "/b"])).toBeNull();
    expect(safeNext(undefined)).toBeNull();
  });

  it("is left out for the home page", () => {
    expect(loginWithNext("/login", "/", "/")).toBe("/login");
    expect(loginWithNext("/login", "/", "/accounts/1")).toBe("/login?next=%2Faccounts%2F1");
  });
});

import { describe, expect, it } from "vitest";

import { guideSteps, stepAfter, stepOf, tryChecklist } from "./getStarted";
import { testAccount } from "./testAccounts";

describe("the guide for a new firm", () => {
  it("goes through the look, the price, the payments and trying it, and stays within its steps", () => {
    expect(guideSteps.map((s) => s.key)).toEqual(["look", "price", "payments", "try"]);
    expect(stepAfter("look", 1)).toBe("price");
    expect(stepAfter("look", -1)).toBe("look");
    expect(stepAfter("try", 1)).toBe("try");
  });

  it("starts at the step in the address, or the first", () => {
    expect(stepOf("payments")).toBe("payments");
    expect(stepOf("nonsense")).toBe("look");
    expect(stepOf(undefined)).toBe("look");
  });

  it("ticks off trying it as a trader by itself, from the firm's accounts", () => {
    const done = (accounts: (typeof testAccount)[]) => tryChecklist(accounts).map((i) => i.done);

    expect(done([])).toEqual([false, false, false]);
    expect(done([{ ...testAccount, status: "OpeningAccount", tradingAccountId: null, tradingDays: 0 }])).toEqual([true, false, false]);
    expect(done([{ ...testAccount, tradingDays: 0 }])).toEqual([true, true, false]);
    expect(done([testAccount])).toEqual([true, true, true]);
    expect(done([{ ...testAccount, status: "Cancelled" }])).toEqual([false, false, false]);
  });
});

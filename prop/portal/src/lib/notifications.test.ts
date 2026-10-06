import { describe, expect, it } from "vitest";

import { isOn, notificationKinds, recipientText } from "./notifications";

describe("the notification emails", () => {
  it("are listed once each, to the team first", () => {
    const kinds = notificationKinds.map((n) => n.kind);
    expect(new Set(kinds).size).toBe(kinds.length);
    expect(notificationKinds.findIndex((n) => n.audience === "trader")).toBeGreaterThan(notificationKinds.findLastIndex((n) => n.audience === "team"));
  });

  it("are sent unless the firm turned them off", () => {
    expect(isOn({}, "firmSale")).toBe(true);
    expect(isOn({ firmSale: false }, "firmSale")).toBe(false);
    expect(isOn({ firmSale: true }, "firmSale")).toBe(true);
  });

  it("say who they go to in their preview", () => {
    expect(recipientText("Team")).toBe("To each administrator on your team");
    expect(recipientText("Trader")).toBe("To the trader");
  });
});

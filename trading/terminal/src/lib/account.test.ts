import { describe, expect, it } from "vitest";

import type { EngineEvent, FloorSnapshot } from "./api/types";
import { endedText, floorLabel, floorLeftText, floorRisk, initials, targetText } from "./account";

describe("floorLabel", () => {
  it.each([
    ["daily", "Daily loss limit"],
    ["max-loss", "Max loss limit"],
    ["min_equity", "Min equity limit"],
    ["", "Loss limit"],
  ])("names %j as %j", (id, expected) => {
    expect(floorLabel(id)).toBe(expected);
  });
});

describe("floorRisk", () => {
  const trailing = (headroom: number): FloorSnapshot => ({
    floorId: "max-loss",
    rule: { kind: "TrailingFloor", distance: 10_000 },
    level: 90_000,
    highWaterMark: 100_000,
    headroom,
  });

  it("marks floors by how much of the distance is left", () => {
    expect(floorRisk(trailing(5_000))).toBe("ok");
    expect(floorRisk(trailing(2_499))).toBe("warning");
    expect(floorRisk(trailing(999))).toBe("danger");
  });

  it("never marks a floor at a fixed level before equity reaches it", () => {
    expect(floorRisk({ ...trailing(1), rule: { kind: "FixedFloor", level: 90_000 } })).toBe("ok");
    expect(floorRisk({ ...trailing(-49), rule: { kind: "FixedFloor", level: 90_000 } })).toBe("danger");
  });

  it("never shows a negative amount left", () => {
    expect(floorLeftText(trailing(2_500))).toBe("2,500.00 left");
    expect(floorLeftText(trailing(-49))).toBe("Broken");
  });
});

describe("targetText", () => {
  it("counts the target on the balance, which needs every position closed", () => {
    expect(targetText(110_000, { balance: 104_500, positions: [] })).toBe("5,500.00 to go");
    expect(targetText(110_000, { balance: 110_200, positions: [] })).toBe("Reached");
    expect(targetText(110_000, { balance: 110_200, positions: [{} as never] })).toBe("Reached, close positions");
  });
});

describe("endedText", () => {
  const timestamp = "2026-10-05T15:38:12+00:00";
  const breach: EngineEvent = {
    kind: "EquityFloorBreached",
    accountId: "demo",
    floorId: "daily",
    level: 9_500,
    equity: 9_482.5,
    prices: [],
    positions: [],
    timestamp,
  };

  it("says which limit broke, when and at what equity", () => {
    expect(endedText([breach, { kind: "AccountDisabled", accountId: "demo", reason: "EquityFloor", timestamp }], "Europe/Stockholm")).toBe(
      "The daily loss limit was broken at 17:38:12 Stockholm time: equity 9,482.50 fell below 9,500.00. Every position was closed at the next price.",
    );
  });

  it("says when the firm closed the account", () => {
    expect(endedText([{ kind: "AccountDisabled", accountId: "demo", reason: "Closed", timestamp }], "UTC")).toBe("The firm closed the account.");
  });
});

describe("initials", () => {
  it.each([
    ["anna.berg@example.com", "AB"],
    ["test@test.com", "TE"],
    ["a@x.se", "A"],
    ["@x.se", "?"],
  ])("takes %j as %j", (email, expected) => {
    expect(initials(email)).toBe(expected);
  });
});

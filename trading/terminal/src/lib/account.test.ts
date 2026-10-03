import { describe, expect, it } from "vitest";

import type { FloorSnapshot } from "./api/types";
import { floorLabel, floorRisk, initials } from "./account";

describe("floorLabel", () => {
  it.each([
    ["daily", "Daily floor"],
    ["max-loss", "Max loss floor"],
    ["min_equity", "Min equity floor"],
    ["", "Floor"],
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

  it("never marks a floor at a fixed level", () => {
    expect(floorRisk({ ...trailing(1), rule: { kind: "FixedFloor", level: 90_000 } })).toBe("ok");
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

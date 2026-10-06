import { describe, expect, it } from "vitest";

import type { FloorSnapshot } from "./api/types";
import { accountWith, daily, rulesWith } from "./rules.fixtures";
import { floorStage, nextWarnings, type WarningMemory } from "./ruleWarnings";

const now = Date.parse("2026-10-07T10:00:00Z");
const timeZone = "Europe/Stockholm";

describe("floorStage", () => {
  it("comes closer below 25% and 10% of the room, and goes back only 10 points above", () => {
    const stages: number[] = [];
    let stage = 0;
    for (const headroom of [1_300, 1_200, 600, 450, 900, 1_000, 1_700, 1_800, 0]) {
      stage = floorStage(daily(headroom), stage);
      stages.push(stage);
    }

    expect(stages).toEqual([0, 1, 1, 2, 2, 1, 1, 0, 3]);
  });

  it("never marks a floor at a fixed level before equity reaches it", () => {
    const fixed: FloorSnapshot = { ...daily(1), rule: { kind: "FixedFloor", level: 95_000 } };

    expect(floorStage(fixed, 0)).toBe(0);
  });
});

describe("nextWarnings", () => {
  const input = (changes: Partial<Parameters<typeof nextWarnings>[1]> = {}) => ({
    account: accountWith(),
    profitTarget: 110_000,
    rules: rulesWith(),
    now,
    timeZone,
    ...changes,
  });

  const run = (steps: Parameters<typeof nextWarnings>[1][]) => {
    let memory: WarningMemory | null = null;
    return steps.map((step) => {
      const next = nextWarnings(memory, step);
      memory = next.memory;
      return next.warnings.map((w) => [w.level, w.title]);
    });
  };

  it("warns once as a loss limit comes closer, and again once it was further away", () => {
    const told = run([
      input(),
      input({ account: accountWith({ floors: [daily(1_200)] }) }),
      input({ account: accountWith({ floors: [daily(1_100)] }) }),
      input({ account: accountWith({ floors: [daily(400)] }) }),
      input({ account: accountWith({ floors: [daily(3_000)] }) }),
      input({ account: accountWith({ floors: [daily(1_000)] }) }),
      input({ account: accountWith({ status: "Disabled", floors: [daily(-50)] }) }),
    ]);

    expect(told).toEqual([
      [],
      [["warning", "Close to the daily loss limit"]],
      [],
      [["danger", "Very close to the daily loss limit"]],
      [],
      [["warning", "Close to the daily loss limit"]],
      [["danger", "The daily loss limit was broken"]],
    ]);
  });

  // The account bar already shows how close the limits are.
  it("tells what already holds when the terminal opens, but not how close the limits are", () => {
    const told = run([
      input({
        account: accountWith({ floors: [daily(400)] }),
        rules: rulesWith({ openPositionBy: "2026-10-08T22:00:00Z" }),
      }),
    ]);

    expect(told).toEqual([[["warning", "Open a position by Fri 9 Oct 00:00 Stockholm time"]]]);
  });

  it("warns three days and again one day before a deadline", () => {
    const passBy = "2026-10-12T22:00:00Z";
    const at = (iso: string) => input({ rules: rulesWith({ passBy }), now: Date.parse(iso) });

    const told = run([at("2026-10-09T21:00:00Z"), at("2026-10-09T23:00:00Z"), at("2026-10-10T12:00:00Z"), at("2026-10-11T22:30:00Z"), at("2026-10-12T22:00:00Z")]);

    expect(told).toEqual([[], [["warning", "Pass the stage by Tue 13 Oct 00:00 Stockholm time"]], [], [["danger", "Pass the stage by Tue 13 Oct 00:00 Stockholm time"]], []]);
  });

  it("tells when the profit target is reached and when the best day is above the consistency rule", () => {
    const told = run([
      input(),
      input({ account: accountWith({ balance: 110_100, positions: [{} as never] }) }),
      input({ profitTarget: null, rules: rulesWith({ funded: true, consistencyPercent: 40, bestDayPercent: 35 }) }),
      input({ profitTarget: null, rules: rulesWith({ funded: true, consistencyPercent: 40, bestDayPercent: 52.5 }) }),
      input({ profitTarget: null, rules: rulesWith({ funded: true, consistencyPercent: 40, bestDayPercent: 55 }) }),
    ]);

    expect(told).toEqual([[], [["success", "Profit target reached"]], [], [["warning", "Best day above the consistency rule"]], []]);
  });

  it("tells nothing more once trading has ended", () => {
    const ended = accountWith({ status: "Disabled", balance: 111_000 });

    const told = run([input(), input({ account: ended, rules: rulesWith({ passBy: "2026-10-07T12:00:00Z" }) })]);

    expect(told).toEqual([[], []]);
  });
});

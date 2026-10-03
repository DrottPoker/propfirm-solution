import { describe, expect, it } from "vitest";

import { formatSignedMoney, formatSignedPercent, formatSignedPrice, formatUnits, formatUtc } from "./format";

describe("formatSignedPrice", () => {
  it.each([
    [0.00312, 5, "+0.00312"],
    [-0.00312, 5, "-0.00312"],
    [0, 5, "0.00000"],
    [-0.000001, 5, "0.00000"],
    [1.5, 2, "+1.50"],
  ])("formats %d with %i digits as %j", (value, digits, expected) => {
    expect(formatSignedPrice(value, digits)).toBe(expected);
  });
});

describe("formatSignedMoney", () => {
  it.each([
    [4, "+4.00"],
    [-1234.5, "-1,234.50"],
    [0, "0.00"],
  ])("formats %d as %j", (value, expected) => {
    expect(formatSignedMoney(value)).toBe(expected);
  });
});

describe("formatSignedPercent", () => {
  it("shows the sign and a dash without a value", () => {
    expect(formatSignedPercent(0.2912)).toBe("+0.29%");
    expect(formatSignedPercent(-0.14)).toBe("-0.14%");
    expect(formatSignedPercent(null)).toBe("-");
  });
});

describe("formatUnits", () => {
  it("hides floating point noise", () => {
    expect(formatUnits(0.07 * 100_000)).toBe("7,000");
  });
});

describe("formatUtc", () => {
  it("shows the date and time in UTC", () => {
    expect(formatUtc(new Date(Date.UTC(2026, 9, 5, 14, 12, 34)))).toBe("2026-10-05 14:12:34");
  });
});

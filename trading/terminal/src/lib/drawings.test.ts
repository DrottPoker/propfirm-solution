import { describe, expect, it } from "vitest";

import { distanceToSegment, hitDrawing, logicalOfTime, moveDrawing, parseDrawings, timeOfLogical, type Drawing } from "./drawings";

describe("parseDrawings", () => {
  it("keeps drawings and leaves out anything else", () => {
    const raw = JSON.stringify([
      { id: "a", kind: "horizontal", price: 1.08 },
      { id: "b", kind: "trend", from: { time: 1, price: 1 }, to: { time: 2, price: 2 } },
      { id: "c", kind: "rectangle", from: { time: 1, price: 1 } },
      { id: "d", kind: "circle", price: 1 },
      "e",
    ]);

    expect(parseDrawings(raw).map((d) => d.id)).toEqual(["a", "b"]);
    expect(parseDrawings("not json")).toEqual([]);
    expect(parseDrawings(null)).toEqual([]);
  });
});

describe("hitDrawing", () => {
  const trend = { kind: "trend" as const, from: { x: 0, y: 0 }, to: { x: 100, y: 100 } };

  it("finds the ends before the line between them", () => {
    expect(hitDrawing(trend, { x: 2, y: 1 }, 5)).toBe("from");
    expect(hitDrawing(trend, { x: 99, y: 100 }, 5)).toBe("to");
    expect(hitDrawing(trend, { x: 50, y: 53 }, 5)).toBe("body");
    expect(hitDrawing(trend, { x: 50, y: 70 }, 5)).toBeNull();
  });

  it("grabs a horizontal line at its height and a rectangle anywhere inside", () => {
    expect(hitDrawing({ kind: "horizontal", y: 40 }, { x: 500, y: 43 }, 5)).toBe("body");
    expect(hitDrawing({ kind: "rectangle", from: { x: 0, y: 0 }, to: { x: 100, y: 50 } }, { x: 50, y: 25 }, 5)).toBe("body");
    expect(hitDrawing({ kind: "rectangle", from: { x: 0, y: 0 }, to: { x: 100, y: 50 } }, { x: 50, y: 60 }, 5)).toBeNull();
  });

  it("measures to the nearest point of a segment", () => {
    expect(distanceToSegment({ x: 0, y: 10 }, { x: 0, y: 0 }, { x: 10, y: 0 })).toBe(10);
    expect(distanceToSegment({ x: 20, y: 0 }, { x: 0, y: 0 }, { x: 10, y: 0 })).toBe(10);
  });
});

describe("moveDrawing", () => {
  const trend: Drawing = { id: "t", kind: "trend", from: { time: 100, price: 1 }, to: { time: 200, price: 2 } };

  it("moves an end to the pointer, or the whole drawing by how far it moved", () => {
    expect(moveDrawing(trend, "to", { time: 200, price: 2 }, { time: 300, price: 3 })).toEqual({ ...trend, to: { time: 300, price: 3 } });
    expect(moveDrawing(trend, "body", { time: 150, price: 1.5 }, { time: 160, price: 2 })).toEqual({
      ...trend,
      from: { time: 110, price: 1.5 },
      to: { time: 210, price: 2.5 },
    });
    const line = moveDrawing({ id: "h", kind: "horizontal", price: 1.08 }, "body", { time: 0, price: 1.08 }, { time: 50, price: 1.09 });
    expect(line.kind === "horizontal" && line.price).toBeCloseTo(1.09, 10);
  });
});

describe("logicalOfTime", () => {
  // Minute bars on a Friday evening and the Sunday after, with the weekend between them.
  const times = [0, 60, 120, 200_000, 200_060];

  it("counts whole bars at bar starts and fractions inside a bar", () => {
    expect(logicalOfTime(times, 60, 60)).toBe(1);
    expect(logicalOfTime(times, 90, 60)).toBe(1.5);
  });

  it("puts a time in a gap at the end of the bar before it", () => {
    expect(logicalOfTime(times, 100_000, 60)).toBe(3);
  });

  it("goes on at the bar length past either end", () => {
    expect(logicalOfTime(times, -120, 60)).toBe(-2);
    expect(logicalOfTime(times, 200_180, 60)).toBe(6);
  });

  it("finds the time back from the place", () => {
    for (const logical of [-2, 0, 1.5, 3.5, 6]) {
      expect(logicalOfTime(times, timeOfLogical(times, logical, 60), 60)).toBeCloseTo(logical, 9);
    }
  });
});

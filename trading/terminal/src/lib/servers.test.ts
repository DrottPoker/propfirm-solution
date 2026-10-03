import { describe, expect, it } from "vitest";

import { initialServer } from "./servers";

const servers = [
  { id: "alpha-funded", name: "Alpha Funded" },
  { id: "nordic-prop", name: "Nordic Prop" },
];

describe("initialServer", () => {
  it("prefers the server in the link from the firm's portal", () => {
    expect(initialServer(servers, "nordic-prop", "alpha-funded")).toBe("nordic-prop");
  });

  it("falls back to the server last used on this device", () => {
    expect(initialServer(servers, null, "alpha-funded")).toBe("alpha-funded");
  });

  it("ignores servers that do not exist", () => {
    expect(initialServer(servers, "closed-firm", "nordic-prop")).toBe("nordic-prop");
    expect(initialServer(servers, "closed-firm", null)).toBe("");
  });

  it("chooses the only server", () => {
    expect(initialServer([servers[0]], null, null)).toBe("alpha-funded");
  });

  it("lets the trader choose among several", () => {
    expect(initialServer(servers, null, null)).toBe("");
  });
});

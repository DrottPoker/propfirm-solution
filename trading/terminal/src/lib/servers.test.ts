import { describe, expect, it } from "vitest";

import { initialServer, portalLogin } from "./servers";

const servers = [
  { id: "alpha-funded", name: "Alpha Funded", loginUrl: null },
  { id: "nordic-prop", name: "Nordic Prop", loginUrl: "https://nordic-prop.example.com/terminal" },
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

describe("portalLogin", () => {
  it("sends the trader to the firm's portal with the account to open", () => {
    expect(portalLogin(servers[1], "nordic-prop-1001-1")).toBe("https://nordic-prop.example.com/terminal?account=nordic-prop-1001-1");
  });

  it("keeps what the address already asks for", () => {
    expect(portalLogin({ ...servers[1], loginUrl: "https://portal.example.com/terminal?firm=a" }, "a-1")).toBe(
      "https://portal.example.com/terminal?firm=a&account=a-1",
    );
  });

  it("opens the portal without an account when none is known", () => {
    expect(portalLogin(servers[1], null)).toBe("https://nordic-prop.example.com/terminal");
  });

  it("has nothing for a firm whose traders log in with a password", () => {
    expect(portalLogin(servers[0], "alpha-1")).toBeNull();
    expect(portalLogin(null, "alpha-1")).toBeNull();
  });
});

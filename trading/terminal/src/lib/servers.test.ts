import { describe, expect, it } from "vitest";

import { standardProfile } from "./profile";
import { portalLogin } from "./servers";

const servers = [
  { id: "alpha-funded", name: "Alpha Funded", loginUrl: null, logoUrl: null, profile: standardProfile },
  { id: "nordic-prop", name: "Nordic Prop", loginUrl: "https://nordic-prop.example.com/terminal", logoUrl: null, profile: { ...standardProfile, passwordLogin: false } },
];

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

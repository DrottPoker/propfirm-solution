import { describe, expect, it } from "vitest";

import type { PlatformLogEntry } from "./api/types";
import { isWarning, logText } from "./platformLog";

const now = new Date("2026-10-08T14:32:00Z");
const entry = (fields: Partial<PlatformLogEntry> & Pick<PlatformLogEntry, "kind">): PlatformLogEntry => ({
  id: 1,
  at: "2026-10-08T14:20:00Z",
  serverId: null,
  staffEmail: null,
  detail: {},
  ...fields,
});

describe("platform log", () => {
  it("says who made a server", () => {
    expect(logText(entry({ kind: "ServerCreated", serverId: "fjord-traders", detail: { partner: "Kronant Prop", name: "Fjord Traders", currency: "EUR" } }), now)).toBe(
      "Kronant Prop made the server fjord-traders, Fjord Traders in EUR",
    );
    expect(logText(entry({ kind: "ServerCreated", serverId: "helix", staffEmail: "ops@test.com", detail: { name: "Helix", currency: "GBP", kind: "Practice" } }), now)).toBe(
      "ops@test.com made the server helix, Helix in GBP, type Practice",
    );
  });

  it("says who changed a server's type, and from what", () => {
    expect(logText(entry({ kind: "TerminalKindChanged", serverId: "helix", staffEmail: "ops@test.com", detail: { from: "Practice", kind: "Desk" } }), now)).toBe(
      "ops@test.com changed the type of helix from Practice to Trading desk",
    );
  });

  it("tells a partner's new key from one our staff stopped, with why", () => {
    expect(logText(entry({ kind: "AdminKeyReplaced", serverId: "nordic-edge", detail: { partner: "Kronant Prop" } }), now)).toBe(
      "New admin key for nordic-edge, asked for by Kronant Prop",
    );
    expect(logText(entry({ kind: "AdminKeyReplaced", serverId: "acme", staffEmail: "ops@test.com", detail: { reason: "Leaked" } }), now)).toBe(
      "ops@test.com stopped the admin key of acme: Leaked",
    );
  });

  it("describes the feed, the charts and a start", () => {
    expect(logText(entry({ kind: "SymbolSilent", detail: { symbol: "UK100", feed: "CapitalCom" } }), now)).toBe("UK100 went silent on Capital.com while its market is open");
    expect(logText(entry({ kind: "FeedBack", detail: { feed: "CapitalCom", silentSeconds: "240" } }), now)).toBe("Capital.com gives prices again after 4 min");
    expect(logText(entry({ kind: "ChartGapFilled", detail: { from: "2026-10-08T09:39:00Z", until: "2026-10-08T09:41:00Z", bars: "46" } }), now)).toBe(
      "Chart gap from 09:39 to 09:41 filled with 46 bars",
    );
    expect(logText(entry({ kind: "ServiceStarted", detail: { milliseconds: "2100", replayed: "8412" } }), now)).toBe(
      "The service started in 2.1 s, replaying 8,412 inputs after the snapshot",
    );
  });

  it("marks bad news", () => {
    expect(isWarning(entry({ kind: "FeedSilent" }))).toBe(true);
    expect(isWarning(entry({ kind: "ChartHistoryReloaded", detail: { problem: "Timeout" } }))).toBe(true);
    expect(isWarning(entry({ kind: "ChartHistoryReloaded", detail: { bars: "100" } }))).toBe(false);
  });
});

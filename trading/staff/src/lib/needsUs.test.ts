import { describe, expect, it } from "vitest";

import type { NeedsUsItem } from "./api/types";
import { needsUsText } from "./needsUs";

const now = new Date("2026-10-08T14:32:32Z");
const item = (fields: Partial<NeedsUsItem> & Pick<NeedsUsItem, "kind">): NeedsUsItem => ({ since: null, symbol: null, serverId: null, count: null, gap: null, ...fields });

describe("needs us", () => {
  it("says which symbol is silent, since when and whom it hurts", () => {
    const text = needsUsText(item({ kind: "SymbolSilent", symbol: "UK100", since: "2026-10-08T14:28:11Z", count: 38 }), "CapitalCom", now);

    expect(text.title).toBe("UK100 has had no price for 4 min 21 s while its market is open");
    expect(text.detail).toBe(
      "Capital.com, last price 14:28. 38 accounts hold UK100, and their orders, closes and stop changes on it are refused until prices return.",
    );
    expect(text.href).toBe("/price-feed");
  });

  it("says when nobody holds a silent symbol, or it never had a price", () => {
    const text = needsUsText(item({ kind: "SymbolSilent", symbol: "NATGAS", count: 0 }), "Tiingo", now);

    expect(text.title).toBe("NATGAS has had no price since the service started, while its market is open");
    expect(text.detail).toBe("Tiingo. No account holds NATGAS now.");
  });

  it("calls a whole silent feed bad news", () => {
    const text = needsUsText(item({ kind: "FeedSilent", since: "2026-10-08T14:30:32Z", count: 2 }), "CapitalCom", now);

    expect(text.title).toBe("No prices from Capital.com for 2 min while markets are open");
    expect(text.detail).toContain("2 accounts have open positions");
    expect(text.tone).toBe("loss");
  });

  it("points at the server whose system stopped reading", () => {
    const text = needsUsText(item({ kind: "EventsNotRead", serverId: "aurora-funded", since: "2026-10-08T14:23:02Z", count: 1001 }), "CapitalCom", now);

    expect(text.title).toBe("aurora-funded has not read its events for 9 min 30 s");
    expect(text.detail).toMatch(/^More than 1,000 events are waiting/);
    expect(text.href).toBe("/servers/aurora-funded");
  });

  it("describes a gap that could not be filled", () => {
    const gap = {
      id: "g1",
      from: "2026-10-08T03:12:00Z",
      until: "2026-10-08T03:19:00Z",
      foundAt: "2026-10-08T03:19:30Z",
      state: "NotFilled" as const,
      tries: 4,
      finishedAt: "2026-10-08T03:20:12Z",
      bars: 0,
      problem: "Too many requests",
    };
    const text = needsUsText(item({ kind: "ChartGapNotFilled", gap }), "CapitalCom", now);

    expect(text.title).toBe("A chart gap from 03:12 to 03:19 could not be filled");
    expect(text.detail).toMatch(/^Capital.com could not be asked after 4 tries, the last at 03:20\./);
  });

  it("sends a slow engine to the engine page", () => {
    expect(needsUsText(item({ kind: "QueueBehind", count: 1500 }), "CapitalCom", now).title).toBe("1,500 inputs are waiting for the engine");
    expect(needsUsText(item({ kind: "SlowSaves", count: 1250 }), "CapitalCom", now).title).toBe("Saving the journal took 1,250 ms at worst in the last 5 minutes");
  });
});

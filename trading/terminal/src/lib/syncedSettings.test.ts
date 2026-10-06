import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { createUploader, isSynced, parseUnsent, planSync, type SendResult } from "./syncedSettings";

describe("isSynced", () => {
  it.each([
    ["trading.favorites", true],
    ["trading.drawings.EURUSD", true],
    ["trading.server", false],
    ["trading.account", false],
    ["trading.settingsOwner", false],
    ["trading.settingsUnsent", false],
    ["theme", false],
    ["trading.drawings.EUR/USD", false],
  ])("%s follows the trader: %j", (key, expected) => {
    expect(isSynced(key)).toBe(expected);
  });
});

describe("planSync", () => {
  const owner = "demo/u1";
  const same = { localOwner: owner, owner, unsent: [] };

  it("takes the login's settings over the browser's", () => {
    const plan = planSync({ "trading.favorites": '["EURUSD"]', "trading.fillSound": "on" }, { "trading.favorites": '["XAUUSD"]', "trading.fillSound": "on" }, same);

    expect(plan).toEqual({ write: { "trading.favorites": '["XAUUSD"]' }, remove: [], upload: {} });
  });

  it("sends what only the browser has the first time the device is used", () => {
    const plan = planSync({ "trading.favorites": '["EURUSD"]', "trading.server": "demo" }, {}, { localOwner: null, owner, unsent: [] });

    expect(plan).toEqual({ write: {}, remove: [], upload: { "trading.favorites": '["EURUSD"]' } });
  });

  // Removed on another device, or another trader's.
  it.each([owner, "demo/u2"])("removes what only the browser has once it held settings of %s", (localOwner) => {
    const plan = planSync({ "trading.drawings.EURUSD": "[]", "trading.account": "A1" }, { "trading.chartVolume": "off" }, { localOwner, owner, unsent: [] });

    expect(plan).toEqual({ write: { "trading.chartVolume": "off" }, remove: ["trading.drawings.EURUSD"], upload: {} });
  });

  // For example a change made just before the page was reloaded.
  it("sends the trader's changes the login does not have yet, also a removal", () => {
    const plan = planSync(
      { "trading.volumes": '{"EURUSD":"0.50"}', "trading.chartVolume": "off" },
      { "trading.volumes": '{"EURUSD":"1.00"}', "trading.drawings.EURUSD": "[]", "trading.chartVolume": "off" },
      { ...same, unsent: ["trading.volumes", "trading.drawings.EURUSD", "trading.chartVolume"] },
    );

    expect(plan).toEqual({ write: {}, remove: [], upload: { "trading.volumes": '{"EURUSD":"0.50"}', "trading.drawings.EURUSD": null } });
  });

  it("never sends another trader's changes", () => {
    const plan = planSync({ "trading.volumes": '{"EURUSD":"0.50"}' }, { "trading.volumes": '{"EURUSD":"1.00"}' }, { localOwner: "demo/u2", owner, unsent: ["trading.volumes"] });

    expect(plan).toEqual({ write: { "trading.volumes": '{"EURUSD":"1.00"}' }, remove: [], upload: {} });
  });

  it("ignores values the terminal did not store", () => {
    const plan = planSync({}, { "trading.favorites": ["EURUSD"], "trading.server": "other", "portal.theme": "dark" }, same);

    expect(plan).toEqual({ write: {}, remove: [], upload: {} });
  });
});

describe("parseUnsent", () => {
  it("reads a list of keys and nothing else", () => {
    expect(parseUnsent('["trading.a",1,"trading.b"]')).toEqual(["trading.a", "trading.b"]);
    expect(parseUnsent("{}")).toEqual([]);
    expect(parseUnsent("not json")).toEqual([]);
    expect(parseUnsent(null)).toEqual([]);
  });
});

describe("createUploader", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  const recorder = (results: SendResult[] = []) => {
    const sent: [string, string | null][] = [];
    const send = vi.fn(async (key: string, value: string | null) => {
      sent.push([key, value]);
      return results.shift() ?? "done";
    });
    return { sent, send };
  };

  it("sends the latest value of each key a moment after the last change", async () => {
    const { sent, send } = recorder();
    const uploader = createUploader(send, { delayMs: 1_000, retryMs: 10_000 });

    uploader.queue("trading.volumes", '{"EURUSD":"1"}');
    await vi.advanceTimersByTimeAsync(500);
    uploader.queue("trading.volumes", '{"EURUSD":"2"}');
    uploader.queue("trading.drawings.EURUSD", null);
    await vi.advanceTimersByTimeAsync(999);
    const early = sent.length;
    await vi.advanceTimersByTimeAsync(1);

    expect(early).toBe(0);
    expect(sent).toEqual([
      ["trading.volumes", '{"EURUSD":"2"}'],
      ["trading.drawings.EURUSD", null],
    ]);
  });

  it("tries a failed send again later, unless a newer value came meanwhile", async () => {
    const sent: [string, string | null][] = [];
    const uploader = createUploader(
      async (key, value) => {
        sent.push([key, value]);
        // The trader changes b while it is sent.
        if (sent.length === 2) {
          uploader.queue("trading.b", "2");
        }

        return sent.length <= 2 ? "retry" : "done";
      },
      { delayMs: 1_000, retryMs: 10_000 },
    );

    uploader.queue("trading.a", "1");
    uploader.queue("trading.b", "1");
    await vi.advanceTimersByTimeAsync(1_000);
    await vi.advanceTimersByTimeAsync(9_999);
    const beforeRetry = sent.length;
    await vi.advanceTimersByTimeAsync(1);

    expect(beforeRetry).toBe(2);
    expect(sent).toEqual([
      ["trading.a", "1"],
      ["trading.b", "1"],
      ["trading.b", "2"],
      ["trading.a", "1"],
    ]);
  });

  it("sends nothing more once the session ended", async () => {
    const { sent, send } = recorder(["stop"]);
    const uploader = createUploader(send, { delayMs: 1_000, retryMs: 10_000 });

    uploader.queue("trading.a", "1");
    await vi.advanceTimersByTimeAsync(1_000);
    uploader.queue("trading.a", "2");
    await vi.advanceTimersByTimeAsync(20_000);

    expect(sent).toEqual([["trading.a", "1"]]);
  });

  it("tells which keys are sent, once their latest value is", async () => {
    const done: string[] = [];
    const uploader = createUploader(async () => "done", { sent: (key) => done.push(key), delayMs: 1_000, retryMs: 10_000 });

    uploader.queue("trading.a", "1");
    await vi.advanceTimersByTimeAsync(1_000);

    expect(done).toEqual(["trading.a"]);
  });

  it("sends at once when flushed", async () => {
    const { sent, send } = recorder();
    const uploader = createUploader(send, { delayMs: 1_000, retryMs: 10_000 });

    uploader.queue("trading.a", "1");
    await uploader.flush(true);

    expect(sent).toEqual([["trading.a", "1"]]);
    expect(send).toHaveBeenCalledWith("trading.a", "1", true);
  });
});

import { describe, expect, it } from "vitest";

import type { EventEnvelope } from "./api/types";
import { createEventSync, type EventQuery } from "./eventSync";
import { maxEvents, mergeEvents } from "./store";

function envelope(sequence: number): EventEnvelope {
  return {
    sequence,
    event: { kind: "EquityFloorRemoved", accountId: "demo", floorId: `f${sequence}`, timestamp: "2026-10-05T08:00:00+00:00" },
  };
}

const range = (first: number, last: number) => Array.from({ length: last - first + 1 }, (_, i) => first + i);

/** An account whose events are numbered 1 to `last` in the service, and a terminal that keeps them like the store. */
function setup(last: number) {
  const service = {
    last,
    failures: 0,
    queries: [] as EventQuery[],
    gate: null as Promise<void> | null,
    /** Holds back the answer to the next query until released. The answer is read when the query is made. */
    hold() {
      let release = () => {};
      service.gate = new Promise<void>((resolve) => (release = resolve));
      return release;
    },
  };

  const fetchEvents = async (query: EventQuery) => {
    service.queries.push(query);
    if (service.failures > 0) {
      service.failures--;
      throw new Error("Could not load events (HTTP 503).");
    }

    const all = range(1, service.last);
    const after = query.after;
    const page = after === undefined ? all.slice(-query.limit) : all.filter((s) => s > after).slice(0, query.limit);
    const gate = service.gate;
    service.gate = null;
    await gate;
    return page.map(envelope);
  };

  let stored: EventEnvelope[] = [];
  const store = (events: readonly EventEnvelope[]) => {
    stored = mergeEvents(stored, events);
  };
  const sync = createEventSync(fetchEvents, store, maxEvents);

  return {
    service,
    sync,
    /** A command response, which goes straight to the store. */
    commandResponse: (sequence: number) => store([envelope(sequence)]),
    push: (...sequences: number[]) => sync.received(sequences.map(envelope)),
    stored: () => stored.map((e) => e.sequence),
  };
}

const catchUp = (after: number): EventQuery => ({ after, limit: maxEvents });
const latest: EventQuery = { limit: maxEvents };

describe("createEventSync", () => {
  it("starts from the latest events when the account has more than the store keeps", async () => {
    const t = setup(2_500);

    await t.sync.subscribing().run();

    expect(t.service.queries).toEqual([latest]);
    expect(t.stored()).toEqual(range(1_501, 2_500));
  });

  it("catches up with after from the last event it has after a reconnect", async () => {
    const t = setup(10);
    await t.sync.subscribing().run();
    t.service.last = 15;

    await t.sync.subscribing().run();

    expect(t.service.queries[1]).toEqual(catchUp(10));
    expect(t.stored()).toEqual(range(1, 15));
  });

  it("catches up from the last event pushed in realtime", async () => {
    const t = setup(10);
    await t.sync.subscribing().run();
    t.service.last = 12;
    t.push(11, 12);
    t.service.last = 14;

    await t.sync.subscribing().run();

    expect(t.service.queries[1]).toEqual(catchUp(12));
    expect(t.stored()).toEqual(range(1, 14));
  });

  it("does not skip missed events when a command response arrives first", async () => {
    const t = setup(10);
    await t.sync.subscribing().run();
    // While disconnected a stop loss closes a position (11), then the trader places an order (12).
    t.service.last = 12;
    t.commandResponse(12);

    await t.sync.subscribing().run();

    expect(t.service.queries[1]).toEqual(catchUp(10));
    expect(t.stored()).toEqual(range(1, 12));
  });

  it("does not skip missed events when the new subscription pushes before the load", async () => {
    const t = setup(10);
    await t.sync.subscribing().run();
    t.service.last = 12;
    const load = t.sync.subscribing();
    t.push(12);

    await load.run();

    expect(t.service.queries[1]).toEqual(catchUp(10));
    expect(t.stored()).toEqual(range(1, 12));
  });

  it("loads the latest page instead of paging through a long disconnect", async () => {
    const t = setup(10);
    await t.sync.subscribing().run();
    t.service.last = 5_000;

    await t.sync.subscribing().run();

    expect(t.service.queries.slice(1)).toEqual([catchUp(10), latest]);
    expect(t.stored()).toEqual(range(4_001, 5_000));
  });

  it("runs a failed load again from the same point", async () => {
    const t = setup(10);
    await t.sync.subscribing().run();
    t.service.last = 12;
    const load = t.sync.subscribing();
    t.service.failures = 1;

    await expect(load.run()).rejects.toThrow("HTTP 503");
    await load.run();

    expect(t.service.queries.slice(1)).toEqual([catchUp(10), catchUp(10)]);
    expect(t.stored()).toEqual(range(1, 12));
  });

  it("catches up from the last complete point when the load before a reconnect failed", async () => {
    const t = setup(10);
    await t.sync.subscribing().run();
    t.service.last = 12;
    const failed = t.sync.subscribing();
    t.service.failures = 1;
    await expect(failed.run()).rejects.toThrow();
    // Pushed by the subscription that never loaded, so it says nothing about 11 and 12.
    t.push(13);
    t.service.last = 14;

    const next = t.sync.subscribing();
    await next.run();

    expect(failed.current).toBe(false);
    expect(t.service.queries.at(-1)).toEqual(catchUp(10));
    expect(t.stored()).toEqual(range(1, 14));
  });

  it("starts from the latest events again when no load has succeeded yet", async () => {
    const t = setup(10);
    t.service.failures = 1;
    await expect(t.sync.subscribing().run()).rejects.toThrow();

    await t.sync.subscribing().run();

    expect(t.service.queries).toEqual([latest, latest]);
    expect(t.stored()).toEqual(range(1, 10));
  });

  it("ignores a load that ends after a newer subscription has started", async () => {
    const t = setup(10);
    await t.sync.subscribing().run();
    t.service.last = 11;
    const stale = t.sync.subscribing();
    const release = t.service.hold();
    const staleRun = stale.run();
    // The connection drops again before the load answers, and 12 and 13 are missed.
    t.service.last = 14;
    const current = t.sync.subscribing();
    t.push(14);
    release();
    await staleRun;
    t.service.failures = 1;
    await expect(current.run()).rejects.toThrow();

    await t.sync.subscribing().run();

    expect(t.service.queries.at(-1)).toEqual(catchUp(10));
    expect(t.stored()).toEqual(range(1, 14));
  });
});

import type { EventEnvelope } from "./api/types";

/** A page of the account's events. Without `after` the latest ones, with it the first ones after that sequence number. */
export interface EventQuery {
  after?: number;
  limit: number;
}

/** Fetches a page of the account's events, oldest first. */
export type FetchEvents = (query: EventQuery) => Promise<EventEnvelope[]>;

/** Loads the events one subscription needs. Can run again if it fails. */
export interface EventLoad {
  run(): Promise<void>;
  /** False once a newer subscription has started and this load is no longer needed. */
  readonly current: boolean;
}

export interface EventSync {
  /** Events pushed in realtime. */
  received(events: readonly EventEnvelope[]): void;
  /** Call right before each subscription. Returns the load to run once subscribed. */
  subscribing(): EventLoad;
}

/**
 * Keeps the account's events complete across reconnects. The first subscription loads the latest page. Later ones
 * catch up with `after` from the last event that has no gap before it, also when the load before them failed. Events
 * from command responses never move that point, since they can arrive before earlier events pushed in realtime.
 *
 * `pageSize` must be at least what the store keeps and at most what the service returns in one call, so that one
 * page of the latest events can replace a gap that is too long to catch up.
 */
export function createEventSync(
  fetchEvents: FetchEvents,
  addEvents: (events: readonly EventEnvelope[]) => void,
  pageSize: number,
): EventSync {
  // The store has every event of the account up to this sequence number. Null until a load has succeeded.
  let complete: number | null = null;
  // Whether the current subscription has loaded, so that what it pushes continues from `complete`.
  let loaded = false;
  // The highest sequence number pushed since the current subscription started.
  let pushed = 0;
  let subscription = 0;

  const load = async (after: number | null): Promise<number> => {
    let last = after ?? 0;
    if (after !== null) {
      const missed = await fetchEvents({ after, limit: pageSize });
      addEvents(missed);
      last = Math.max(last, lastSequence(missed));
      if (missed.length < pageSize) {
        return last;
      }
      // More was missed than the store keeps. The latest page replaces the gap, so the rest is not paged through.
    }

    const latest = await fetchEvents({ limit: pageSize });
    addEvents(latest);
    return Math.max(last, lastSequence(latest));
  };

  return {
    received(events) {
      addEvents(events);
      pushed = Math.max(pushed, lastSequence(events));
      if (loaded) {
        complete = Math.max(complete ?? 0, pushed);
      }
    },
    subscribing() {
      // Taken before subscribing, so that events pushed by the new subscription cannot hide the ones missed before it.
      const id = ++subscription;
      const after = complete;
      loaded = false;
      pushed = 0;
      return {
        run: async () => {
          const last = await load(after);
          // The store has everything up to the last loaded event, and the subscription has pushed everything after it.
          if (id === subscription) {
            complete = Math.max(last, pushed);
            loaded = true;
          }
        },
        get current() {
          return id === subscription;
        },
      };
    },
  };
}

function lastSequence(events: readonly EventEnvelope[]): number {
  return events.reduce((last, e) => Math.max(last, e.sequence), 0);
}

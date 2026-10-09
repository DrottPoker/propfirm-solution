// Charts and series that were removed. React cleans up the chart before the effects that drew on it when the chart
// unmounts, and a removed chart's lines and subscriptions went with it: touching them makes the chart library try to
// draw on a canvas that is gone, which throws.
const removed = new WeakSet<object>();

/** Marks a chart and its series as removed, just before the chart is. */
export function markRemoved(...parts: object[]) {
  for (const part of parts) {
    removed.add(part);
  }
}

/** Whether the chart or series was removed, so its lines need no cleaning up. */
export function isRemoved(part: object): boolean {
  return removed.has(part);
}

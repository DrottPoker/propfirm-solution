import { describe, expect, it } from "vitest";

import { reconnectPolicy } from "./realtime";

describe("reconnectPolicy", () => {
  it("tries at once, then every two seconds without giving up", () => {
    const delays = [0, 1, 10, 1_000].map((previousRetryCount) =>
      reconnectPolicy.nextRetryDelayInMilliseconds({ previousRetryCount, elapsedMilliseconds: previousRetryCount * 2_000, retryReason: new Error("closed") }),
    );

    expect(delays).toEqual([0, 2_000, 2_000, 2_000]);
  });
});

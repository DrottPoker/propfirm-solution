import type { Billing, Verification } from "./api/types";

/** The four steps on the way to live, as the Go live page shows them. */
export type GoLiveStepKey = "details" | "deposit" | "answer" | "payment";

/** Where a step is: done, the one to do now, later, waiting for us, needing the firm again, or not possible yet. */
export type GoLiveStepState = "done" | "current" | "todo" | "waiting" | "attention" | "locked";

export const goLiveStepTitles: Record<GoLiveStepKey, string> = {
  details: "Company details",
  deposit: "Deposit",
  answer: "Our answer",
  payment: "Slots and payment",
};

export const goLiveStepKeys: GoLiveStepKey[] = ["details", "deposit", "answer", "payment"];

export const goLiveStateLabels: Record<GoLiveStepState, string> = {
  done: "Done",
  current: "To do now",
  todo: "Next",
  waiting: "With us",
  attention: "Changes needed",
  locked: "Later",
};

/**
 * Where each step is, from our review and the billing. The details are done once nothing is missing, the deposit once
 * the application is sent, our answer once we have approved, and the last step once the firm is live. Only the step
 * after the one to do now is next; the ones after it are later.
 */
export function goLiveStates(verification: Verification, billing: Billing): Record<GoLiveStepKey, GoLiveStepState> {
  const review = verification.status;
  const ready = verification.problems.length === 0;
  const live = billing.status === "Live";
  return {
    details: review === "ChangesRequested" ? "current" : review === "Draft" && !ready ? "current" : "done",
    deposit: review === "Draft" ? (ready ? "current" : "todo") : review === "ChangesRequested" ? "todo" : "done",
    answer:
      review === "Submitted"
        ? "waiting"
        : review === "ChangesRequested"
          ? "attention"
          : review === "Approved" || live
            ? "done"
            : review === "Rejected"
              ? "locked"
              : ready
                ? "todo"
                : "locked",
    payment: live ? "done" : review === "Approved" ? "current" : "locked",
  };
}

/** The step to show: the one in the address, or else the first that needs the firm or us. */
export function goLiveStepOf(value: string | string[] | undefined, states: Record<GoLiveStepKey, GoLiveStepState>): GoLiveStepKey {
  const chosen = goLiveStepKeys.find((key) => key === value);
  if (chosen) {
    return chosen;
  }

  // With nothing open, the firm is live, or we did not approve it, which our answer says.
  return goLiveStepKeys.find((key) => ["current", "waiting", "attention"].includes(states[key])) ?? (states.payment === "done" ? "payment" : "answer");
}

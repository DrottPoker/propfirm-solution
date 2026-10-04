import type { AccountDetails, StageSummary } from "@/lib/api/types";
import { stepState, type StepState } from "@/lib/dashboard";
import { formatDate, formatSignedMoney } from "@/lib/format";

const stepText: Record<StepState, string> = {
  passed: "text-profit",
  current: "font-medium text-foreground",
  waiting: "font-medium text-foreground",
  failed: "font-medium text-loss",
  cancelled: "text-muted",
  upcoming: "text-muted",
};

const stepNames: Record<StepState, string> = {
  passed: "passed",
  current: "now",
  waiting: "waiting for the firm",
  failed: "failed",
  cancelled: "cancelled",
  upcoming: "to come",
};

function StepMark({ state }: { state: StepState }) {
  if (state === "passed") {
    return (
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
        <path d="M20 6 9 17l-5-5" />
      </svg>
    );
  }

  if (state === "failed") {
    return (
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" aria-hidden="true">
        <path d="M18 6 6 18" />
        <path d="m6 6 12 12" />
      </svg>
    );
  }

  const filled = state === "current" || state === "waiting";
  return <span aria-hidden="true" className={`size-2 rounded-full ${filled ? "bg-accent" : "border border-muted"}`} />;
}

/** The stages in a row, for a card: which are passed, which is now and which are still to come. */
export function StageStepper({ details }: { details: AccountDetails }) {
  const { stages, account } = details;
  return (
    <ol aria-label="Stages" className="flex items-center gap-1.5 text-xs">
      {stages.map((stage, index) => {
        const state = stepState(stage, account.status);
        return (
          <li key={stage.stage} className={`flex min-w-0 items-center gap-1.5 ${index > 0 ? "flex-1" : ""}`} aria-current={stage.progress === "Current" ? "step" : undefined}>
            {index > 0 && <span aria-hidden="true" className={`h-px min-w-3 flex-1 ${state === "passed" ? "bg-profit/50" : "bg-border"}`} />}
            <span className={`flex shrink-0 items-center gap-1.5 ${stepText[state]}`}>
              <StepMark state={state} />
              {stage.name}
              <span className="sr-only">, {stepNames[state]}</span>
            </span>
          </li>
        );
      })}
    </ol>
  );
}

/** The stages as tiles, for the account page: when each was passed and with what result, and what comes next. */
export function StageTiles({ details }: { details: AccountDetails }) {
  const { stages, account } = details;
  return (
    <ol aria-label="Stages" className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
      {stages.map((stage) => {
        const state = stepState(stage, account.status);
        return (
          <li
            key={stage.stage}
            aria-current={stage.progress === "Current" ? "step" : undefined}
            className={`flex gap-3 rounded-lg border bg-panel px-4 py-3.5 ${stage.progress === "Current" && state !== "cancelled" ? (state === "failed" ? "border-loss/60" : "border-accent") : "border-border"}`}
          >
            <TileMark state={state} number={stage.stage + 1} />
            <span className="flex min-w-0 flex-col gap-0.5">
              <span className={`font-medium ${state === "upcoming" ? "text-muted" : ""}`}>
                {stage.name} · {tileState(state)}
              </span>
              <span className="text-xs text-muted">{tileDetail(stage, state, details)}</span>
            </span>
          </li>
        );
      })}
    </ol>
  );
}

function TileMark({ state, number }: { state: StepState; number: number }) {
  const style =
    state === "passed"
      ? "bg-profit/15 text-profit"
      : state === "failed"
        ? "bg-loss/15 text-loss"
        : state === "current" || state === "waiting"
          ? "bg-accent text-accent-foreground"
          : "border border-muted text-muted";
  return (
    <span aria-hidden="true" className={`grid size-7 shrink-0 place-items-center rounded-full text-xs font-semibold ${style}`}>
      {state === "passed" || state === "failed" ? <StepMark state={state} /> : number}
    </span>
  );
}

function tileState(state: StepState): string {
  switch (state) {
    case "passed":
      return "Passed";
    case "current":
      return "Now";
    case "waiting":
      return "Under review";
    case "failed":
      return "Failed";
    case "cancelled":
      return "Cancelled";
    case "upcoming":
      return "To come";
  }
}

function tileDetail(stage: StageSummary, state: StepState, details: AccountDetails): string {
  const split = details.challenge.funded.profitSplitPercent;
  const isFunded = stage.stage === details.challenge.evaluation.length;
  switch (state) {
    case "passed": {
      const days = stage.tradingDays == null ? "" : ` in ${stage.tradingDays} trading ${stage.tradingDays === 1 ? "day" : "days"}`;
      const when = stage.passedAt ? `${formatDate(stage.passedAt)} · ` : "";
      return stage.result == null ? when.replace(/ · $/, "") : `${when}${formatSignedMoney(stage.result)}${days}`;
    }
    case "current":
      return stage.startedAt
        ? `Since ${formatDate(stage.startedAt)}${stage.tradingDays == null ? "" : ` · ${stage.tradingDays} trading ${stage.tradingDays === 1 ? "day" : "days"} so far`}`
        : "The trading account is being opened.";
    case "waiting":
      return "Every evaluation stage is passed. The firm is reviewing the funded account.";
    case "failed":
      return details.endedAt ? `Ended ${formatDate(details.endedAt)}` : "Ended";
    case "cancelled":
      return details.endedAt ? `Cancelled ${formatDate(details.endedAt)}` : "Cancelled by the firm";
    case "upcoming":
      return isFunded && split != null ? `${split}% of the profit is yours` : "Starts when the stage before is passed";
  }
}

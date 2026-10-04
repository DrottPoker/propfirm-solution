import { floorLabel, kindOf } from "./challenge";
import { formatDay, formatMoney } from "./format";

// The rule log in plain sentences (finding 70). The rule engine's own names stay behind "Show details". Every value is
// read defensively, since the log keeps steps written by older versions too.

type Json = Record<string, unknown>;

/** What the log needs to know about the account: its currency and the names of its stages. */
export type LogContext = { currency: string; stageName: (stage: number) => string };

const money = (value: unknown, context: LogContext) => `${formatMoney(typeof value === "number" ? value : null)} ${context.currency}`;
const text = (value: unknown) => (typeof value === "string" ? value : "");
const number = (value: unknown) => (typeof value === "number" ? value : 0);
const day = (value: unknown) => (typeof value === "string" ? formatDay(value) : "");
const object = (value: unknown): Json => (typeof value === "object" && value !== null ? (value as Json) : {});

/** What happened, as the rule engine was told, in a sentence. */
export function describeInput(input: unknown, context: LogContext): string {
  const i = object(input);
  switch (i.kind) {
    case "ChallengeStarted":
      return i.reference ? `The challenge started, with your reference ${text(i.reference)}` : "The challenge started";
    case "AccountOpened":
      return `Trading account ${text(i.accountId)} opened`;
    case "TradingDayStarted":
      return `Trading day ${day(i.day)} started`;
    case "PauseChallenge":
      return "You paused the challenge";
    case "ResumeChallenge":
      return "You resumed the challenge";
    case "ApproveFunding":
      return "You approved the funded account";
    case "CancelChallenge":
      return i.reason ? `You cancelled the challenge: ${text(i.reason)}` : "You cancelled the challenge";
    case "RequestPayout":
      return "The trader asked for a payout";
    case "ApprovePayout":
      return "You approved the payout";
    case "MarkPayoutPaid":
      return i.reference ? `You marked the payout as paid, reference ${text(i.reference)}` : "You marked the payout as paid";
    case "RejectPayout":
      return `You rejected the payout${i.returnProfit ? " and put the profit back" : ""}${i.reason ? `: ${text(i.reason)}` : ""}`;
    case "WithdrawalRejected":
      return `The trading platform refused to take the profit off: ${text(i.reason)}`;
    case "AccountUpdated":
      return `Balance ${money(i.balance, context)}, ${number(i.openPositions)} open ${number(i.openPositions) === 1 ? "position" : "positions"}`;
    case "PositionOpened":
      return `A position was opened on ${day(i.day)}`;
    case "FloorBreached":
      return `${floorLabel(text(i.floorId))} broken: equity ${money(i.equity, context)} fell below ${formatMoney(number(i.level))}`;
    case "AccountDisabled":
      return "The trading account was closed on the trading platform";
    case "BalanceAdjusted":
      return `${number(i.amount) >= 0 ? "Deposit" : "Withdrawal"} of ${money(Math.abs(number(i.amount)), context)}, balance ${formatMoney(number(i.balance))}`;
    default:
      return kindOf(i);
  }
}

/** What the rule engine decided, a sentence for each decision. */
export function describeOutputs(outputs: unknown, context: LogContext): string[] {
  return (Array.isArray(outputs) ? outputs : []).map((output) => describeOutput(object(output), context));
}

function describeOutput(o: Json, context: LogContext): string {
  const payout = object(o.payout);
  switch (o.kind) {
    case "OpenAccountRequested":
      return `Open a trading account for ${context.stageName(number(o.stage))} with ${money(o.initialBalance, context)}`;
    case "FloorRequested":
      return `${floorLabel(text(o.floorId))} ${floorText(object(o.floor), context)}`;
    case "CloseAccountRequested":
      return `Close trading account ${text(o.accountId)}`;
    case "StageStarted":
      return `${context.stageName(number(o.stage))} started`;
    case "TradingDayCounted":
      return `${day(o.day)} counts as a trading day, ${number(o.tradingDays)} so far`;
    case "StagePassed":
      return `${context.stageName(number(o.stage))} passed with a balance of ${money(o.balance, context)} in ${number(o.tradingDays)} trading ${number(o.tradingDays) === 1 ? "day" : "days"}`;
    case "FundingAwaited":
      return "Every evaluation stage is passed: waiting for you to approve the funded account";
    case "ChallengeFailed":
      return `Failed: equity ${money(o.equity, context)} fell below the ${floorLabel(text(o.floorId)).toLowerCase()} at ${formatMoney(number(o.level))}`;
    case "ChallengeExpired":
      return o.reason === "Inactivity" ? "Ended: no new trade for too long" : "Ended: the time limit ran out";
    case "ChallengeCancelled":
      return o.reason ? `Cancelled: ${text(o.reason)}` : "Cancelled";
    case "ChallengePaused":
      return "Paused: no new trades, and the days do not count";
    case "ChallengeResumed":
      return `Going on again after ${number(o.daysPaused)} ${number(o.daysPaused) === 1 ? "day" : "days"} paused`;
    case "SuspendAccountRequested":
      return `Stop new trades on ${text(o.accountId)}`;
    case "ResumeAccountRequested":
      return `Allow new trades on ${text(o.accountId)} again`;
    case "PayoutRequested":
      return `Payout of ${money(payout.amount, context)} asked for, ${number(payout.profitSplitPercent)}% of ${formatMoney(number(payout.profit))} profit`;
    case "WithdrawalRequested":
      return `Take the profit of ${money(o.amount, context)} off ${text(o.accountId)}`;
    case "PayoutWithdrawn":
      return `The profit was taken off, the balance is ${money(o.balanceAfter, context)}`;
    case "PayoutApproved":
      return "The payout is approved, waiting for you to send the money";
    case "PayoutPaid":
      return "The payout is paid";
    case "DepositRequested":
      return `Put ${money(o.amount, context)} back on ${text(o.accountId)}`;
    case "PayoutRejected":
      return o.profitReturned ? "The payout is rejected, and the profit goes back on the account" : "The payout is rejected, and the profit stays off the account";
    case "PayoutFailed":
      return `The payout failed: ${text(o.reason)}`;
    case "InputIgnored":
      return `Nothing changed: ${text(o.reason)}`;
    default:
      return kindOf(o);
  }
}

function floorText(floor: Json, context: LogContext): string {
  switch (floor.kind) {
    case "StartOfDayFloor":
      return `set ${money(floor.distance, context)} below where each day starts`;
    case "FixedFloor":
      return `set at ${money(floor.level, context)}`;
    case "TrailingFloor":
      return `set ${money(floor.distance, context)} below the highest balance, until it reaches ${formatMoney(number(floor.lockLevel))}`;
    default:
      return "set";
  }
}

import type { AccountDetails, StageRules, StageSummary } from "./api/types";
import { hasEnded } from "./dashboard";
import { formatMoney } from "./format";

// The rules of every phase of a bought challenge, as a table with a column per phase. A rule that is the same in every
// phase where it applies is one value across them, so the table does not say the same thing three times.

type Rule = { label: string; value: (rules: StageRules, stage: StageSummary, funded: boolean, trader: string) => string | null };

const rules: Rule[] = [
  {
    label: "Profit target",
    value: (rules, stage) =>
      rules.profitTargetPercent == null || stage.profitTarget == null ? "None" : `${rules.profitTargetPercent}% · ${formatMoney(stage.profitTarget)}`,
  },
  {
    label: "Daily loss limit",
    value: (rules, stage) =>
      `${rules.dailyLoss.percent}% · ${formatMoney(stage.dailyLoss)} below ${rules.dailyLoss.reference === "Balance" ? "the balance" : "the higher of balance and equity"} when the day starts`,
  },
  {
    label: "Max loss limit",
    value: (rules, stage) =>
      rules.maxLoss.kind === "Fixed"
        ? `${rules.maxLoss.percent}% · ${formatMoney(stage.maxLoss)} below the initial balance`
        : `${rules.maxLoss.percent}% · ${formatMoney(stage.maxLoss)} below the highest equity, rising no higher than the initial balance`,
  },
  {
    label: "Minimum trading days",
    value: (rules, _, funded) => (rules.minTradingDays === 0 ? "None" : funded ? `${rules.minTradingDays} per payout` : `${rules.minTradingDays}`),
  },
  { label: "Time limit", value: (rules) => (rules.maxDays == null ? "None" : `${rules.maxDays} days`) },
  { label: "Profit split", value: (rules, _, __, trader) => (rules.profitSplitPercent == null ? null : `${rules.profitSplitPercent}% to ${trader}`) },
  {
    label: "Consistency",
    value: (rules, _, funded) =>
      !funded ? null : rules.consistencyPercent == null ? "None" : `The best day since the last payout made at most ${rules.consistencyPercent}% of the profit`,
  },
];

/** A phase's column: its name, and whether the account is in it now. */
export type RuleColumn = { stage: number; name: string; now: boolean };

/**
 * A rule's row. <code>same</code> is the value when it is the same in every phase, and then <code>values</code> is not
 * used. A value is null in a phase where the rule does not apply, such as the profit split before funded.
 */
export type RuleRow = { label: string; values: (string | null)[]; same: string | null };

export type RuleTable = { columns: RuleColumn[]; rows: RuleRow[] };

export function ruleTable(details: AccountDetails, audience: "trader" | "firm"): RuleTable {
  const { challenge, stages, account } = details;
  const allRules = [...challenge.evaluation, challenge.funded];
  const trader = audience === "firm" ? "the trader" : "you";
  // An account that has ended has no phase now.
  const columns = stages.map((stage) => ({
    stage: stage.stage,
    name: stage.name,
    now: stage.stage === account.stage && stage.progress === "Current" && !hasEnded(account.status),
  }));
  const rows = rules.map((rule) => {
    const values = stages.map((stage) => rule.value(allRules[stage.stage], stage, stage.stage === challenge.evaluation.length, trader));
    const same = stages.length > 1 && values.every((v) => v !== null && v === values[0]) ? values[0] : null;
    return { label: rule.label, values, same };
  });
  return { columns, rows };
}

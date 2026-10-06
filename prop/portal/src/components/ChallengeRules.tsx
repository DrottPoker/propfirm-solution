import type { AccountDetails, StageRules, StageSummary } from "@/lib/api/types";
import { hasEnded } from "@/lib/dashboard";
import { formatMoney } from "@/lib/format";

import { Panel } from "./ui";

type Row = { label: string; value: (rules: StageRules, stage: StageSummary, funded: boolean, trader: string) => string };

const rows: Row[] = [
  {
    label: "Profit target",
    value: (rules, stage) => (rules.profitTargetPercent == null || stage.profitTarget == null ? "None" : `${rules.profitTargetPercent}% · ${formatMoney(stage.profitTarget)}`),
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
  { label: "Profit split", value: (rules, _, __, trader) => (rules.profitSplitPercent == null ? "-" : `${rules.profitSplitPercent}% to ${trader}`) },
  {
    label: "Consistency",
    value: (rules, _, funded) =>
      !funded ? "-" : rules.consistencyPercent == null ? "None" : `The best day since the last payout made at most ${rules.consistencyPercent}% of the profit`,
  },
];

/** The rules of every stage of the challenge, as it was bought. Later changes by the firm do not touch it. The firm sees them about its trader. */
export function ChallengeRules({ details, audience = "trader" }: { details: AccountDetails; audience?: "trader" | "firm" }) {
  const { challenge, stages, account } = details;
  const allRules = [...challenge.evaluation, challenge.funded];
  // An account that has ended has no stage now.
  const now = (stage: StageSummary) => stage.stage === account.stage && stage.progress === "Current" && !hasEnded(account.status);
  return (
    <Panel title={audience === "firm" ? "Rules the trader bought" : "Rules of this challenge"}>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[40rem] text-sm">
          <thead className="text-left text-muted">
            <tr>
              <td className="py-2" />
              {stages.map((stage) => (
                <th key={stage.stage} scope="col" className={`py-2 pl-4 font-normal ${stage.stage === account.stage ? "font-medium text-foreground" : ""}`}>
                  {stage.name}
                  {now(stage) && " · now"}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.label} className="border-t border-border align-top">
                <th scope="row" className="py-2.5 pr-2 text-left font-normal text-muted">
                  {row.label}
                </th>
                {stages.map((stage) => (
                  <td key={stage.stage} className="py-2.5 pl-4">
                    {row.value(allRules[stage.stage], stage, stage.stage === challenge.evaluation.length, audience === "firm" ? "the trader" : "you")}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="text-xs text-muted">
        A trading day starts at {challenge.tradingDay.start.slice(0, 5)} in {challenge.tradingDay.timeZone.replaceAll("_", " ")}.
        {challenge.inactivityDays != null && ` The challenge ends if no new trade is opened for ${challenge.inactivityDays} days.`} The account starts at{" "}
        {formatMoney(challenge.initialBalance)} {challenge.currency} in every stage.
      </p>
    </Panel>
  );
}

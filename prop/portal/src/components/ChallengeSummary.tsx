import type { ChallengeDefinition } from "@/lib/api/types";
import { formatMoney } from "@/lib/format";

/** A challenge's size, trading day and the rules of each stage, in a few lines. The id is for the firm, not for buyers. */
export function ChallengeSummary({ challenge, showId = true }: { challenge: ChallengeDefinition; showId?: boolean }) {
  return (
    <div className="flex flex-col gap-2 text-sm">
      <span className="font-medium">
        {challenge.name} {showId && <span className="text-muted">({challenge.id})</span>}
      </span>
      <span>
        {formatMoney(challenge.initialBalance)} {challenge.currency}, trading days start at {challenge.tradingDay.start.slice(0, 5)}{" "}
        {challenge.tradingDay.timeZone}
      </span>
      <ul className="text-muted">
        {[...challenge.evaluation, challenge.funded].map((stage) => (
          <li key={stage.name}>
            {stage.name}: {stage.profitTargetPercent === null ? "no target" : `${stage.profitTargetPercent}% target`}, {stage.dailyLoss.percent}% daily
            loss, {stage.maxLoss.percent}% max loss ({stage.maxLoss.kind.toLowerCase()})
            {stage.minTradingDays > 0 && `, at least ${stage.minTradingDays} trading days${stage.profitSplitPercent == null ? "" : " between payouts"}`}
            {stage.profitSplitPercent != null && `, ${stage.profitSplitPercent}% of the profit to the trader`}
            {stage.maxDays != null && `, to be passed within ${stage.maxDays} days`}
          </li>
        ))}
      </ul>
      {challenge.inactivityDays != null && (
        <span className="text-muted">Ends after {challenge.inactivityDays} days without a new trade.</span>
      )}
    </div>
  );
}

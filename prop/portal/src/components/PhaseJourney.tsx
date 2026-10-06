import type { ChallengeDefinition } from "@/lib/api/types";

import { TrophyIcon } from "./icons";

/**
 * A challenge's way to funded, drawn: each phase before funded as a numbered step with its target, joined by a line to
 * the funded account and its profit split. Says the same as its list does for screen readers.
 */
export function PhaseJourney({ challenge, compact = false }: { challenge: Pick<ChallengeDefinition, "evaluation" | "funded">; compact?: boolean }) {
  const steps = challenge.evaluation.map((stage, index) => ({
    key: `${index}-${stage.name}`,
    mark: String(index + 1),
    name: stage.name,
    detail: stage.profitTargetPercent == null ? "No target" : `${stage.profitTargetPercent}% target`,
  }));

  return (
    <ol aria-label="Phases" className={`flex items-start ${compact ? "gap-0 text-[11px]" : "gap-0 text-xs"}`}>
      {steps.length === 0 && (
        <li className="flex min-w-0 flex-1 flex-col items-center gap-1.5 text-center">
          <Mark muted>0</Mark>
          <span className="text-muted">No evaluation</span>
        </li>
      )}
      {steps.map((step) => (
        <li key={step.key} className="flex min-w-0 flex-1 items-start">
          <span className="flex min-w-0 flex-1 flex-col items-center gap-1.5 text-center">
            <Mark>{step.mark}</Mark>
            <span className="flex flex-col leading-tight">
              <span className="truncate font-medium">{step.name}</span>
              <span className="text-muted">{step.detail}</span>
            </span>
          </span>
          <span aria-hidden="true" className="mt-3.5 h-px w-6 shrink-0 bg-gradient-to-r from-border to-profit/50 sm:w-10" />
        </li>
      ))}
      <li className="flex min-w-0 flex-1 flex-col items-center gap-1.5 text-center">
        <span className="grid size-7 place-items-center rounded-full bg-profit/15 text-profit ring-1 ring-profit/40">
          <TrophyIcon className="size-4" />
        </span>
        <span className="flex flex-col leading-tight">
          <span className="truncate font-medium text-profit">{challenge.funded.name}</span>
          <span className="text-muted">{challenge.funded.profitSplitPercent == null ? "Funded" : `${challenge.funded.profitSplitPercent}% split`}</span>
        </span>
      </li>
    </ol>
  );
}

function Mark({ children, muted = false }: { children: React.ReactNode; muted?: boolean }) {
  return (
    <span className={`grid size-7 place-items-center rounded-full border text-xs font-semibold ${muted ? "border-border text-muted" : "border-accent/50 bg-accent/10 text-accent"}`}>
      {children}
    </span>
  );
}

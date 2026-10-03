"use client";

import { useState } from "react";

import {
  definitionOf,
  formOf,
  maxEvaluationStages,
  nextStage,
  type ChallengeForm,
  type DailyLossReference,
  type MaxLossKind,
  type StageForm,
} from "@/lib/challengeForm";
import { useChallenges, useChallengeTemplates, useSaveChallenge } from "@/lib/queries";

import { ChallengeSummary } from "./ChallengeSummary";
import { buttonClass, ErrorText, fieldClass, Panel, secondaryButtonClass } from "./ui";

type Editing = { form: ChallengeForm; isNew: boolean } | null;

/** The challenges the firm sells: new ones from a template, and changes to existing ones. */
export function AdminChallenges() {
  const challenges = useChallenges();
  const templates = useChallengeTemplates();
  const [editing, setEditing] = useState<Editing>(null);

  const startNew = () => {
    const template = templates.data?.[0];
    if (template) {
      setEditing({ form: { ...formOf(template.definition), id: "", name: "" }, isNew: true });
    }
  };

  return (
    <main className="mx-auto flex w-full max-w-6xl flex-col gap-6 p-6">
      {editing ? (
        <ChallengeEditor key={editing.form.id || "new"} initial={editing.form} isNew={editing.isNew} onDone={() => setEditing(null)} />
      ) : (
        <Panel
          title="Challenges"
          actions={
            <button type="button" onClick={startNew} disabled={!templates.data} className={buttonClass}>
              New challenge
            </button>
          }
        >
          <p className="text-sm text-muted">
            What the firm sells. A change applies to challenges started from now on. Accounts already started keep the rules they were bought with.
          </p>
          <ErrorText error={challenges.error ?? templates.error} />
          {challenges.data && challenges.data.length === 0 && <p className="text-sm text-muted">No challenges yet.</p>}
          <ul className="grid gap-3 sm:grid-cols-2">
            {(challenges.data ?? []).map((challenge) => (
              <li key={challenge.id} className="flex flex-col gap-3 rounded border border-border p-4">
                <ChallengeSummary challenge={challenge} />
                <button type="button" onClick={() => setEditing({ form: formOf(challenge), isNew: false })} className={`${secondaryButtonClass} self-start text-sm`}>
                  Change
                </button>
              </li>
            ))}
          </ul>
        </Panel>
      )}
    </main>
  );
}

function ChallengeEditor({ initial, isNew, onDone }: { initial: ChallengeForm; isNew: boolean; onDone: () => void }) {
  const save = useSaveChallenge();
  const [form, setForm] = useState(initial);
  const [problem, setProblem] = useState<string | null>(null);

  const set = (change: Partial<ChallengeForm>) => setForm((f) => ({ ...f, ...change }));
  const setStage = (index: number, change: Partial<StageForm>) =>
    setForm((f) => ({ ...f, evaluation: f.evaluation.map((s, i) => (i === index ? { ...s, ...change } : s)) }));

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    const result = definitionOf(form);
    setProblem("problem" in result ? result.problem : null);
    if ("definition" in result) {
      save.mutate(result.definition, { onSuccess: onDone });
    }
  };

  return (
    <Panel title={isNew ? "New challenge" : `Change ${initial.name}`}>
      <form onSubmit={submit} className="flex flex-col gap-5">
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <TextField label="Id, used by your systems" value={form.id} onChange={(id) => set({ id })} disabled={!isNew} mono placeholder="one-step-50k" />
          <TextField label="Name, shown to traders" value={form.name} onChange={(name) => set({ name })} placeholder="One-step 50k" />
          <TextField label={`Account size (${form.currency})`} value={form.initialBalance} onChange={(initialBalance) => set({ initialBalance })} numeric />
          <div className="grid grid-cols-2 gap-3">
            <TextField label="Day starts" value={form.dayStart} onChange={(dayStart) => set({ dayStart })} type="time" />
            <TextField label="Time zone" value={form.timeZone} onChange={(timeZone) => set({ timeZone })} />
          </div>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="text-left text-muted">
              <tr>
                <th className="py-2 pr-3 font-normal">Stage</th>
                <th className="py-2 pr-3 font-normal">Profit target %</th>
                <th className="py-2 pr-3 font-normal">Min trading days</th>
                <th className="py-2 pr-3 font-normal">Daily loss %</th>
                <th className="py-2 pr-3 font-normal">Daily loss from</th>
                <th className="py-2 pr-3 font-normal">Max loss %</th>
                <th className="py-2 pr-3 font-normal">Max loss</th>
                <th className="py-2 pr-3 font-normal">Profit split %</th>
                <th className="py-2 font-normal" />
              </tr>
            </thead>
            <tbody>
              {form.evaluation.map((stage, index) => (
                <StageRow
                  key={index}
                  stage={stage}
                  onChange={(change) => setStage(index, change)}
                  onRemove={form.evaluation.length > 1 ? () => set({ evaluation: form.evaluation.filter((_, i) => i !== index) }) : undefined}
                />
              ))}
              <StageRow stage={form.funded} funded onChange={(change) => set({ funded: { ...form.funded, ...change } })} />
            </tbody>
          </table>
        </div>

        <div className="flex flex-wrap items-center gap-3">
          <button
            type="button"
            onClick={() => set({ evaluation: [...form.evaluation, nextStage(form)] })}
            disabled={form.evaluation.length >= maxEvaluationStages}
            className={`${secondaryButtonClass} text-sm`}
          >
            Add stage
          </button>
          <span className="text-xs text-muted">
            Losses are in percent of the account size. The daily loss counts from the balance, or the higher of balance and equity, when the day starts.
          </span>
        </div>

        <ErrorText error={problem ? new Error(problem) : save.error} />

        <div className="flex gap-3">
          <button type="submit" disabled={save.isPending} className={buttonClass}>
            {save.isPending ? "Saving..." : "Save challenge"}
          </button>
          <button type="button" onClick={onDone} className={secondaryButtonClass}>
            Cancel
          </button>
        </div>
      </form>
    </Panel>
  );
}

function StageRow({
  stage,
  funded = false,
  onChange,
  onRemove,
}: {
  stage: StageForm;
  funded?: boolean;
  onChange: (change: Partial<StageForm>) => void;
  onRemove?: () => void;
}) {
  return (
    <tr className="border-t border-border align-top">
      <td className="py-2 pr-3">
        <input aria-label="Stage name" value={stage.name} onChange={(e) => onChange({ name: e.target.value })} className={`${fieldClass} w-28`} />
      </td>
      <td className="py-2 pr-3">
        {funded ? (
          <span className="text-muted">none</span>
        ) : (
          <NumberCell label={`${stage.name} profit target`} value={stage.profitTargetPercent} onChange={(profitTargetPercent) => onChange({ profitTargetPercent })} />
        )}
      </td>
      <td className="py-2 pr-3">
        <NumberCell label={`${stage.name} minimum trading days`} value={stage.minTradingDays} onChange={(minTradingDays) => onChange({ minTradingDays })} />
      </td>
      <td className="py-2 pr-3">
        <NumberCell label={`${stage.name} daily loss`} value={stage.dailyLossPercent} onChange={(dailyLossPercent) => onChange({ dailyLossPercent })} />
      </td>
      <td className="py-2 pr-3">
        <select
          aria-label={`${stage.name} daily loss from`}
          value={stage.dailyLossReference}
          onChange={(e) => onChange({ dailyLossReference: e.target.value as DailyLossReference })}
          className={fieldClass}
        >
          <option value="Balance">Balance</option>
          <option value="HigherOfBalanceAndEquity">Higher of balance and equity</option>
        </select>
      </td>
      <td className="py-2 pr-3">
        <NumberCell label={`${stage.name} max loss`} value={stage.maxLossPercent} onChange={(maxLossPercent) => onChange({ maxLossPercent })} />
      </td>
      <td className="py-2 pr-3">
        <select
          aria-label={`${stage.name} max loss kind`}
          value={stage.maxLossKind}
          onChange={(e) => onChange({ maxLossKind: e.target.value as MaxLossKind })}
          className={fieldClass}
        >
          <option value="Fixed">Fixed</option>
          <option value="Trailing">Trailing</option>
        </select>
      </td>
      <td className="py-2 pr-3">
        {funded ? (
          <NumberCell label="Profit split" value={stage.profitSplitPercent} onChange={(profitSplitPercent) => onChange({ profitSplitPercent })} />
        ) : (
          <span className="text-muted">-</span>
        )}
      </td>
      <td className="py-2">
        {onRemove && (
          <button type="button" onClick={onRemove} className="text-muted hover:text-loss">
            Remove
          </button>
        )}
      </td>
    </tr>
  );
}

function NumberCell({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) {
  return <input aria-label={label} inputMode="decimal" value={value} onChange={(e) => onChange(e.target.value)} className={`${fieldClass} w-20 text-right`} />;
}

function TextField({
  label,
  value,
  onChange,
  disabled = false,
  mono = false,
  numeric = false,
  type = "text",
  placeholder,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  mono?: boolean;
  numeric?: boolean;
  type?: string;
  placeholder?: string;
}) {
  return (
    <label className="flex flex-col gap-1 text-sm">
      <span className="text-muted">{label}</span>
      <input
        type={type}
        required
        value={value}
        disabled={disabled}
        placeholder={placeholder}
        inputMode={numeric ? "decimal" : undefined}
        onChange={(e) => onChange(e.target.value)}
        className={`${fieldClass} ${mono ? "font-mono" : ""} disabled:opacity-60`}
      />
    </label>
  );
}

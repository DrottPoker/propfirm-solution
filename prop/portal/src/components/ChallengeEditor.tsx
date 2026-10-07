"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";

import type { ChallengePrice } from "@/lib/api/types";
import {
  amountOf,
  definitionOf,
  formOf,
  maxEvaluationStages,
  nextStage,
  type ChallengeForm,
  type DailyLossReference,
  type MaxLossKind,
  type StageForm,
} from "@/lib/challengeForm";
import { formatMoney } from "@/lib/format";
import { challengesFromTemplate } from "@/lib/newChallenges";
import { priceCurrencies } from "@/lib/orders";
import { useChallengeFigures, useChallengeTemplates, useChallenges, usePrices, useSaveChallenge, useSavePrice } from "@/lib/queries";

import { InfoIcon, PlusIcon, TrashIcon } from "./icons";
import { AdminPage, buttonClass, ErrorText, fieldClass, Loading, Message, Panel, secondaryButtonClass } from "./ui";

type PriceForm = { amount: string; currency: string; forSale: boolean };

/**
 * A challenge to change, a copy of one, or a new one from the template. The rules are written as percentages of the
 * account size, with the amounts beside them, and the price in the portal's shop is set in the same place.
 */
export function ChallengeEditor({
  challengeId,
  copyOf,
  templateId = null,
  size = null,
}: {
  challengeId: string | null;
  copyOf: string | null;
  templateId?: string | null;
  size?: number | null;
}) {
  const challenges = useChallenges();
  const templates = useChallengeTemplates();
  const prices = usePrices();
  const error = challenges.error ?? templates.error ?? prices.error;
  if (error) {
    return <Message text={error.message} />;
  }

  if (!challenges.data || !templates.data || !prices.data) {
    return <Loading />;
  }

  const existing = challengeId ? challenges.data.find((c) => c.id === challengeId) : undefined;
  if (challengeId && !existing) {
    return <Message text="The firm has no such challenge." />;
  }

  const original = copyOf ? challenges.data.find((c) => c.id === copyOf) : undefined;
  const template = templates.data.find((t) => t.id === templateId) ?? templates.data[0];
  const fromTemplate = template && size ? challengesFromTemplate(template, [size], template.definition.currency)[0] : template?.definition;
  const source = existing ?? original ?? fromTemplate;
  if (!source) {
    return <Message text="There is no template to start a challenge from." />;
  }

  const form: ChallengeForm = existing
    ? formOf(existing)
    : original
      ? { ...formOf(original), id: `${original.id}-copy`, name: `${original.name} copy` }
      : { ...formOf(source), id: size ? source.id : "", name: size ? source.name : "" };
  const price = prices.data.find((p) => p.challengeId === (existing ?? original)?.id);
  return <EditorForm key={challengeId ?? copyOf ?? "new"} initial={form} initialPrice={priceFormOf(price, form.currency)} isNew={!existing} />;
}

function priceFormOf(price: ChallengePrice | undefined, currency: string): PriceForm {
  return {
    amount: price ? String(price.amount) : "",
    currency: price?.currency ?? (priceCurrencies.some((c) => c === currency) ? currency : "USD"),
    forSale: price?.forSale ?? false,
  };
}

function EditorForm({ initial, initialPrice, isNew }: { initial: ChallengeForm; initialPrice: PriceForm; isNew: boolean }) {
  const router = useRouter();
  const saveChallenge = useSaveChallenge();
  const savePrice = useSavePrice();
  const figures = useChallengeFigures();
  const [form, setForm] = useState(initial);
  const [price, setPrice] = useState(initialPrice);
  const [problem, setProblem] = useState<string | null>(null);

  const set = (change: Partial<ChallengeForm>) => setForm((f) => ({ ...f, ...change }));
  const setStage = (index: number, change: Partial<StageForm>) =>
    setForm((f) => ({ ...f, evaluation: f.evaluation.map((s, i) => (i === index ? { ...s, ...change } : s)) }));
  const dirty = JSON.stringify([form, price]) !== JSON.stringify([initial, initialPrice]);
  const trading = figures.data?.find((f) => f.challengeId === initial.id)?.trading;
  const busy = saveChallenge.isPending || savePrice.isPending;

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    const result = definitionOf(form);
    if ("problem" in result) {
      setProblem(result.problem);
      return;
    }

    const amount = Number(price.amount.trim().replace(",", "."));
    if (price.amount.trim() !== "" && !(Number.isFinite(amount) && amount > 0)) {
      setProblem("Write the price as a number, for example 99 or 89.50, or leave it empty for no price.");
      return;
    }

    setProblem(null);
    try {
      await saveChallenge.mutateAsync(result.definition);
      if (price.amount.trim() !== "" && (isNew || JSON.stringify(price) !== JSON.stringify(initialPrice))) {
        await savePrice.mutateAsync({ challengeId: result.definition.id, amount, currency: price.currency, forSale: price.forSale });
      }

      router.push("/admin/challenges");
    } catch {
      // The mutations show what went wrong.
    }
  };

  return (
    <AdminPage>
      <div className="flex flex-col gap-3">
        <nav aria-label="Breadcrumb" className="text-sm text-muted">
          <Link href="/admin/challenges" className="hover:text-foreground">
            Challenges
          </Link>{" "}
          <span aria-hidden="true">/</span> <span className="text-foreground">{isNew ? "New challenge" : initial.name}</span>
        </nav>
        <h1 className="font-serif text-[2.35rem] leading-[1.05] tracking-tight">{isNew ? (initial.name ? `New challenge from ${initial.name.replace(/ copy$/, "")}` : "New challenge") : `Change ${initial.name}`}</h1>
        {!isNew && (
          <p className="flex items-center gap-2 text-muted">
            <InfoIcon className="size-4 shrink-0" />
            {trading ? `The ${trading} ${trading === 1 ? "account trading it keeps" : "accounts trading it keep"}` : "Accounts keep"} the rules they were bought with. Your change
            applies to challenges started after you save.
          </p>
        )}
      </div>

      <form onSubmit={submit} className="grid grid-cols-1 items-start gap-6 xl:grid-cols-[minmax(0,1fr)_20rem]">
        <div className="flex min-w-0 flex-col gap-6">
          <Panel title="Basics">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
              <TextField label="Name traders see" value={form.name} onChange={(name) => set({ name })} placeholder="One-step 50K" />
              <TextField
                label="Id for your systems"
                value={form.id}
                onChange={(id) => set({ id })}
                disabled={!isNew}
                mono
                placeholder="one-step-50k"
                hint={isNew ? "Your website and the firm API use it. It cannot be changed later." : "Set when the challenge was made."}
              />
              <TextField label="Account size" value={form.initialBalance} onChange={(initialBalance) => set({ initialBalance })} numeric suffix={form.currency} />
              <TextField label="Trading day starts" value={form.dayStart} onChange={(dayStart) => set({ dayStart })} type="time" />
              <TextField label="Time zone of the trading day" value={form.timeZone} onChange={(timeZone) => set({ timeZone })} list="time-zones" />
              <TextField
                label="Ends after days without a new trade"
                value={form.inactivityDays}
                onChange={(inactivityDays) => set({ inactivityDays })}
                numeric
                optional
                suffix="days"
                hint="Empty for no limit."
              />
            </div>
            <datalist id="time-zones">
              {["UTC", "Europe/Stockholm", "Europe/London", "America/New_York", "Asia/Dubai", "Asia/Singapore"].map((zone) => (
                <option key={zone} value={zone} />
              ))}
            </datalist>
          </Panel>

          <Panel
            title="Phases"
            actions={<span className="text-xs text-muted">Up to three phases before funded, or none to fund traders from the start. Losses are in percent of the account size.</span>}
          >
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2 2xl:grid-cols-3">
              {form.evaluation.map((stage, index) => (
                <StageCard
                  key={index}
                  stage={stage}
                  size={form.initialBalance}
                  currency={form.currency}
                  onChange={(change) => setStage(index, change)}
                  onRemove={() => set({ evaluation: form.evaluation.filter((_, i) => i !== index) })}
                />
              ))}
              <StageCard stage={form.funded} size={form.initialBalance} currency={form.currency} funded onChange={(change) => set({ funded: { ...form.funded, ...change } })} />
            </div>
            <button
              type="button"
              onClick={() => set({ evaluation: [...form.evaluation, nextStage(form)] })}
              disabled={form.evaluation.length >= maxEvaluationStages}
              className="flex items-center justify-center gap-2 rounded-lg border border-dashed border-border py-2.5 text-sm font-medium text-muted hover:text-foreground disabled:opacity-50"
            >
              <PlusIcon />
              {form.evaluation.length >= maxEvaluationStages ? `At most ${maxEvaluationStages} phases before funded` : "Add a phase before funded"}
            </button>
          </Panel>

          <Panel title="Price in your shop">
            <div className="flex flex-wrap items-end gap-4">
              <label className="flex flex-col gap-1.5 text-sm">
                <span className="font-medium">Price</span>
                <span className="flex gap-2">
                  <input
                    aria-label="Price"
                    inputMode="decimal"
                    value={price.amount}
                    onChange={(e) => setPrice({ ...price, amount: e.target.value, forSale: e.target.value.trim() === "" ? false : price.forSale })}
                    placeholder="No price"
                    className={`${fieldClass} w-32 text-right`}
                  />
                  <select aria-label="Price currency" value={price.currency} onChange={(e) => setPrice({ ...price, currency: e.target.value })} className={fieldClass}>
                    {priceCurrencies.map((c) => (
                      <option key={c} value={c}>
                        {c}
                      </option>
                    ))}
                  </select>
                </span>
              </label>
              <label className={`flex items-center gap-2.5 py-2 text-sm ${price.amount.trim() ? "" : "text-muted"}`} title={price.amount.trim() ? undefined : "Set a price first."}>
                <input
                  type="checkbox"
                  checked={price.forSale}
                  disabled={!price.amount.trim()}
                  onChange={(e) => setPrice({ ...price, forSale: e.target.checked })}
                  className="size-4 accent-accent"
                />
                For sale in your shop
              </label>
            </div>
            <p className="text-xs text-muted">
              Buyers pay in this currency, which can differ from the account&apos;s. A new challenge is not for sale until you say so. Without a price it is not sold,
              but you can still start it for a trader.
            </p>
          </Panel>

          <div className="flex flex-wrap items-center gap-3 rounded-lg border border-border bg-panel px-4 py-3.5">
            <span className="flex-[1_1_14rem] text-sm text-muted">{dirty ? "You have changes that are not saved." : "No changes yet."}</span>
            <Link href="/admin/challenges" className={secondaryButtonClass}>
              Cancel
            </Link>
            <button type="submit" disabled={busy} className={buttonClass}>
              {busy ? "Saving..." : "Save challenge"}
            </button>
          </div>
          <ErrorText error={problem ? new Error(problem) : (saveChallenge.error ?? savePrice.error)} />
        </div>

        <Preview form={form} price={price} />
      </form>
    </AdminPage>
  );
}

function StageCard({
  stage,
  size,
  currency,
  funded = false,
  onChange,
  onRemove,
}: {
  stage: StageForm;
  size: string;
  currency: string;
  funded?: boolean;
  onChange: (change: Partial<StageForm>) => void;
  onRemove?: () => void;
}) {
  const label = stage.name || (funded ? "Funded" : "Stage");

  // The amount beside a percentage, or an empty line that keeps the fields in place while it is not a number.
  const amount = (percent: string) => {
    const value = amountOf(size, percent);
    return value === null ? "" : `= ${formatMoney(value)} ${currency}`;
  };

  return (
    <fieldset className={`flex min-w-0 flex-col gap-3.5 rounded-lg border p-4 ${funded ? "border-profit/35" : "border-border"}`}>
      <legend className={`px-1.5 text-xs font-semibold uppercase tracking-[0.14em] ${funded ? "text-profit" : "text-muted"}`}>{funded ? "Funded" : "Phase before funded"}</legend>
      <TextField
        label="Name"
        value={stage.name}
        onChange={(name) => onChange({ name })}
        after={
          onRemove && (
            <button type="button" aria-label={`Remove ${label}`} onClick={onRemove} className="grid size-10 shrink-0 place-items-center rounded border border-border text-muted hover:border-loss hover:text-loss">
              <TrashIcon />
            </button>
          )
        }
      />
      {funded ? (
        <TextField label="Profit split to the trader" value={stage.profitSplitPercent} onChange={(profitSplitPercent) => onChange({ profitSplitPercent })} numeric suffix="%" />
      ) : (
        <TextField
          label="Profit target"
          value={stage.profitTargetPercent}
          onChange={(profitTargetPercent) => onChange({ profitTargetPercent })}
          numeric
          suffix="%"
          hint={amount(stage.profitTargetPercent)}
        />
      )}
      <TextField
        label="Daily loss limit"
        value={stage.dailyLossPercent}
        onChange={(dailyLossPercent) => onChange({ dailyLossPercent })}
        numeric
        suffix="%"
        hint={amount(stage.dailyLossPercent)}
      />
      <label className="flex flex-col gap-1.5 text-sm">
        <span className="text-muted">Daily loss counted from, when the day starts</span>
        <select value={stage.dailyLossReference} onChange={(e) => onChange({ dailyLossReference: e.target.value as DailyLossReference })} className={fieldClass}>
          <option value="Balance">The balance</option>
          <option value="HigherOfBalanceAndEquity">Balance or equity, the higher</option>
        </select>
      </label>
      <TextField
        label="Max loss limit"
        value={stage.maxLossPercent}
        onChange={(maxLossPercent) => onChange({ maxLossPercent })}
        numeric
        suffix="%"
        hint={amount(stage.maxLossPercent)}
        after={
          <select aria-label={`${label} max loss kind`} value={stage.maxLossKind} onChange={(e) => onChange({ maxLossKind: e.target.value as MaxLossKind })} className={fieldClass}>
            <option value="Fixed">Fixed</option>
            <option value="Trailing">Trailing</option>
          </select>
        }
      />
      <div className="grid grid-cols-2 gap-2.5">
        <TextField
          label={funded ? "Minimum trading days between payouts" : "Minimum trading days"}
          value={stage.minTradingDays}
          onChange={(minTradingDays) => onChange({ minTradingDays })}
          numeric
        />
        {!funded && (
          <TextField label="Time limit (days)" value={stage.maxDays} onChange={(maxDays) => onChange({ maxDays })} numeric optional placeholder="No limit" />
        )}
        {funded && (
          <TextField
            label="Best day at most"
            value={stage.consistencyPercent}
            onChange={(consistencyPercent) => onChange({ consistencyPercent })}
            numeric
            optional
            suffix="%"
            placeholder="No rule"
            hint="of a payout's profit, so one lucky day is not paid out"
          />
        )}
      </div>
    </fieldset>
  );
}

/** The challenge as traders see it in the shop and on their account, from what is written now. */
function Preview({ form, price }: { form: ChallengeForm; price: PriceForm }) {
  const stages = [...form.evaluation.map((s) => ({ stage: s, funded: false })), { stage: form.funded, funded: true }];
  const value = (percent: string) => {
    const amount = amountOf(form.initialBalance, percent);
    return amount === null ? "-" : formatMoney(amount);
  };
  const size = amountOf(form.initialBalance, "100");

  return (
    <aside aria-labelledby="preview" className="flex flex-col gap-4 rounded-xl border border-border bg-panel p-5 shadow-card xl:sticky xl:top-6">
      <h2 id="preview" className="font-semibold">
        What traders see
      </h2>
      <div className="flex flex-col gap-1 rounded-lg border border-border bg-background p-3.5">
        <span className="text-xs text-muted">In your shop</span>
        <span className="flex items-baseline justify-between gap-2">
          <strong className="font-semibold">{form.name || "Unnamed challenge"}</strong>
          <span className="text-sm">{priceText(price)}</span>
        </span>
        <span className="text-xs text-muted">
          {size === null ? "-" : formatMoney(size)} {form.currency} account,{" "}
          {form.evaluation.length === 0 ? "funded from the start" : form.evaluation.length === 1 ? "1 phase, then funded" : `${form.evaluation.length} phases, then funded`}, with{" "}
          {form.funded.profitSplitPercent || "-"}% of the profit
        </span>
      </div>
      <table className="w-full text-xs">
        <thead className="text-left text-muted">
          <tr>
            <td className="py-1.5" />
            {stages.map(({ stage }, i) => (
              <th key={i} scope="col" className="py-1.5 pl-2 font-normal">
                {stage.name || "-"}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          <Row label="Target" cells={stages.map(({ stage, funded }) => (funded ? "None" : value(stage.profitTargetPercent)))} />
          <Row label="Daily loss" cells={stages.map(({ stage }) => value(stage.dailyLossPercent))} />
          <Row label="Max loss" cells={stages.map(({ stage }) => value(stage.maxLossPercent))} />
          <Row
            label="Trading days"
            cells={stages.map(({ stage, funded }) => (funded ? `${stage.minTradingDays || 0} between payouts` : `${stage.minTradingDays || 0} at least`))}
          />
          <Row label="Time limit" cells={stages.map(({ stage, funded }) => (funded || !stage.maxDays.trim() ? "None" : `${stage.maxDays} days`))} />
          <Row
            label="Best day"
            cells={stages.map(({ stage, funded }) => (funded && stage.consistencyPercent.trim() ? `${stage.consistencyPercent}% of the profit at most` : "-"))}
          />
        </tbody>
      </table>
      <p className="text-xs text-muted">
        In {form.currency}. The day starts at {form.dayStart || "00:00"} {form.timeZone}.
        {form.inactivityDays.trim() ? ` The challenge ends after ${form.inactivityDays.trim()} days with no new trade.` : ""}
      </p>
    </aside>
  );
}

/** The price as everywhere else, for example "499.00 USD", or what is typed while it is not a number. */
function priceText(price: PriceForm): string {
  const typed = price.amount.trim();
  if (typed === "") {
    return "No price";
  }

  const amount = Number(typed.replace(",", "."));
  return `${Number.isFinite(amount) ? formatMoney(amount) : typed} ${price.currency}`;
}

function Row({ label, cells }: { label: string; cells: string[] }) {
  return (
    <tr className="border-t border-border">
      <th scope="row" className="py-1.5 text-left font-sans font-normal text-muted">
        {label}
      </th>
      {cells.map((cell, i) => (
        <td key={i} className="py-1.5 pl-2">
          {cell}
        </td>
      ))}
    </tr>
  );
}

function TextField({
  label,
  value,
  onChange,
  disabled = false,
  mono = false,
  numeric = false,
  optional = false,
  type = "text",
  placeholder,
  suffix,
  hint,
  list,
  after,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  mono?: boolean;
  numeric?: boolean;
  optional?: boolean;
  type?: string;
  placeholder?: string;
  suffix?: string;
  /** A line under the field. An empty one keeps its place, so fields beside each other stay in line. */
  hint?: string;
  list?: string;
  /** A control beside the field, on the same line, such as a choice that goes with it. */
  after?: React.ReactNode;
}) {
  const input = (
    <input
      aria-label={label}
      type={type}
      required={!optional}
      value={value}
      disabled={disabled}
      placeholder={placeholder}
      list={list}
      inputMode={numeric ? "decimal" : undefined}
      onChange={(e) => onChange(e.target.value)}
      className={
        suffix
          ? `min-w-0 flex-1 bg-transparent px-3 py-2 outline-none ${numeric ? "text-right" : ""}`
          : `${fieldClass} w-full ${mono ? "font-mono" : ""} ${numeric ? "text-right" : ""} disabled:opacity-60`
      }
    />
  );
  const field = suffix ? (
    <span className="flex min-w-0 flex-1 items-center rounded-lg border border-border bg-background/60 transition-[border-color,box-shadow] focus-within:border-accent focus-within:ring-3 focus-within:ring-accent/20">
      {input}
      {/* An empty field shows its placeholder alone, such as "No rule", without the unit after it. */}
      {(value !== "" || !placeholder) && <span className="pr-3 text-muted">{suffix}</span>}
    </span>
  ) : (
    input
  );
  return (
    <div className="flex min-w-0 flex-1 flex-col gap-1.5 text-sm">
      <div className="flex min-w-0 items-end gap-2">
        <label className="flex min-w-0 flex-1 flex-col gap-1.5">
          <span className="text-muted">{label}</span>
          {field}
        </label>
        {after}
      </div>
      {hint !== undefined && <span className="min-h-4 text-xs text-muted">{hint}</span>}
    </div>
  );
}

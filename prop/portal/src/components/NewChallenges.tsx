"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";

import type { ChallengeTemplate } from "@/lib/api/types";
import { formatMoney } from "@/lib/format";
import { challengeSizes, challengesFromTemplate, sizeName } from "@/lib/newChallenges";
import { priceCurrencies } from "@/lib/orders";
import { useChallengeTemplates, useChallenges, useFirmSettings, useSaveChallenge, useSavePrice } from "@/lib/queries";

import { AdminPage, buttonClass, ErrorText, fieldClass, Message, PageHeader, Panel, secondaryButtonClass } from "./ui";

/**
 * New challenges from a template: one for each account size the firm sells, each with its price. The rules can be
 * changed afterwards, or first for a single size in the editor.
 */
export function NewChallenges() {
  const settings = useFirmSettings();
  const templates = useChallengeTemplates();
  const challenges = useChallenges(settings.data !== undefined && settings.data.status !== "Provisioning");
  const error = settings.error ?? templates.error ?? challenges.error;

  if (error) {
    return <Message text={error.message} />;
  }

  if (!settings.data || !templates.data || !challenges.data) {
    return <Message text="Loading..." />;
  }

  return (
    <Form
      templates={templates.data}
      existing={new Set(challenges.data.map((c) => c.id))}
      currency={settings.data.currency ?? "USD"}
    />
  );
}

function Form({ templates, existing, currency }: { templates: ChallengeTemplate[]; existing: Set<string>; currency: string }) {
  const router = useRouter();
  const saveChallenge = useSaveChallenge();
  const savePrice = useSavePrice();
  const [templateId, setTemplateId] = useState(templates.find((t) => t.id === "two-step")?.id ?? templates[0]?.id ?? "");
  const [sizes, setSizes] = useState<number[]>([100_000]);
  const [prices, setPrices] = useState<Record<number, string>>({});
  const [priceCurrency, setPriceCurrency] = useState(priceCurrencies.some((c) => c === currency) ? currency : "USD");
  const [forSale, setForSale] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const template = templates.find((t) => t.id === templateId);
  if (!template) {
    return <Message text="There is no template to start a challenge from." />;
  }

  const made = challengesFromTemplate(template, challengeSizes, currency);
  const idOf = (size: number) => made[challengeSizes.indexOf(size as (typeof challengeSizes)[number])].id;
  const chosen = sizes.filter((size) => !existing.has(idOf(size)));

  const toggle = (size: number) => setSizes(sizes.includes(size) ? sizes.filter((s) => s !== size) : [...sizes, size].sort((a, b) => a - b));

  const create = async () => {
    const amounts = chosen.map((size) => ({ size, text: (prices[size] ?? "").trim() }));
    const wrong = amounts.find((a) => a.text !== "" && !(Number(a.text.replace(",", ".")) > 0));
    if (wrong) {
      setProblem(`Write the price of the ${sizeName(wrong.size)} challenge as a number, for example 99 or 89.50, or leave it empty.`);
      return;
    }

    setProblem(null);
    setBusy(true);
    try {
      for (const definition of challengesFromTemplate(template, chosen, currency)) {
        await saveChallenge.mutateAsync(definition);
        const price = amounts.find((a) => a.size === definition.initialBalance)?.text ?? "";
        if (price !== "") {
          await savePrice.mutateAsync({ challengeId: definition.id, amount: Number(price.replace(",", ".")), currency: priceCurrency, forSale });
        }
      }

      router.push("/admin/challenges");
    } catch {
      // The mutations show what went wrong.
      setBusy(false);
    }
  };

  const phases = template.definition.evaluation.length;
  return (
    <AdminPage>
      <PageHeader
        title="New challenges"
        description="Start from a template and choose the account sizes you sell. The rules are percentages, so they fit every size, and you can change them afterwards."
        back={
          <Link href="/admin/challenges" className="text-sm text-muted hover:text-foreground">
            Challenges
          </Link>
        }
      />

      <Panel title="1. Choose a template">
        <div role="radiogroup" aria-label="Template" className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
          {templates.map((t) => {
            const count = t.definition.evaluation.length;
            const selected = t.id === templateId;
            return (
              <button
                key={t.id}
                type="button"
                role="radio"
                aria-checked={selected}
                onClick={() => setTemplateId(t.id)}
                className={`flex flex-col gap-1.5 rounded-lg border p-4 text-left text-sm ${selected ? "border-accent bg-accent/5" : "border-border hover:border-muted"}`}
              >
                <span className="font-medium">{t.name}</span>
                <span className="text-xs text-muted">{count === 0 ? "Funded from the start" : `${count} ${count === 1 ? "phase" : "phases"}, then funded`}</span>
                <span className="text-xs text-muted">{t.description}</span>
              </button>
            );
          })}
        </div>
      </Panel>

      <Panel title="2. Choose the account sizes and their prices">
        <ul className="flex flex-col divide-y divide-border text-sm">
          {challengeSizes.map((size) => {
            const id = idOf(size);
            const taken = existing.has(id);
            const checked = sizes.includes(size) && !taken;
            return (
              <li key={size} className="flex flex-wrap items-center gap-3 py-2.5">
                <label className={`flex min-w-48 flex-1 items-center gap-2.5 ${taken ? "text-muted" : ""}`}>
                  <input type="checkbox" checked={checked} disabled={taken} onChange={() => toggle(size)} className="size-4 accent-accent" />
                  <span>
                    {template.name} {sizeName(size)}
                    <span className="block text-xs text-muted">
                      {formatMoney(size)} {currency} account{taken ? " · you have it already" : ""}
                    </span>
                  </span>
                </label>
                <input
                  aria-label={`Price of ${template.name} ${sizeName(size)}`}
                  inputMode="decimal"
                  disabled={!checked}
                  value={prices[size] ?? ""}
                  onChange={(e) => setPrices({ ...prices, [size]: e.target.value })}
                  placeholder="No price"
                  className={`${fieldClass} w-28 py-1.5 text-right font-mono disabled:opacity-50`}
                />
              </li>
            );
          })}
        </ul>
        <div className="flex flex-wrap items-center gap-4 text-sm">
          <label className="flex items-center gap-2">
            <span className="text-muted">Prices in</span>
            <select value={priceCurrency} onChange={(e) => setPriceCurrency(e.target.value)} className={`${fieldClass} py-1.5`}>
              {priceCurrencies.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
          </label>
          <label className="flex items-center gap-2">
            <input type="checkbox" checked={forSale} onChange={(e) => setForSale(e.target.checked)} className="size-4 accent-accent" />
            Put those with a price up for sale at once
          </label>
        </div>
        <p className="text-xs text-muted">
          {phases === 0
            ? "Traders are funded from the start, without an evaluation."
            : "A challenge without a price is not sold, but you can still start it for a trader. Set prices later on the challenges' cards."}
        </p>
      </Panel>

      <div className="flex flex-wrap items-center gap-3 rounded-lg border border-border bg-panel px-4 py-3.5">
        <span className="flex-[1_1_14rem] text-sm text-muted">
          {chosen.length === 0 ? "Choose at least one size." : `${chosen.length} ${chosen.length === 1 ? "challenge" : "challenges"} in ${currency}.`}
        </span>
        {chosen.length === 1 && (
          <Link href={`/admin/challenges/edit?template=${encodeURIComponent(template.id)}&size=${chosen[0]}`} className={secondaryButtonClass}>
            Change the rules first
          </Link>
        )}
        <button type="button" disabled={chosen.length === 0 || busy} onClick={create} className={buttonClass}>
          {busy ? "Creating..." : chosen.length > 1 ? `Create ${chosen.length} challenges` : "Create challenge"}
        </button>
      </div>
      <ErrorText error={problem ? new Error(problem) : (saveChallenge.error ?? savePrice.error)} />
    </AdminPage>
  );
}

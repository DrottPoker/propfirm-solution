"use client";

import { useState } from "react";

import type { StartingSizeKind, TerminalSettings } from "@/lib/api/types";
import { useFirmSettings, useSaveTerminalSettings, useTerminalSettings } from "@/lib/queries";
import { useSavedNote } from "@/lib/useSavedNote";

import { AdminPage, buttonClass, ErrorText, fieldClass, Loading, Message, PageHeader, Panel, Switch } from "./ui";

/**
 * How orders start in the firm's terminal (ADR 0058): whether each order asks before it is sent, and the size a new
 * ticket starts with. Terminals take a change the next time they open, and a trader's own choice comes first.
 */
export function AdminTerminal() {
  const settings = useFirmSettings();
  const ready = settings.data !== undefined && settings.data.status !== "Provisioning";
  const terminal = useTerminalSettings(ready);

  if (settings.data?.status === "Provisioning") {
    return <Message text="Your trading server is being set up. This takes a few seconds." />;
  }

  if (settings.isError || terminal.isError) {
    return <Message text={(settings.error ?? terminal.error)?.message ?? "The terminal settings cannot be loaded."} />;
  }

  if (!terminal.data) {
    return <Loading />;
  }

  return (
    <AdminPage narrow>
      <PageHeader
        title="Terminal"
        description="How orders start in your traders' terminal. A change applies the next time a trader opens the terminal."
      />
      <TerminalForm settings={terminal.data} />
    </AdminPage>
  );
}

const sizeChoices: { kind: StartingSizeKind; label: string; hint: string }[] = [
  {
    kind: "Smallest",
    label: "The smallest each instrument allows",
    hint: "For example 0.01 lots. The safest start, since one lot of gold is worth hundreds of thousands of dollars.",
  },
  { kind: "Lots", label: "A number of lots", hint: "Lowered or raised to what each instrument allows." },
  {
    kind: "RiskOfRoom",
    label: "Sized from today's loss limit",
    hint: "The size that loses this share of what is left of today's loss limit at the order's stop loss. The trader sets the stop loss first.",
  },
];

function TerminalForm({ settings }: { settings: TerminalSettings }) {
  const save = useSaveTerminalSettings();
  useSavedNote(save, "Saved. Terminals start this way the next time they open.");
  const [confirmOrders, setConfirmOrders] = useState(settings.confirmOrders);
  const [size, setSize] = useState<StartingSizeKind>(settings.startingSize);
  // Each choice keeps its own value, so switching between them loses nothing.
  const [lots, setLots] = useState(settings.startingSize === "Lots" ? String(settings.startingValue ?? "") : "0.10");
  const [percent, setPercent] = useState(settings.startingSize === "RiskOfRoom" ? String(settings.startingValue ?? "") : "1");
  const [problem, setProblem] = useState<string | null>(null);

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    const text = size === "Lots" ? lots : size === "RiskOfRoom" ? percent : null;
    const value = text === null ? null : Number(text.trim().replace(",", "."));
    if (value !== null && (text?.trim() === "" || !Number.isFinite(value))) {
      setProblem(size === "Lots" ? "Write the size in lots, for example 0.10." : "Write the share as a percent, for example 1.");
      return;
    }

    setProblem(null);
    save.mutate({ confirmOrders, startingSize: size, startingValue: value });
  };

  return (
    <form onSubmit={submit} className="flex flex-col gap-6">
      <Panel title="Asking before an order">
        <div className="flex items-start justify-between gap-6 text-sm">
          <span className="flex flex-col gap-0.5">
            <span className="font-medium">Ask before every order</span>
            <span className="text-muted">
              The terminal shows each order and its stops, and sends it when the trader confirms. Off, an order goes at the first click, as most
              prop traders expect. Each trader can still choose for themselves in the terminal&apos;s settings.
            </span>
          </span>
          <Switch
            checked={confirmOrders}
            onChange={(checked) => {
              save.reset();
              setConfirmOrders(checked);
            }}
            label="Ask before every order"
          />
        </div>
      </Panel>

      <Panel title="A new order's size">
        <p className="text-sm text-muted">Where the size starts on an instrument the trader has not traded. After that, it starts with their last size.</p>
        <fieldset className="flex flex-col gap-3 text-sm">
          <legend className="sr-only">Where a new order&apos;s size starts</legend>
          {sizeChoices.map((choice) => (
            <div key={choice.kind} className="flex flex-col gap-2">
              <label className="flex items-start gap-2">
                <input
                  type="radio"
                  name="startingSize"
                  checked={size === choice.kind}
                  onChange={() => {
                    save.reset();
                    setSize(choice.kind);
                  }}
                  className="mt-1 accent-[var(--accent)]"
                />
                <span>
                  {choice.label}
                  <span className="block text-xs text-muted">{choice.hint}</span>
                </span>
              </label>
              {size === choice.kind && choice.kind !== "Smallest" && (
                <label className="ml-6 flex w-48 flex-col gap-1">
                  <span className="text-muted">{choice.kind === "Lots" ? "Lots" : "Percent of what is left today"}</span>
                  <input
                    inputMode="decimal"
                    value={choice.kind === "Lots" ? lots : percent}
                    onChange={(e) => (choice.kind === "Lots" ? setLots : setPercent)(e.target.value)}
                    className={fieldClass}
                  />
                </label>
              )}
            </div>
          ))}
        </fieldset>
      </Panel>

      <div className="flex flex-col items-start gap-2">
        <button type="submit" disabled={save.isPending} className={buttonClass}>
          {save.isPending ? "Saving..." : "Save"}
        </button>
        {problem && <p className="text-sm text-loss">{problem}</p>}
        <ErrorText error={save.error} />
      </div>
    </form>
  );
}

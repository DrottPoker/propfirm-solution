"use client";

import type { TerminalKind } from "@/lib/api/types";
import { kindDescriptions, kindNames, kinds } from "@/lib/kinds";

/** The type of business to choose for a server, each with what it means for the firm's traders (ADR 0058). */
export function KindChoice({ value, onChange }: { value: TerminalKind | null; onChange: (kind: TerminalKind) => void }) {
  return (
    <fieldset className="flex flex-col gap-2">
      <legend className="mb-1.5 font-medium">Type of business</legend>
      {kinds.map((kind) => (
        <label
          key={kind}
          className={`flex cursor-pointer items-start gap-3 rounded-xl border px-3.5 py-2.5 transition-colors duration-150 ${value === kind ? "border-accent bg-accent/10" : "border-border hover:border-muted/60"}`}
        >
          <input type="radio" name="kind" checked={value === kind} onChange={() => onChange(kind)} className="mt-1 accent-accent" />
          <span className="flex flex-col gap-0.5">
            <span className="font-medium">{kindNames[kind]}</span>
            <span className="text-sm leading-relaxed text-muted">{kindDescriptions[kind]}</span>
          </span>
        </label>
      ))}
    </fieldset>
  );
}

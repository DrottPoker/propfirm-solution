"use client";

import { LayoutGroup, motion } from "motion/react";
import { useId } from "react";

// Choices between a few options, whose marker slides to the one chosen. Client components, since the marker moves;
// ui.tsx exports them with the other building blocks.

const slide = { type: "spring", stiffness: 520, damping: 40, mass: 0.8 } as const;

/** Filters shown as a row of buttons, each with how many it finds, for example the groups of accounts. */
export function FilterTabs<T extends string>({
  label,
  options,
  value,
  onChange,
}: {
  label: string;
  options: { value: T; label: string; count?: number; highlight?: boolean }[];
  value: T;
  onChange: (value: T) => void;
}) {
  const group = useId();
  return (
    <LayoutGroup id={group}>
      <div role="group" aria-label={label} className="flex flex-wrap gap-1 self-start rounded-xl border border-border bg-panel p-1 text-sm shadow-card">
        {options.map((option) => {
          const chosen = option.value === value;
          return (
            <button
              key={option.value}
              type="button"
              aria-pressed={chosen}
              onClick={() => onChange(option.value)}
              className={`relative flex items-center gap-2 rounded-lg px-3 py-1.5 transition-colors ${chosen ? "font-medium text-foreground" : "text-muted hover:text-foreground"}`}
            >
              {chosen && <motion.span layoutId="marker" transition={slide} className="absolute inset-0 rounded-lg bg-raised shadow-card" />}
              <span className="relative">{option.label}</span>
              {option.count !== undefined && (
                <span className={`relative text-xs tabular-nums ${option.highlight && option.count > 0 ? "font-medium text-warning" : chosen ? "text-foreground/70" : "text-muted"}`}>
                  {option.count}
                </span>
              )}
            </button>
          );
        })}
      </div>
    </LayoutGroup>
  );
}

/** Tabs over the parts of a page. The chosen tab's part is shown below them. */
export function Tabs<T extends string>({
  label,
  tabs,
  value,
  onChange,
}: {
  label: string;
  tabs: { value: T; label: string; count?: number }[];
  value: T;
  onChange: (value: T) => void;
}) {
  const group = useId();
  return (
    <LayoutGroup id={group}>
      <div role="tablist" aria-label={label} className="flex gap-1 overflow-x-auto border-b border-border">
        {tabs.map((tab) => {
          const chosen = tab.value === value;
          return (
            <button
              key={tab.value}
              type="button"
              role="tab"
              id={`tab-${tab.value}`}
              aria-selected={chosen}
              aria-controls={`panel-${tab.value}`}
              onClick={() => onChange(tab.value)}
              className={`relative whitespace-nowrap px-3.5 py-2.5 text-sm transition-colors ${chosen ? "font-medium text-foreground" : "text-muted hover:text-foreground"}`}
            >
              {tab.label}
              {tab.count !== undefined && <span className="ml-1.5 text-xs text-muted">{tab.count}</span>}
              {chosen && <motion.span layoutId="underline" transition={slide} className="absolute inset-x-2 -bottom-px h-0.5 rounded-full bg-accent" />}
            </button>
          );
        })}
      </div>
    </LayoutGroup>
  );
}

/** One of a few choices, shown side by side, for example the stage a chart shows. */
export function SegmentedControl<T extends string | number>({
  label,
  options,
  value,
  onChange,
}: {
  label: string;
  options: { value: T; label: string }[];
  value: T;
  onChange: (value: T) => void;
}) {
  const group = useId();
  return (
    <LayoutGroup id={group}>
      <div role="group" aria-label={label} className="flex flex-wrap gap-0.5 rounded-lg border border-border bg-background/40 p-0.5 text-xs">
        {options.map((option) => {
          const chosen = option.value === value;
          return (
            <button
              key={option.value}
              type="button"
              aria-pressed={chosen}
              onClick={() => onChange(option.value)}
              className={`relative rounded-md px-2.5 py-1 transition-colors ${chosen ? "font-medium text-foreground" : "text-muted hover:text-foreground"}`}
            >
              {chosen && <motion.span layoutId="segment" transition={slide} className="absolute inset-0 rounded-md bg-raised shadow-card" />}
              <span className="relative">{option.label}</span>
            </button>
          );
        })}
      </div>
    </LayoutGroup>
  );
}

/** An on or off setting, saved when it is switched, with its label beside it. */
export function Switch({
  checked,
  onChange,
  label,
  disabled = false,
}: {
  checked: boolean;
  onChange: (checked: boolean) => void;
  label: string;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={`relative inline-flex h-6 w-11 shrink-0 items-center rounded-full border transition-colors duration-200 disabled:opacity-50 ${
        checked ? "border-accent bg-accent" : "border-border bg-raised"
      }`}
    >
      <motion.span
        layout
        transition={slide}
        className={`absolute size-4.5 rounded-full shadow-card ${checked ? "right-0.5 bg-accent-foreground" : "left-0.5 bg-muted"}`}
      />
    </button>
  );
}

"use client";

import { useRef } from "react";

import { useSheet } from "@/lib/sheet";

import { CloseIcon, PrintIcon } from "./icons";
import { useDismiss } from "./useDismiss";

/**
 * A panel along the right side of the terminal, over everything else, or the whole screen on a phone. Closes on Esc, a
 * press outside it or its cross. Printing a printable one shows only the panel, so it can be saved as a PDF.
 */
export function Sheet({
  label,
  title,
  subtitle,
  printable = true,
  children,
}: {
  label: string;
  title: string;
  subtitle: string;
  printable?: boolean;
  children: React.ReactNode;
}) {
  const ref = useRef<HTMLElement>(null);
  const close = useSheet((s) => s.close);
  useDismiss(true, ref, close);

  return (
    <aside
      ref={ref}
      role="dialog"
      aria-label={`${label}: ${title}`}
      data-print={printable || undefined}
      className="fixed inset-x-2 top-16 bottom-2 z-30 flex animate-slide-in flex-col overflow-hidden rounded-2xl border border-border bg-panel shadow-float sm:left-auto sm:w-[30rem] max-sm:inset-0 max-sm:rounded-none"
    >
      <div className="flex items-start justify-between gap-3 border-b border-border px-5 pt-4 pb-3">
        <div className="flex min-w-0 flex-col gap-1">
          <span className="text-[11px] font-semibold tracking-[0.08em] text-accent uppercase">{label}</span>
          <h2 className="font-serif text-3xl leading-tight">{title}</h2>
          <span className="truncate text-xs text-muted">{subtitle}</span>
        </div>
        <span className="flex shrink-0 items-center gap-1 print:hidden">
          {printable && (
            <button
              type="button"
              aria-label="Print or save as PDF"
              title="Print or save as PDF"
              onClick={() => window.print()}
              className="flex size-8 items-center justify-center rounded-lg text-muted transition-colors duration-150 hover:bg-raised hover:text-foreground"
            >
              <PrintIcon />
            </button>
          )}
          <button
            type="button"
            aria-label="Close"
            onClick={close}
            className="flex size-8 items-center justify-center rounded-lg text-muted transition-colors duration-150 hover:bg-raised hover:text-foreground"
          >
            <CloseIcon className="size-4" />
          </button>
        </span>
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto">{children}</div>
    </aside>
  );
}

/** A part of a sheet, with a heading and what is on the right of it. */
export function SheetSection({ title, aside, children }: { title: string; aside?: React.ReactNode; children: React.ReactNode }) {
  return (
    <section aria-label={title} className="flex flex-col gap-2.5 border-b border-border px-5 py-4 text-sm">
      <div className="flex items-baseline justify-between gap-3">
        <h3 className="font-semibold">{title}</h3>
        {aside && <span className="font-mono text-xs text-muted tabular-nums">{aside}</span>}
      </div>
      {children}
    </section>
  );
}

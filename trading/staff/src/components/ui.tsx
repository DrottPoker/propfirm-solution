"use client";

import { useEffect, useId, useRef, useState } from "react";

import { CloseIcon, CopyIcon } from "./icons";

// Small building blocks for the staff panel's pages (ADR 0047, ADR 0057): surfaces, figures, text in a tone, buttons,
// fields and dialogs, in the same look as our staff view in Kronant Prop.

export const fieldClass =
  "h-10 rounded-lg border border-border bg-background/60 px-3 outline-none transition-[border-color,box-shadow] duration-150 placeholder:text-muted/70 focus:border-accent focus:ring-3 focus:ring-accent/20";

export const buttonClass =
  "inline-flex h-10 items-center justify-center gap-2 rounded-lg bg-accent px-4 font-semibold text-accent-foreground shadow-[inset_0_1px_0_rgb(255_255_255/0.18),0_1px_2px_rgb(0_0_0/0.25)] transition duration-150 hover:brightness-110 active:translate-y-px disabled:pointer-events-none disabled:opacity-50";

export const secondaryButtonClass =
  "inline-flex h-10 items-center justify-center gap-2 rounded-lg border border-border bg-raised px-4 font-medium transition duration-150 hover:border-muted/60 active:translate-y-px disabled:pointer-events-none disabled:opacity-50";

/** For an action that cannot be undone, such as stopping a key. */
export const dangerButtonClass =
  "inline-flex h-10 items-center justify-center gap-2 rounded-lg bg-loss px-4 font-semibold text-background shadow-[inset_0_1px_0_rgb(255_255_255/0.18),0_1px_2px_rgb(0_0_0/0.25)] transition duration-150 hover:brightness-110 active:translate-y-px disabled:pointer-events-none disabled:opacity-50";

export type Tone = "normal" | "accent" | "profit" | "loss" | "warning" | "muted";

export const toneClass: Record<Tone, string> = {
  normal: "text-foreground",
  accent: "text-accent",
  profit: "text-profit",
  loss: "text-loss",
  warning: "text-warning",
  muted: "text-muted",
};

/** A short status such as "Healthy", as text in its tone, never in a pill (ADR 0047). */
export function Status({ tone, children }: { tone: Tone; children: React.ReactNode }) {
  return <span className={`whitespace-nowrap font-semibold ${toneClass[tone]}`}>{children}</span>;
}

/** A page's title in the serif, with a line under it and room for actions on the right. */
export function PageHeader({ title, text, actions }: { title: string; text?: React.ReactNode; actions?: React.ReactNode }) {
  return (
    <header className="flex flex-wrap items-end justify-between gap-4">
      <div className="flex max-w-3xl flex-col gap-1.5">
        <h1 className="font-serif text-4xl leading-tight">{title}</h1>
        {text && <p className="leading-relaxed text-muted">{text}</p>}
      </div>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </header>
  );
}

/** A surface for a part of a page, with its heading and what goes beside it. */
export function Panel({
  title,
  aside,
  children,
  className = "",
  labelledBy,
}: {
  title?: React.ReactNode;
  aside?: React.ReactNode;
  children: React.ReactNode;
  className?: string;
  labelledBy?: string;
}) {
  const id = useId();
  return (
    <section aria-labelledby={title ? (labelledBy ?? id) : undefined} className={`flex min-w-0 flex-col gap-3 rounded-2xl border border-border bg-panel p-5 shadow-card ${className}`}>
      {(title || aside) && (
        <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
          {title && (
            <h2 id={labelledBy ?? id} className="text-base font-semibold">
              {title}
            </h2>
          )}
          {aside && <div className="text-sm text-muted">{aside}</div>}
        </div>
      )}
      {children}
    </section>
  );
}

/** A labelled figure on its own card, such as the platform's open positions. */
export function FigureCard({ label, value, sub, tone = "normal" }: { label: string; value: React.ReactNode; sub?: React.ReactNode; tone?: Tone }) {
  return (
    <div className="flex flex-col gap-1 rounded-2xl border border-border bg-panel px-4 py-3.5 shadow-card">
      <span className="text-[13px] text-muted">{label}</span>
      <span className={`text-[26px] font-semibold leading-tight tracking-tight ${toneClass[tone]}`}>{value}</span>
      {sub && <span className="text-xs text-muted">{sub}</span>}
    </div>
  );
}

// Whole rows at every width, so no card is left alone on a row.
const figureColumns: Record<4 | 5 | 6, string> = {
  4: "grid-cols-2 xl:grid-cols-4",
  5: "grid-cols-2 sm:grid-cols-3 xl:grid-cols-5",
  6: "grid-cols-2 sm:grid-cols-3 xl:grid-cols-6",
};

/** Figure cards in a grid of as many columns as cards on a wide screen, and fewer on a narrow one. */
export function FigureGrid({ label, count, children }: { label: string; count: 4 | 5 | 6; children: React.ReactNode }) {
  return (
    <section aria-label={label} className={`grid gap-3 ${figureColumns[count]}`}>
      {children}
    </section>
  );
}

/** Label and value pairs, two columns. */
export function Facts({ items }: { items: { label: string; value: React.ReactNode }[] }) {
  return (
    <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-2.5">
      {items.map(({ label, value }) => (
        <div key={label} className="contents">
          <dt className="text-muted">{label}</dt>
          <dd className="min-w-0">{value}</dd>
        </div>
      ))}
    </dl>
  );
}

/** A loading page: placeholders in the shape of a page, faded in after a moment so a fast page never flashes them. */
export function Loading({ label = "Loading" }: { label?: string }) {
  return (
    <div role="status" aria-label={label} className="flex animate-fade flex-col gap-5 [animation-delay:150ms]">
      <span className="skeleton block h-10 w-64 rounded-lg" />
      <div className="grid grid-cols-[repeat(auto-fit,minmax(170px,1fr))] gap-3">
        {[0, 1, 2, 3].map((i) => (
          <span key={i} className="skeleton block h-24 rounded-2xl" />
        ))}
      </div>
      <span className="skeleton block h-72 rounded-2xl" />
    </div>
  );
}

/** What went wrong loading a page or doing something, in the service's own words. */
export function ErrorText({ error }: { error: Error | null | undefined }) {
  return error ? (
    <p role="alert" className="text-sm text-loss">
      {error.message}
    </p>
  ) : null;
}

/** A table that scrolls sideways in its box on a narrow screen, rather than the page. */
export function TableBox({ children, minWidth }: { children: React.ReactNode; minWidth: number }) {
  return (
    <div className="-mx-1 overflow-x-auto px-1">
      <table className="w-full border-collapse text-left" style={{ minWidth }}>
        {children}
      </table>
    </div>
  );
}

export const thClass = "px-2 py-1.5 text-xs font-medium text-muted first:pl-0 last:pr-0";
export const tdClass = "border-t border-border px-2 py-2 first:pl-0 last:pr-0";

/** Copies the text, and says so for a moment. */
export function CopyButton({ value, label }: { value: string; label: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <button
      type="button"
      onClick={async () => {
        await navigator.clipboard.writeText(value);
        setCopied(true);
        window.setTimeout(() => setCopied(false), 1_500);
      }}
      aria-label={`Copy ${label}`}
      className={secondaryButtonClass}
    >
      {copied ? <span className="text-profit">Copied</span> : <CopyIcon className="size-4" />}
      {!copied && "Copy"}
    </button>
  );
}

/**
 * A dialog in the middle of the screen, on the native dialog element: the browser keeps focus inside it, makes the page
 * behind inert and closes it on Escape. A click on the backdrop closes it too.
 */
export function Modal({
  open,
  onClose,
  title,
  description,
  children,
  footer,
}: {
  open: boolean;
  onClose: () => void;
  title: React.ReactNode;
  description?: React.ReactNode;
  children: React.ReactNode;
  footer?: React.ReactNode;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  useEffect(() => {
    const dialog = ref.current;
    if (open && dialog && !dialog.open) {
      dialog.showModal();
    } else if (!open && dialog?.open) {
      dialog.close();
    }
  }, [open]);

  return (
    <dialog
      ref={ref}
      aria-labelledby={titleId}
      onClose={onClose}
      onClick={(event) => event.target === ref.current && onClose()}
      className="m-auto w-[calc(100%-2rem)] max-w-xl rounded-2xl border border-border bg-panel p-0 text-foreground shadow-float backdrop:bg-black/60 backdrop:backdrop-blur-sm open:animate-pop"
    >
      {open && (
        <div className="flex flex-col gap-4 px-6 py-5">
          <div className="flex items-start justify-between gap-3">
            <div className="flex min-w-0 flex-col gap-1">
              <h2 id={titleId} className="text-lg font-semibold leading-snug">
                {title}
              </h2>
              {description && <div className="text-sm leading-relaxed text-muted">{description}</div>}
            </div>
            <button type="button" aria-label="Close" onClick={onClose} className="-mr-2 grid size-9 shrink-0 place-items-center rounded-md text-muted hover:bg-raised hover:text-foreground">
              <CloseIcon />
            </button>
          </div>
          {children}
          {footer && <div className="flex flex-wrap justify-end gap-2.5 pt-1">{footer}</div>}
        </div>
      )}
    </dialog>
  );
}

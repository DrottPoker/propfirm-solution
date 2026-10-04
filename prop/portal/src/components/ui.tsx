// Small building blocks shared by the portal's pages.

export const fieldClass = "rounded border border-border bg-background px-3 py-2 outline-none focus:border-accent";

export const buttonClass = "rounded bg-accent px-4 py-2 font-medium text-accent-foreground disabled:opacity-50";

export const secondaryButtonClass = "rounded border border-border px-4 py-2 hover:border-muted disabled:opacity-50";

export function Message({ text }: { text: string }) {
  return <main className="flex flex-1 items-center justify-center p-8 text-muted">{text}</main>;
}

export function ErrorText({ error }: { error: Error | null }) {
  if (!error) {
    return null;
  }

  return (
    <p role="alert" className="text-sm text-loss">
      {error.message}
    </p>
  );
}

/** A labelled figure, such as a balance, in a <dl>. */
export function Figure({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <dt className="text-sm text-muted">{label}</dt>
      <dd className="font-mono text-lg tabular-nums">{value}</dd>
    </div>
  );
}

export function Panel({ title, actions, children }: { title?: string; actions?: React.ReactNode; children: React.ReactNode }) {
  return (
    <section className="flex flex-col gap-4 rounded-lg border border-border bg-panel p-5">
      {(title || actions) && (
        <div className="flex flex-wrap items-center justify-between gap-3">
          {title && <h2 className="font-semibold">{title}</h2>}
          {actions}
        </div>
      )}
      {children}
    </section>
  );
}

/** The tones a badge, a figure or a bar can have. They come from the firm's colors. */
export type BadgeTone = "accent" | "profit" | "loss" | "warning" | "muted";

const badgeTones: Record<BadgeTone, string> = {
  accent: "bg-accent/15 text-accent",
  profit: "bg-profit/15 text-profit",
  loss: "bg-loss/15 text-loss",
  warning: "bg-warning/15 text-warning",
  muted: "bg-muted/15 text-muted",
};

export function Badge({ tone, children }: { tone: BadgeTone; children: React.ReactNode }) {
  return <span className={`inline-flex items-center whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs font-medium ${badgeTones[tone]}`}>{children}</span>;
}

const barTones: Record<"profit" | "accent" | "warning" | "loss", string> = {
  profit: "bg-profit",
  accent: "bg-accent",
  warning: "bg-warning",
  loss: "bg-loss",
};

/** A bar from 0 to 100, with its label for screen readers. */
export function ProgressBar({ value, label, tone = "profit" }: { value: number; label: string; tone?: keyof typeof barTones }) {
  const clamped = Math.min(100, Math.max(0, value));
  return (
    <div
      role="progressbar"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(clamped)}
      className="h-1.5 overflow-hidden rounded-full bg-border"
    >
      <div className={`h-full rounded-full ${barTones[tone]}`} style={{ width: `${clamped}%` }} />
    </div>
  );
}

/** Steps such as trading days, filled from the left. */
export function StepBar({ filled, total, label }: { filled: number; total: number; label: string }) {
  return (
    <div role="img" aria-label={label} className="grid gap-1" style={{ gridTemplateColumns: `repeat(${Math.min(total, 30)}, minmax(0, 1fr))` }}>
      {Array.from({ length: Math.min(total, 30) }, (_, i) => (
        <span key={i} className={`h-1.5 rounded-full ${i < filled ? "bg-profit" : "bg-border"}`} />
      ))}
    </div>
  );
}

/** A small heading over a group of cards, such as "Trading". */
export function SectionLabel({ id, children }: { id?: string; children: React.ReactNode }) {
  return (
    <h2 id={id} className="text-xs font-medium uppercase tracking-wider text-muted">
      {children}
    </h2>
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
  return (
    <div role="group" aria-label={label} className="flex flex-wrap gap-0.5 rounded-md border border-border p-0.5 text-xs">
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          aria-pressed={option.value === value}
          onClick={() => onChange(option.value)}
          className={`rounded px-2.5 py-1 ${option.value === value ? "bg-background font-medium text-foreground" : "text-muted hover:text-foreground"}`}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}

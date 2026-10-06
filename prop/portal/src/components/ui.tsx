// Small building blocks shared by the portal's pages.

export const fieldClass = "rounded border border-border bg-background px-3 py-2 outline-none focus:border-accent";

export const buttonClass = "rounded bg-accent px-4 py-2 font-medium text-accent-foreground disabled:opacity-50";

export const secondaryButtonClass = "rounded border border-border px-4 py-2 hover:border-muted disabled:opacity-50";

/** For an action that cannot be undone, such as rejecting a payout. */
export const dangerButtonClass = "rounded bg-loss px-4 py-2 font-medium text-background disabled:opacity-50";

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

/**
 * A bar from 0 to 100, with its label for screen readers. A ghost, such as where a target would be with the open
 * trades closed, is drawn lighter behind the value, so it shows where it goes beyond it.
 */
export function ProgressBar({ value, label, tone = "profit", ghost }: { value: number; label: string; tone?: keyof typeof barTones; ghost?: number }) {
  const clamped = Math.min(100, Math.max(0, value));
  return (
    <div
      role="progressbar"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(clamped)}
      className="relative h-1.5 overflow-hidden rounded-full bg-border"
    >
      {ghost !== undefined && (
        <div className={`absolute inset-y-0 left-0 rounded-full opacity-35 ${barTones[tone]}`} style={{ width: `${Math.min(100, Math.max(0, ghost))}%` }} />
      )}
      <div className={`relative h-full rounded-full ${barTones[tone]}`} style={{ width: `${clamped}%` }} />
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

/**
 * A trader's page in the firm's portal, under its header and as wide as it, so every page starts at the same left
 * edge. A narrow page, such as a form or a conversation, keeps a readable width from that edge.
 */
export function TraderPage({ children, narrow = false, gap = "gap-6" }: { children: React.ReactNode; narrow?: boolean; gap?: string }) {
  return (
    <main className={`mx-auto flex w-full max-w-6xl flex-col px-4 py-8 sm:px-6 ${gap}`}>
      {narrow ? <div className={`flex w-full max-w-4xl flex-col ${gap}`}>{children}</div> : children}
    </main>
  );
}

/** An admin panel page's content, beside the menu. */
export function AdminPage({ children, narrow = false }: { children: React.ReactNode; narrow?: boolean }) {
  return (
    <main className="mx-auto flex w-full max-w-6xl flex-col gap-6 px-4 py-6 sm:px-7 sm:py-7">
      {narrow ? <div className="flex w-full max-w-4xl flex-col gap-6">{children}</div> : children}
    </main>
  );
}

/** A page's title with what it is for, and its main actions beside it. */
export function PageHeader({ title, description, actions, back }: { title: string; description?: React.ReactNode; actions?: React.ReactNode; back?: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-3">
      {back}
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div className="flex min-w-0 flex-col gap-1">
          <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
          {description && <p className="max-w-3xl text-muted">{description}</p>}
        </div>
        {actions && <div className="flex flex-wrap items-center gap-2.5">{actions}</div>}
      </div>
    </div>
  );
}

/** A figure on its own tile, such as the payouts paid in the last 30 days. */
export function StatTile({ label, value, unit, note, tone = "", highlight = false }: { label: string; value: string; unit?: string; note?: React.ReactNode; tone?: string; highlight?: boolean }) {
  return (
    <div className={`flex flex-col gap-1 rounded-lg border bg-panel px-4 py-3.5 ${highlight ? "border-accent/50" : "border-border"}`}>
      <dt className="text-xs text-muted">{label}</dt>
      <dd className={`font-mono text-xl font-medium tabular-nums sm:text-2xl ${tone}`}>
        {value} {unit && <span className="text-sm text-muted">{unit}</span>}
      </dd>
      {note && <dd className="text-xs text-muted">{note}</dd>}
    </div>
  );
}

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
  return (
    <div role="group" aria-label={label} className="flex flex-wrap gap-1 self-start rounded-lg border border-border bg-panel p-1 text-sm">
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          aria-pressed={option.value === value}
          onClick={() => onChange(option.value)}
          className={`flex items-center gap-2 rounded-md px-3 py-1.5 ${option.value === value ? "bg-background font-medium text-foreground" : "text-muted hover:text-foreground"}`}
        >
          {option.label}
          {option.count !== undefined && (
            <span className={`rounded-full px-1.5 font-mono text-xs tabular-nums ${option.highlight && option.count > 0 ? "bg-warning/20 text-warning" : ""}`}>{option.count}</span>
          )}
        </button>
      ))}
    </div>
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
  return (
    <div role="tablist" aria-label={label} className="flex gap-1 overflow-x-auto border-b border-border">
      {tabs.map((tab) => (
        <button
          key={tab.value}
          type="button"
          role="tab"
          id={`tab-${tab.value}`}
          aria-selected={tab.value === value}
          aria-controls={`panel-${tab.value}`}
          onClick={() => onChange(tab.value)}
          className={`-mb-px whitespace-nowrap border-b-2 px-3.5 py-2.5 text-sm ${tab.value === value ? "border-accent font-medium text-foreground" : "border-transparent text-muted hover:text-foreground"}`}
        >
          {tab.label}
          {tab.count !== undefined && <span className="ml-1.5 font-mono text-xs tabular-nums text-muted">{tab.count}</span>}
        </button>
      ))}
    </div>
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

// Small building blocks shared by the portal's pages (ADR 0047): fields, buttons, surfaces with depth, figures, bars
// and page frames. They use only the theme's color tokens, so a firm's colors carry through.

export { FilterTabs, SegmentedControl, Switch, Tabs } from "./Choice";

export const fieldClass =
  "rounded-lg border border-border bg-background/60 px-3 py-2 outline-none transition-[border-color,box-shadow] duration-150 placeholder:text-muted/70 focus:border-accent focus:ring-3 focus:ring-accent/20";

export const buttonClass =
  "rounded-lg bg-accent px-4 py-2 text-center font-medium text-accent-foreground shadow-[inset_0_1px_0_rgb(255_255_255/0.18),0_1px_2px_rgb(0_0_0/0.25)] transition duration-150 ease-out-soft hover:brightness-110 active:translate-y-px disabled:pointer-events-none disabled:opacity-50";

export const secondaryButtonClass =
  "rounded-lg border border-border bg-panel/60 px-4 py-2 text-center transition duration-150 ease-out-soft hover:border-muted/60 hover:bg-raised active:translate-y-px disabled:pointer-events-none disabled:opacity-50";

/** For an action that cannot be undone, such as rejecting a payout. */
export const dangerButtonClass =
  "rounded-lg bg-loss px-4 py-2 font-medium text-background shadow-[inset_0_1px_0_rgb(255_255_255/0.18),0_1px_2px_rgb(0_0_0/0.25)] transition duration-150 hover:brightness-110 active:translate-y-px disabled:pointer-events-none disabled:opacity-50";

export function Message({ text }: { text: string }) {
  return <main className="flex flex-1 items-center justify-center p-8 text-muted">{text}</main>;
}

/**
 * A page that is loading: placeholders in the shape of a page, a title and three panels, with a soft band of light
 * passing over them. They fade in after a moment, so a fast page never flashes them.
 */
export function Loading({ label = "Loading" }: { label?: string }) {
  return (
    <main
      role="status"
      aria-label={label}
      className="mx-auto flex w-full max-w-6xl animate-fade [animation-delay:150ms] flex-col gap-6 px-4 py-8 text-muted sm:px-7"
    >
      <div className="flex flex-col gap-3">
        <Skeleton className="h-9 w-64" />
        <Skeleton className="h-4 w-96 max-w-full" />
      </div>
      <div className="grid gap-4 sm:grid-cols-3">
        {[0, 1, 2].map((i) => (
          <Skeleton key={i} className="h-24 rounded-xl" />
        ))}
      </div>
      <Skeleton className="h-64 rounded-xl" />
    </main>
  );
}

/** A placeholder of the size its class gives it, while what goes there loads. */
export function Skeleton({ className = "" }: { className?: string }) {
  return <span aria-hidden="true" className={`skeleton block text-muted ${className}`} />;
}

/** Nothing to show yet: a mark, what is missing, and the way to change that. */
export function EmptyState({
  icon,
  title,
  text,
  actions,
  titleAs: Title = "p",
}: {
  icon?: React.ReactNode;
  title: string;
  text?: React.ReactNode;
  actions?: React.ReactNode;
  /** A heading when the empty state stands on its own on the page, rather than inside a panel with one. */
  titleAs?: "p" | "h2";
}) {
  return (
    <div className="flex flex-col items-center gap-3 px-6 py-10 text-center">
      {icon && <span className="grid size-12 place-items-center rounded-2xl border border-border bg-raised text-accent shadow-card">{icon}</span>}
      <div className="flex max-w-md flex-col gap-1">
        <Title className={Title === "h2" ? "text-lg font-semibold" : "font-medium"}>{title}</Title>
        {text && <p className="text-sm text-muted">{text}</p>}
      </div>
      {actions && <div className="mt-1 flex flex-wrap justify-center gap-2.5">{actions}</div>}
    </div>
  );
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
      <dd className="text-lg font-medium">{value}</dd>
    </div>
  );
}

/** A surface for a part of a page, lifted off the background with a soft shadow and a light top edge. */
export function Panel({ title, actions, children }: { title?: string; actions?: React.ReactNode; children: React.ReactNode }) {
  return (
    <section className="flex flex-col gap-4 rounded-xl border border-border bg-panel p-5 shadow-card">
      {(title || actions) && (
        <div className="flex flex-wrap items-center justify-between gap-3">
          {title && <h2 className="font-semibold tracking-tight">{title}</h2>}
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
  accent: "text-accent",
  profit: "text-profit",
  loss: "text-loss",
  warning: "text-warning",
  muted: "text-muted",
};

/** A short status, such as "Funded", as text in its tone. No pill around it, so it reads as part of the page. */
export function Badge({ tone, children }: { tone: BadgeTone; children: React.ReactNode }) {
  return <span className={`inline-flex items-center whitespace-nowrap text-sm font-medium ${badgeTones[tone]}`}>{children}</span>;
}

const barTones: Record<"profit" | "accent" | "warning" | "loss", string> = {
  profit: "bg-profit",
  accent: "bg-accent",
  warning: "bg-warning",
  loss: "bg-loss",
};

/**
 * A bar from 0 to 100, with its label for screen readers, which fills from the left when it appears. A ghost, such as
 * where a target would be with the open trades closed, is drawn lighter behind the value, so it shows where it goes
 * beyond it.
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
        <div
          className={`absolute inset-y-0 left-0 origin-left animate-grow-x rounded-full opacity-35 transition-[width] duration-700 ease-out-soft ${barTones[tone]}`}
          style={{ width: `${Math.min(100, Math.max(0, ghost))}%` }}
        />
      )}
      <div
        className={`relative h-full origin-left animate-grow-x rounded-full transition-[width,background-color] duration-700 ease-out-soft ${barTones[tone]}`}
        style={{ width: `${clamped}%` }}
      />
    </div>
  );
}

/** Steps such as trading days, filled from the left, one after another when they appear. */
export function StepBar({ filled, total, label }: { filled: number; total: number; label: string }) {
  return (
    <div role="img" aria-label={label} className="grid gap-1" style={{ gridTemplateColumns: `repeat(${Math.min(total, 30)}, minmax(0, 1fr))` }}>
      {Array.from({ length: Math.min(total, 30) }, (_, i) => (
        <span
          key={i}
          className={`h-1.5 origin-left rounded-full ${i < filled ? "animate-grow-x bg-profit" : "bg-border"}`}
          style={i < filled ? { animationDelay: `${i * 70}ms` } : undefined}
        />
      ))}
    </div>
  );
}

/** A small heading over a group of cards, such as "Trading". */
export function SectionLabel({ id, children }: { id?: string; children: React.ReactNode }) {
  return (
    <h2 id={id} className="text-xs font-semibold uppercase tracking-[0.14em] text-muted">
      {children}
    </h2>
  );
}

/**
 * A trader's page in the firm's portal, under its header and as wide as it, so every page starts at the same left
 * edge. A narrow page, such as a form or a conversation, keeps a readable width from that edge. Its parts come in one
 * after another.
 */
export function TraderPage({ children, narrow = false, gap = "gap-6" }: { children: React.ReactNode; narrow?: boolean; gap?: string }) {
  return (
    <main className={`mx-auto flex w-full max-w-6xl flex-col px-4 py-8 sm:px-6 ${narrow ? "" : "stagger"} ${gap}`}>
      {narrow ? <div className={`stagger flex w-full max-w-4xl flex-col ${gap}`}>{children}</div> : children}
    </main>
  );
}

/** An admin panel page's content, beside the menu. Its parts come in one after another. */
export function AdminPage({ children, narrow = false }: { children: React.ReactNode; narrow?: boolean }) {
  return (
    <main className={`mx-auto flex w-full max-w-6xl flex-col gap-6 px-4 py-6 sm:px-7 sm:py-8 ${narrow ? "" : "stagger"}`}>
      {narrow ? <div className="stagger flex w-full max-w-4xl flex-col gap-6">{children}</div> : children}
    </main>
  );
}

/** A page's title, in the display face, with what it is for, and its main actions beside it. */
export function PageHeader({ title, description, actions, back }: { title: string; description?: React.ReactNode; actions?: React.ReactNode; back?: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-3">
      {back}
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div className="flex min-w-0 flex-col gap-1.5">
          <h1 className="font-serif text-[2.35rem] leading-[1.05] tracking-tight">{title}</h1>
          {description && <p className="max-w-3xl text-muted">{description}</p>}
        </div>
        {actions && <div className="flex flex-wrap items-center gap-2.5">{actions}</div>}
      </div>
    </div>
  );
}

/** A figure on its own tile, such as the payouts paid in the last 30 days, with something beside it such as a curve. */
export function StatTile({
  label,
  value,
  unit,
  note,
  tone = "",
  highlight = false,
  aside,
}: {
  label: string;
  value: React.ReactNode;
  unit?: string;
  note?: React.ReactNode;
  tone?: string;
  highlight?: boolean;
  aside?: React.ReactNode;
}) {
  return (
    <div
      className={`relative flex flex-col gap-1 overflow-hidden rounded-xl border bg-panel px-4 py-3.5 shadow-card ${highlight ? "border-accent/50" : "border-border"}`}
    >
      <dt className="text-xs text-muted">{label}</dt>
      <dd className="flex items-end justify-between gap-3">
        <span className={`text-xl font-semibold tracking-tight sm:text-2xl ${tone}`}>
          {value} {unit && <span className="text-sm font-normal text-muted">{unit}</span>}
        </span>
        {aside && <span className="-mb-1 shrink-0">{aside}</span>}
      </dd>
      {note && <dd className="text-xs text-muted">{note}</dd>}
    </div>
  );
}

/**
 * A small curve of values over time, such as sales per week, filled softly below it and drawn from the left when it
 * appears. Decoration beside a figure that says the same in words, so it is hidden from screen readers.
 */
export function Sparkline({ values, tone = "accent", className = "h-9 w-28" }: { values: number[]; tone?: "accent" | "profit" | "loss"; className?: string }) {
  if (values.length < 2) {
    return null;
  }

  const width = 112;
  const height = 36;
  const max = Math.max(...values);
  const min = Math.min(0, ...values);
  const span = max - min || 1;
  const points = values.map((value, i) => [(i / (values.length - 1)) * width, height - 3 - ((value - min) / span) * (height - 6)] as const);
  const line = points.map(([x, y], i) => `${i === 0 ? "M" : "L"}${x.toFixed(1)},${y.toFixed(1)}`).join(" ");
  const area = `${line} L${width},${height} L0,${height} Z`;
  const color = { accent: "var(--accent)", profit: "var(--profit)", loss: "var(--loss)" }[tone];
  const gradient = `spark-${tone}`;
  return (
    <svg viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" aria-hidden="true" className={className}>
      <defs>
        <linearGradient id={gradient} x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor={color} stopOpacity="0.28" />
          <stop offset="100%" stopColor={color} stopOpacity="0" />
        </linearGradient>
      </defs>
      <path d={area} fill={`url(#${gradient})`} className="animate-fade" />
      <path
        d={line}
        pathLength={1}
        strokeDasharray="1"
        fill="none"
        stroke={color}
        strokeWidth={1.75}
        strokeLinejoin="round"
        strokeLinecap="round"
        vectorEffect="non-scaling-stroke"
        className="animate-draw"
      />
    </svg>
  );
}

/** How far along something is, as a ring that fills when it appears, with what is in it, such as "6 of 9", in the middle. */
export function ProgressRing({ value, label, children, size = 64 }: { value: number; label: string; children?: React.ReactNode; size?: number }) {
  const clamped = Math.min(100, Math.max(0, value));
  return (
    <div
      role="progressbar"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(clamped)}
      className="relative grid shrink-0 place-items-center"
      style={{ width: size, height: size }}
    >
      <svg viewBox="0 0 36 36" className="absolute inset-0 -rotate-90">
        <circle cx="18" cy="18" r="15.5" fill="none" stroke="var(--border)" strokeWidth="3" />
        <circle
          cx="18"
          cy="18"
          r="15.5"
          fill="none"
          stroke="var(--profit)"
          strokeWidth="3"
          strokeLinecap="round"
          pathLength={100}
          strokeDasharray="100"
          strokeDashoffset={100 - clamped}
          className="animate-ring-fill transition-[stroke-dashoffset] duration-700"
        />
      </svg>
      <span className="relative text-xs font-semibold">{children}</span>
    </div>
  );
}

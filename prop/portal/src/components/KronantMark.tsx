import { kronantMarkPaths } from "@/lib/icon";

/** Kronant's mark, a crown on a brass coin, beside the product's name when there is one. Ours, never a firm's. */
export function KronantMark({ className = "size-7" }: { className?: string }) {
  return (
    <svg viewBox="0 0 32 32" aria-hidden="true" className={className}>
      <path d={kronantMarkPaths.coin} fill="var(--kronant-brass)" />
      <path d={kronantMarkPaths.crown} fill="var(--kronant-brass-foreground)" />
      <path d={kronantMarkPaths.band} stroke="var(--kronant-brass-foreground)" strokeWidth={1.6} strokeLinecap="round" />
    </svg>
  );
}

/** Our name as a wordmark: the mark, "Kronant" in the display face and the product after it, such as "Prop". */
export function KronantWordmark({ product, className = "" }: { product: string; className?: string }) {
  return (
    <span className={`inline-flex items-center gap-2 ${className}`}>
      <KronantMark />
      <span className="flex items-baseline gap-1.5">
        <span className="font-serif text-[1.45rem] leading-none tracking-tight">Kronant</span>
        <span className="text-xs font-semibold uppercase tracking-[0.18em] text-muted">{product}</span>
      </span>
    </span>
  );
}

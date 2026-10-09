/**
 * Kronant's mark: a crown on a brass coin, the krona it is named after. Drawn on 32 by 32, the same as in the firm's
 * portal and in the tab's icon (src/app/icon.svg). Copied rather than imported, since the products share no code.
 */
const markPaths = {
  coin: "M16 1.5a14.5 14.5 0 1 1 0 29 14.5 14.5 0 0 1 0-29Z",
  crown: "M8.5 21.5V12l4.4 3.6L16 9.2l3.1 6.4 4.4-3.6v9.5Z",
  band: "M8.5 23.2h15",
} as const;

export function KronantMark({ className = "size-7" }: { className?: string }) {
  return (
    <svg viewBox="0 0 32 32" aria-hidden="true" className={className}>
      <path d={markPaths.coin} fill="var(--kronant-brass)" />
      <path d={markPaths.crown} fill="var(--kronant-brass-foreground)" />
      <path d={markPaths.band} stroke="var(--kronant-brass-foreground)" strokeWidth={1.6} strokeLinecap="round" />
    </svg>
  );
}

/**
 * The product's name as a wordmark: the mark, "Kronant" in the serif and the rest of the name, "Trader", small after
 * it, or a smaller one, such as under a firm's login. Screen readers get the name in one piece.
 */
export function KronantWordmark({ name, className = "", small = false }: { name: string; className?: string; small?: boolean }) {
  const product = name.replace(/^Kronant\s*/, "");
  return (
    <span className={`inline-flex items-center ${small ? "gap-1.5" : "gap-2.5"} ${className}`}>
      <KronantMark className={small ? "size-4" : "size-8"} />
      <span aria-hidden="true" className="flex items-baseline gap-1.5">
        <span className={`font-serif leading-none tracking-tight text-foreground ${small ? "text-base" : "text-[1.6rem]"}`}>Kronant</span>
        {product && <span className={`font-semibold tracking-[0.18em] text-muted uppercase ${small ? "text-[9px]" : "text-xs"}`}>{product}</span>}
      </span>
      <span className="sr-only">{name}</span>
    </span>
  );
}

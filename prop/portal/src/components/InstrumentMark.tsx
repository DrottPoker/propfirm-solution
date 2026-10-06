import { instrumentMarks } from "@/lib/instruments";

/** Two small overlapping coins with the symbol's halves, such as € and $, beside an instrument. Decoration. */
export function InstrumentMark({ symbol }: { symbol: string }) {
  const marks = instrumentMarks(symbol);
  if (!marks) {
    return <span aria-hidden="true" className="grid size-7 shrink-0 place-items-center rounded-full border border-border bg-raised text-[10px] font-semibold text-muted">{symbol.slice(0, 2)}</span>;
  }

  return (
    <span aria-hidden="true" className="relative flex h-7 w-10 shrink-0">
      <span className="absolute left-0 top-0 grid size-7 place-items-center rounded-full border border-border bg-raised text-[10px] font-semibold shadow-card">{marks[0]}</span>
      <span className="absolute bottom-0 right-0 grid size-5 place-items-center rounded-full border border-border bg-panel text-[9px] font-semibold text-muted shadow-card">{marks[1]}</span>
    </span>
  );
}

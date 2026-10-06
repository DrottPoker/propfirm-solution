import type { StopUnit } from "@/lib/stops";

/** Chooses whether stop loss and take profit are typed as prices or as amounts in the account currency. */
export function StopUnitToggle({ unit, currency, onChange }: { unit: StopUnit; currency: string; onChange: (unit: StopUnit) => void }) {
  return (
    <div role="group" aria-label="Stops in" className="flex shrink-0 rounded-lg bg-background p-0.5 text-[11px]">
      {(["price", "money"] as const).map((u) => (
        <button
          key={u}
          type="button"
          aria-pressed={u === unit}
          onClick={() => onChange(u)}
          className={`rounded-md px-2 py-0.5 font-medium transition duration-150 active:translate-y-px ${u === unit ? "bg-raised text-foreground shadow-card" : "text-muted hover:text-foreground"}`}
        >
          {u === "price" ? "Price" : currency}
        </button>
      ))}
    </div>
  );
}

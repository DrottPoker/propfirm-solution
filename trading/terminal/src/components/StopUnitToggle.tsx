import type { StopUnit } from "@/lib/stops";

/** Chooses whether stop loss and take profit are typed as prices or as amounts in the account currency. */
export function StopUnitToggle({ unit, currency, onChange }: { unit: StopUnit; currency: string; onChange: (unit: StopUnit) => void }) {
  return (
    <div role="group" aria-label="Stops in" className="flex shrink-0 rounded-md bg-background p-0.5 text-[11px]">
      {(["price", "money"] as const).map((u) => (
        <button
          key={u}
          type="button"
          aria-pressed={u === unit}
          onClick={() => onChange(u)}
          className={`rounded px-2 py-0.5 font-medium ${u === unit ? "bg-raised text-foreground" : "text-muted hover:text-foreground"}`}
        >
          {u === "price" ? "Price" : currency}
        </button>
      ))}
    </div>
  );
}

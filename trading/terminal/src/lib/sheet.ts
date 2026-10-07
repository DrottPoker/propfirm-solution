import { create } from "zustand";

/**
 * A panel over the terminal: a trade's details, or the report of a broken loss limit (ADR 0053), or the trader's own
 * limits, or locking the rest of the day (ADR 0054), or the terminal's settings (ADR 0055).
 */
export type Sheet = { kind: "details"; positionId: string } | { kind: "breach" } | { kind: "limits" } | { kind: "lock" } | { kind: "settings" };

interface SheetState {
  sheet: Sheet | null;
  open: (sheet: Sheet) => void;
  close: () => void;
}

/** The panel open over the terminal, one at a time. */
export const useSheet = create<SheetState>()((set) => ({
  sheet: null,
  open: (sheet) => set({ sheet }),
  close: () => set({ sheet: null }),
}));

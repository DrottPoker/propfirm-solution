import { create } from "zustand";

/** A panel over the terminal: a trade's details, or the report of a broken loss limit (ADR 0053). */
export type Sheet = { kind: "details"; positionId: string } | { kind: "breach" };

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

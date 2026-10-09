import { create } from "zustand";

import type { InstrumentInfo } from "./api/types";
import { parseVolume } from "./orderInput";
import type { TerminalProfile } from "./profile";
import { useProfile } from "./profileContext";
import { useSettings } from "./settings";
import { startVolume } from "./ticket";
import { loadVolumes, rememberVolume } from "./volumes";

interface TicketState {
  /** The volume typed for each symbol while the terminal is open, valid or not. */
  volumes: Record<string, string>;
  setVolume: (symbol: string, volume: string) => void;
}

/**
 * The volume of each symbol's ticket, shared by the order panel and the buy and sell buttons on the chart (ADR 0058),
 * so both send what the trader sees. A valid volume is also remembered for the symbol on the login (ADR 0052).
 */
export const useTicket = create<TicketState>()((set) => ({
  volumes: {},
  setVolume: (symbol, volume) => set((state) => ({ volumes: { ...state.volumes, [symbol]: volume } })),
}));

/** Sets the symbol's volume, and remembers it on the login when the instrument allows it. */
export function changeVolume(instrument: InstrumentInfo, volume: string) {
  useTicket.getState().setVolume(instrument.symbol, volume);
  if (parseVolume(volume, instrument).ok) {
    rememberVolume(instrument.symbol, volume);
  }
}

/** The volume of the symbol's ticket: as typed, or where the ticket starts (ADR 0058). */
export function useVolume(instrument: InstrumentInfo): string {
  const typed = useTicket((s) => s.volumes[instrument.symbol]);
  const profile = useProfile();
  return typed ?? startVolume(instrument, loadVolumes(), profile.startingSize);
}

/** The same, outside React, for sending an order from the chart. */
export function volumeOf(instrument: InstrumentInfo, profile: TerminalProfile): string {
  return useTicket.getState().volumes[instrument.symbol] ?? startVolume(instrument, loadVolumes(), profile.startingSize);
}

/**
 * Whether an order asks before it is sent: as the trader chose in Settings, or, until they chose, as the firm's profile
 * says (ADR 0058). A broker's clients are asked by default; a prop firm's traders are not.
 */
export function useConfirmOrders(): boolean {
  const profile = useProfile();
  const chosen = useSettings((s) => s.confirmOrders);
  const madeChoice = useSettings((s) => s.confirmOrdersChosen);
  return madeChoice ? chosen : profile.confirmOrders;
}

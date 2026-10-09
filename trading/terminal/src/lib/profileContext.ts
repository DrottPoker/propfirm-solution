import { createContext, useContext } from "react";

import { standardProfile, type TerminalProfile } from "./profile";

/** How the open server's terminal works (ADR 0058), from the trader's login. */
export const ProfileContext = createContext<TerminalProfile>(standardProfile);

export function useProfile(): TerminalProfile {
  return useContext(ProfileContext);
}

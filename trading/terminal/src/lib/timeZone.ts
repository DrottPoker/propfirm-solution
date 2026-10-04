import { createContext, useContext } from "react";

/** The time zone the terminal shows times in: the open account's, which its trading day follows. */
export const TimeZoneContext = createContext("UTC");

export function useTimeZone(): string {
  return useContext(TimeZoneContext);
}

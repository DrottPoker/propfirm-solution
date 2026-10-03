import type { FloorSnapshot } from "./api/types";

/** A floor's name for the trader, from its id: "max-loss" becomes "Max loss floor". */
export function floorLabel(floorId: string): string {
  const words = floorId.replace(/[-_]+/g, " ").trim();
  return words.length > 0 ? `${words[0].toUpperCase()}${words.slice(1)} floor` : "Floor";
}

export type FloorRisk = "ok" | "warning" | "danger";

/**
 * How close equity is to the floor, measured against the floor's distance. Floors at a fixed level have no
 * distance and are never marked.
 */
export function floorRisk(floor: FloorSnapshot): FloorRisk {
  const distance = "distance" in floor.rule ? floor.rule.distance : null;
  if (!distance || distance <= 0) {
    return "ok";
  }

  const left = floor.headroom / distance;
  return left < 0.1 ? "danger" : left < 0.25 ? "warning" : "ok";
}

/** Two letters for the trader's avatar, from the email address: anna.berg@example.com becomes AB. */
export function initials(email: string): string {
  const name = email.split("@")[0] ?? "";
  const parts = name.split(/[._+-]+/).filter((p) => p.length > 0);
  const letters = parts.length > 1 ? `${parts[0][0]}${parts[1][0]}` : name.slice(0, 2);
  return letters.toUpperCase() || "?";
}

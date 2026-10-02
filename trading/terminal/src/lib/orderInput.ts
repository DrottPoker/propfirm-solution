import type { InstrumentInfo } from "./api/types";

export type Parsed = { ok: true; value: number | null } | { ok: false };

/**
 * Parses a typed price. Empty input means no price. The price must be positive and have
 * no more decimals than the instrument, so what the trader typed is exactly what is sent.
 */
export function parsePrice(input: string, digits: number): Parsed {
  const text = normalize(input);
  if (text === "") {
    return { ok: true, value: null };
  }

  return isPositiveDecimal(text) && decimalsOf(text) <= digits ? { ok: true, value: Number(text) } : { ok: false };
}

/** Parses a typed volume and checks it against the instrument's limits and step. */
export function parseVolume(input: string, instrument: InstrumentInfo): Parsed {
  const text = normalize(input);
  if (!isPositiveDecimal(text)) {
    return { ok: false };
  }

  // Compare in whole step units to avoid binary floating point.
  const scale = 10 ** Math.max(decimalsOf(text), decimalsOf(String(instrument.volumeStep)));
  const units = Math.round(Number(text) * scale);
  const stepUnits = Math.round(instrument.volumeStep * scale);
  const valid =
    units % stepUnits === 0 &&
    units >= Math.round(instrument.volumeMin * scale) &&
    units <= Math.round(instrument.volumeMax * scale);
  return valid ? { ok: true, value: Number(text) } : { ok: false };
}

function normalize(input: string): string {
  return input.trim().replace(",", ".");
}

function isPositiveDecimal(text: string): boolean {
  return /^\d+(\.\d+)?$/.test(text) && Number(text) > 0;
}

function decimalsOf(text: string): number {
  const dot = text.indexOf(".");
  return dot === -1 ? 0 : text.length - dot - 1;
}

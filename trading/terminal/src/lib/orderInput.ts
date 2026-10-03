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

/**
 * The volume one step up or down from the typed one, kept within the instrument's limits. Typed volumes
 * off the step grid snap to it, and input that is not a number starts from the smallest volume.
 */
export function stepVolume(input: string, direction: 1 | -1, instrument: InstrumentInfo): string {
  const decimals = decimalsOf(String(instrument.volumeStep));
  const scale = 10 ** decimals;
  const step = Math.round(instrument.volumeStep * scale);
  const min = Math.round(instrument.volumeMin * scale);
  const max = Math.round(instrument.volumeMax * scale);

  const text = normalize(input);
  if (!isPositiveDecimal(text)) {
    return (min / scale).toFixed(decimals);
  }

  // Count the typed volume in whole steps, with enough decimals to read it exactly.
  const fine = 10 ** Math.max(decimalsOf(text), decimals);
  const steps = Math.round(Math.round(Number(text) * fine) / Math.round(instrument.volumeStep * fine));
  const next = Math.min(max, Math.max(min, (steps + direction) * step));
  return (next / scale).toFixed(decimals);
}

/** One pip: a tenth of the last two decimals, for example 0.0001 for EURUSD and 0.01 for USDJPY. */
export function pipSize(digits: number): number {
  return digits > 0 ? 10 ** (1 - digits) : 1;
}

/**
 * The price one pip up or down from the typed one. Empty input starts from <paramref name="start"/>,
 * usually the current price, and stays empty without one. The price never goes below one point.
 */
export function stepPrice(input: string, direction: 1 | -1, digits: number, start: number | undefined): string {
  const parsed = parsePrice(input, digits);
  const from = parsed.ok && parsed.value !== null ? parsed.value : start;
  if (from === undefined) {
    return input;
  }

  // Whole points avoid binary floating point.
  const scale = 10 ** digits;
  const pip = Math.round(pipSize(digits) * scale);
  const next = Math.max(1, Math.round(from * scale) + direction * pip);
  return (next / scale).toFixed(digits);
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

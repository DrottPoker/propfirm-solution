// Indicators on the bid candles, worked out in the browser from the candles on the chart. Each returns one value per
// candle, null where there are too few candles before it.

export type Series = (number | null)[];

/** Simple moving average: the mean of the last period closes. */
export function sma(values: readonly number[], period: number): Series {
  const result: Series = [];
  let sum = 0;
  for (let i = 0; i < values.length; i++) {
    sum += values[i];
    if (i >= period) {
      sum -= values[i - period];
    }
    result.push(i >= period - 1 ? sum / period : null);
  }
  return result;
}

/** Exponential moving average with weight 2 / (period + 1), started from the simple average of the first period values. */
export function ema(values: readonly number[], period: number): Series {
  const result: Series = [];
  const weight = 2 / (period + 1);
  let previous: number | null = null;
  let sum = 0;
  for (let i = 0; i < values.length; i++) {
    if (previous === null) {
      sum += values[i];
      if (i === period - 1) {
        previous = sum / period;
      }
      result.push(previous);
      continue;
    }

    previous = values[i] * weight + previous * (1 - weight);
    result.push(previous);
  }
  return result;
}

/** Bollinger Bands: the simple average, and that plus and minus width standard deviations of the same closes. */
export function bollinger(values: readonly number[], period: number, width: number): { middle: Series; upper: Series; lower: Series } {
  const middle = sma(values, period);
  const upper: Series = [];
  const lower: Series = [];
  for (let i = 0; i < values.length; i++) {
    const mean = middle[i];
    if (mean === null) {
      upper.push(null);
      lower.push(null);
      continue;
    }

    let squares = 0;
    for (let j = i - period + 1; j <= i; j++) {
      squares += (values[j] - mean) ** 2;
    }
    const deviation = Math.sqrt(squares / period);
    upper.push(mean + width * deviation);
    lower.push(mean - width * deviation);
  }
  return { middle, upper, lower };
}

/** Relative Strength Index with Wilder's smoothing, from 0 to 100. */
export function rsi(values: readonly number[], period: number): Series {
  const result: Series = values.length > 0 ? [null] : [];
  let gain = 0;
  let loss = 0;
  for (let i = 1; i < values.length; i++) {
    const change = values[i] - values[i - 1];
    const up = Math.max(change, 0);
    const down = Math.max(-change, 0);
    if (i <= period) {
      gain += up / period;
      loss += down / period;
      if (i < period) {
        result.push(null);
        continue;
      }
    } else {
      gain = (gain * (period - 1) + up) / period;
      loss = (loss * (period - 1) + down) / period;
    }

    result.push(loss === 0 ? (gain === 0 ? 50 : 100) : 100 - 100 / (1 + gain / loss));
  }
  return result;
}

/** MACD: the fast average minus the slow one, its signal line, and the difference between them as a histogram. */
export function macd(values: readonly number[], fast: number, slow: number, signal: number): { macd: Series; signal: Series; histogram: Series } {
  const fastLine = ema(values, fast);
  const slowLine = ema(values, slow);
  const line: Series = values.map((_, i) => (fastLine[i] === null || slowLine[i] === null ? null : fastLine[i]! - slowLine[i]!));

  // The signal is an average of the MACD line from where it starts.
  const first = line.findIndex((v) => v !== null);
  const signalLine: Series = first < 0 ? line.map(() => null) : [...line.slice(0, first), ...ema(line.slice(first) as number[], signal)];
  const histogram: Series = line.map((v, i) => (v === null || signalLine[i] === null ? null : v - signalLine[i]!));
  return { macd: line, signal: signalLine, histogram };
}

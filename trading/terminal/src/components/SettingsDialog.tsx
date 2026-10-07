"use client";

import { useEffect, useId, useRef, useState } from "react";

import { kronant } from "@/lib/colors";
import { candlePresets, presetOf, useSettings, type CandleColors, type Setting } from "@/lib/settings";
import { useSheet } from "@/lib/sheet";
import { playCloseSound, playFillSound, playWarningSound, unlockSound } from "@/lib/sound";

import { CandlesIcon, CloseIcon, SoundIcon, TradeIcon } from "./icons";
import { useDismiss } from "./useDismiss";

type Tab = "Chart" | "Sounds" | "Trading";

const tabs: { tab: Tab; Icon: (props: { className?: string }) => React.ReactNode }[] = [
  { tab: "Chart", Icon: CandlesIcon },
  { tab: "Sounds", Icon: SoundIcon },
  { tab: "Trading", Icon: TradeIcon },
];

// How long resetting waits for the second click.
const confirmResetMs = 4_000;

/**
 * The trader's settings for the terminal (ADR 0055): the chart's candle colors and what it shows, the sounds and
 * their volume, and whether the order panel asks before an order goes. Every change applies at once and is saved on
 * the trader's login, so it follows them to every device (ADR 0052).
 */
export function SettingsDialog() {
  const close = useSheet((s) => s.close);
  const ref = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const [tab, setTab] = useState<Tab>("Chart");
  useDismiss(true, ref, close);

  return (
    <div className="fixed inset-0 z-30 flex animate-fade items-center justify-center bg-background/75 backdrop-blur-sm sm:p-4">
      <div
        ref={ref}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="flex h-full w-full animate-pop flex-col overflow-hidden border-border bg-panel shadow-float sm:h-auto sm:max-h-[min(50rem,94vh)] sm:max-w-xl sm:rounded-2xl sm:border"
      >
        <div className="flex items-start justify-between gap-3 px-6 pt-5">
          <div className="flex flex-col gap-1">
            <h2 id={titleId} className="font-serif text-3xl leading-tight">
              Settings
            </h2>
            <p className="text-sm text-muted">Saved on your login, so they follow you to every device.</p>
          </div>
          <button
            type="button"
            aria-label="Close"
            onClick={close}
            className="flex size-8 shrink-0 items-center justify-center rounded-lg text-muted transition-colors duration-150 hover:bg-raised hover:text-foreground"
          >
            <CloseIcon className="size-4" />
          </button>
        </div>

        <div role="tablist" aria-label="Settings" className="mx-6 mt-4 grid grid-cols-3 gap-1 rounded-lg bg-background p-1 text-sm">
          {tabs.map(({ tab: t, Icon }) => (
            <button
              key={t}
              type="button"
              role="tab"
              aria-selected={t === tab}
              onClick={() => setTab(t)}
              className={`flex items-center justify-center gap-2 rounded-md py-1.5 font-medium transition duration-150 active:translate-y-px ${t === tab ? "bg-raised text-foreground shadow-card" : "text-muted hover:text-foreground"}`}
            >
              <Icon className="size-4" />
              {t}
            </button>
          ))}
        </div>

        <div role="tabpanel" aria-label={tab} className="min-h-0 flex-1 overflow-y-auto px-6 py-2">
          {tab === "Chart" && <ChartSettings />}
          {tab === "Sounds" && <SoundSettings />}
          {tab === "Trading" && <TradingSettings />}
        </div>

        <div className="flex items-center justify-between gap-3 border-t border-border px-6 py-4">
          <ResetButton />
          <button
            type="button"
            onClick={close}
            className="h-10 rounded-lg bg-accent px-5 text-sm font-semibold text-accent-foreground transition duration-150 hover:brightness-110 active:translate-y-px"
          >
            Done
          </button>
        </div>
      </div>
    </div>
  );
}

function ChartSettings() {
  return (
    <>
      <CandleColorsSetting />
      <SwitchRow setting="chartVolume" title="Volume under the candles" description="The number of prices in each candle, as faint bars along the bottom." />
      <SwitchRow setting="chartGrid" title="Grid lines" description="Faint lines behind the candles at each price and time on the axes." />
      <SwitchRow setting="chartAsk" title="Ask price line" description="The candles show the bid. This adds a dashed line at the ask, where a buy opens." />
      <SwitchRow setting="chartTrades" title="Trades on the chart" description="Arrows where your positions opened and closed, joined by a line." />
    </>
  );
}

function SoundSettings() {
  const volume = useSettings((s) => s.soundVolume);
  const changeVolume = useSettings((s) => s.changeSoundVolume);
  const id = useId();
  return (
    <>
      <Row
        title="Volume"
        titleId={`${id}-volume`}
        description="For every sound below. The sounds are made in the browser, so the computer's own volume counts too."
      >
        <span className="flex items-center gap-3">
          <input
            type="range"
            min={0}
            max={100}
            step={5}
            value={volume}
            aria-labelledby={`${id}-volume`}
            onChange={(e) => changeVolume(Number(e.target.value))}
            className="w-28 accent-[var(--accent)]"
          />
          <span className="w-9 text-right font-mono text-xs tabular-nums">{volume}%</span>
          <button
            type="button"
            onClick={() => {
              unlockSound();
              playFillSound();
            }}
            className="h-8 rounded-lg border border-border bg-raised px-2.5 text-xs font-medium transition duration-150 hover:border-muted active:translate-y-px"
          >
            Play
          </button>
        </span>
      </Row>
      <SwitchRow setting="fillSound" title="Sound on fills" description="Two short rising tones when an order is filled." play={playFillSound} />
      <SwitchRow
        setting="closeSound"
        title="Sound on closes"
        description="Two soft falling tones when a position closes, also at a stop loss, a take profit or a loss limit."
        play={playCloseSound}
      />
      <SwitchRow
        setting="warningSound"
        title="Sound on warnings"
        description="A longer signal with each warning about the account's rules, such as a loss limit getting close."
        play={playWarningSound}
      />
    </>
  );
}

function TradingSettings() {
  return (
    <SwitchRow
      setting="confirmOrders"
      title="Ask before placing an order"
      description="The order panel shows the order and its stops, and sends it when you confirm. Orders from the chart's right-click menu go at once."
    />
  );
}

/** A setting with its explanation on the left and the control on the right. */
function Row({ title, titleId, description, children }: { title: string; titleId: string; description: string; children: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-6 border-b border-border py-3.5 last:border-b-0">
      <div className="flex min-w-0 flex-col gap-0.5">
        <span id={titleId} className="text-sm font-medium">
          {title}
        </span>
        <span className="text-xs leading-relaxed text-muted">{description}</span>
      </div>
      <span className="shrink-0">{children}</span>
    </div>
  );
}

/** A choice turned on or off. A sound plays once when it is turned on, so the trader hears it and the browser allows it later. */
function SwitchRow({ setting, title, description, play }: { setting: Setting; title: string; description: string; play?: () => void }) {
  const on = useSettings((s) => s[setting]);
  const change = useSettings((s) => s.change);
  const id = useId();
  const toggle = () => {
    change(setting, !on);
    if (!on && play) {
      unlockSound();
      play();
    }
  };

  return (
    <Row title={title} titleId={id} description={description}>
      <button
        type="button"
        role="switch"
        aria-checked={on}
        aria-labelledby={id}
        onClick={toggle}
        className={`relative flex h-6 w-11 items-center rounded-full transition-colors duration-150 ${on ? "bg-accent" : "bg-border"}`}
      >
        <span className={`absolute left-1 size-4 rounded-full bg-foreground shadow transition-transform duration-150 ease-out-soft ${on ? "translate-x-5" : ""}`} />
      </button>
    </Row>
  );
}

/** The candles' colors: one of the ready-made pairs, or the trader's own, with a few candles that show them. */
function CandleColorsSetting() {
  const candles = useSettings((s) => s.candles);
  const changeCandles = useSettings((s) => s.changeCandles);
  const preset = presetOf(candles);
  // The trader's own pair shows its color fields, also before it differs from a ready-made one.
  const [own, setOwn] = useState(preset === null);
  const id = useId();

  return (
    <div className="flex flex-col gap-3 border-b border-border pt-2 pb-4">
      <div className="flex flex-col gap-0.5">
        <span id={id} className="text-sm font-medium">
          Candle colors
        </span>
        <span className="text-xs leading-relaxed text-muted">For rising and falling candles, their volume and MACD. Buy and sell keep their colors.</span>
      </div>
      <CandlePreview colors={candles} />
      <div role="radiogroup" aria-labelledby={id} className="grid grid-cols-2 gap-2 sm:grid-cols-3">
        {candlePresets.map((p) => (
          <ColorChoice
            key={p.name}
            name={p.name}
            colors={p.colors}
            checked={!own && preset === p.name}
            onChoose={() => {
              setOwn(false);
              changeCandles(p.colors);
            }}
          />
        ))}
        <ColorChoice name="Your own" colors={candles} checked={own || preset === null} onChoose={() => setOwn(true)} />
      </div>
      {(own || preset === null) && (
        <div className="flex flex-wrap gap-4 text-sm">
          <ColorField label="Rising" value={candles.up} onChange={(up) => changeCandles({ ...candles, up })} />
          <ColorField label="Falling" value={candles.down} onChange={(down) => changeCandles({ ...candles, down })} />
        </div>
      )}
    </div>
  );
}

function ColorChoice({ name, colors, checked, onChoose }: { name: string; colors: CandleColors; checked: boolean; onChoose: () => void }) {
  return (
    <button
      type="button"
      role="radio"
      aria-checked={checked}
      onClick={onChoose}
      className={`flex items-center gap-2.5 rounded-lg border px-3 py-2 text-left text-xs font-medium transition-colors duration-150 ${checked ? "border-accent bg-accent/10 text-foreground" : "border-border text-muted hover:border-muted hover:text-foreground"}`}
    >
      <span aria-hidden="true" className="flex items-end gap-0.5">
        <span className="h-4 w-1.5 rounded-[1px]" style={{ backgroundColor: colors.up }} />
        <span className="h-2.5 w-1.5 rounded-[1px]" style={{ backgroundColor: colors.down }} />
      </span>
      {name}
    </button>
  );
}

function ColorField({ label, value, onChange }: { label: string; value: string; onChange: (color: string) => void }) {
  return (
    <label className="flex items-center gap-2">
      <input
        type="color"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="h-8 w-10 cursor-pointer rounded-md border border-border bg-raised p-0.5"
      />
      <span>{label}</span>
      <span className="font-mono text-xs text-muted uppercase">{value}</span>
    </label>
  );
}

// A made-up stretch of closes, rising and falling in turn within a range, so each candle is tall enough to show its
// color. Each candle opens at the close before it.
const sampleCloses = [50, 54, 51, 47, 52, 57, 55, 50, 46, 49, 54, 58, 55, 52, 56, 60, 57, 53, 49, 52, 56, 59, 62, 58, 61, 64];
const sample = sampleCloses.map((close, i) => {
  const open = i === 0 ? 47 : sampleCloses[i - 1];
  return { open, close, high: Math.max(open, close) + 1 + (i % 3), low: Math.min(open, close) - 1 - ((i + 1) % 3) };
});
const sampleTop = Math.max(...sample.map((c) => c.high));
const sampleBottom = Math.min(...sample.map((c) => c.low));

// The preview's size in its own units, wide and low like the chart.
const previewStep = 20;
const previewHeight = 84;

function CandlePreview({ colors }: { colors: CandleColors }) {
  const y = (price: number) => 6 + ((sampleTop - price) / (sampleTop - sampleBottom)) * (previewHeight - 12);
  return (
    <svg
      role="img"
      aria-label="Candles in the chosen colors"
      viewBox={`0 0 ${previewStep * sample.length} ${previewHeight}`}
      className="w-full rounded-lg border border-border"
      style={{ backgroundColor: kronant.panel }}
    >
      {sample.map(({ open, close, high, low }, i) => {
        const color = close >= open ? colors.up : colors.down;
        const x = i * previewStep + previewStep / 2;
        return (
          <g key={i}>
            <line x1={x} x2={x} y1={y(high)} y2={y(low)} stroke={color} strokeWidth={1.5} />
            <rect x={x - 6} y={y(Math.max(open, close))} width={12} height={Math.max(2, Math.abs(y(open) - y(close)))} rx={1} fill={color} />
          </g>
        );
      })}
    </svg>
  );
}

/** Every setting back to its default, on a second click, so one click never wipes the trader's own colors. */
function ResetButton() {
  const reset = useSettings((s) => s.reset);
  const [confirming, setConfirming] = useState(false);
  useEffect(() => {
    if (!confirming) {
      return;
    }

    const timer = setTimeout(() => setConfirming(false), confirmResetMs);
    return () => clearTimeout(timer);
  }, [confirming]);

  return (
    <button
      type="button"
      onClick={() => {
        if (confirming) {
          reset();
          setConfirming(false);
        } else {
          setConfirming(true);
        }
      }}
      className={`text-sm transition-colors duration-150 ${confirming ? "font-medium text-warning" : "text-muted hover:text-foreground"}`}
    >
      {confirming ? "Click again to reset everything" : "Reset to defaults"}
    </button>
  );
}

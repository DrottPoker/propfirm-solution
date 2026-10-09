"use client";

import { useEffect, useId, useRef, useState } from "react";

import { askForNotifications, notificationsSupported } from "@/lib/notify";
import { timeZoneName } from "@/lib/format";
import { candlePresets, presetOf, themeChoices, timeZoneChoices, useSettings, type CandleColors, type Setting } from "@/lib/settings";
import { useSheet } from "@/lib/sheet";
import { playCloseSound, playFillSound, playWarningSound, unlockSound } from "@/lib/sound";
import { useConfirmOrders } from "@/lib/ticketStore";
import { useTimeZone } from "@/lib/timeZone";

import { CandlesIcon, CloseIcon, InfoIcon, SoundIcon, SunIcon, TradeIcon } from "./icons";
import { Tooltip } from "./Tooltip";
import { useDismiss } from "./useDismiss";

type Tab = "Display" | "Chart" | "Sounds" | "Trading";

const tabs: { tab: Tab; Icon: (props: { className?: string }) => React.ReactNode }[] = [
  { tab: "Display", Icon: SunIcon },
  { tab: "Chart", Icon: CandlesIcon },
  { tab: "Sounds", Icon: SoundIcon },
  { tab: "Trading", Icon: TradeIcon },
];

// How long resetting waits for the second click.
const confirmResetMs = 4_000;

/**
 * The trader's settings for the terminal (ADR 0055): the theme and the time zone, the chart's candle colors and what it
 * shows, the sounds and notifications, whether orders and closes ask first, and buying and selling on the chart
 * (ADR 0058). Every choice says what it does in one line. Every change applies at once and is saved on the trader's
 * login, so it follows them to every device (ADR 0052).
 */
export function SettingsDialog() {
  const close = useSheet((s) => s.close);
  const ref = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const [tab, setTab] = useState<Tab>("Display");
  useDismiss(true, ref, close);

  return (
    // A fixed height, so the dialog does not move when a tab with more or fewer settings is chosen.
    <div className="fixed inset-0 z-30 flex animate-fade items-center justify-center bg-background/75 backdrop-blur-sm sm:p-4">
      <div
        ref={ref}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="flex h-full w-full animate-pop flex-col overflow-hidden border-border bg-panel shadow-float sm:h-[min(42rem,92vh)] sm:max-w-xl sm:rounded-2xl sm:border"
      >
        <div className="flex items-start justify-between gap-3 px-6 pt-5">
          <div className="flex flex-col gap-1">
            <h2 id={titleId} className="text-xl font-semibold">
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

        <div role="tablist" aria-label="Settings" className="mx-6 mt-4 grid grid-cols-4 gap-1 rounded-lg bg-background p-1 text-sm">
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
          {tab === "Display" && <DisplaySettings />}
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

/** The theme, the time zone every time is shown in, and whether prices flash their colour as they move. */
function DisplaySettings() {
  const theme = useSettings((s) => s.theme);
  const changeTheme = useSettings((s) => s.changeTheme);
  const timeZone = useSettings((s) => s.timeZone);
  const changeTimeZone = useSettings((s) => s.changeTimeZone);
  const shownZone = useTimeZone();
  const id = useId();
  return (
    <>
      <Row title="Theme" titleId={`${id}-theme`} description="Dark is Kronant's own. As the computer follows its setting.">
        <Choices labelledBy={`${id}-theme`} value={theme} options={themeChoices} onChange={changeTheme} />
      </Row>
      <Row
        title="Time zone"
        titleId={`${id}-zone`}
        description={`Every time is shown in ${timeZoneName(shownZone)} now.`}
        help="The account's is the one its trading day and the firm's portal follow."
      >
        <Choices labelledBy={`${id}-zone`} value={timeZone} options={timeZoneChoices} onChange={changeTimeZone} />
      </Row>
      <SwitchRow setting="priceTicks" title="Colour prices as they move" description="Green for a moment when a price rises, red when it falls." />
    </>
  );
}

function ChartSettings() {
  return (
    <>
      <CandleColorsSetting />
      <SwitchRow setting="chartVolume" title="Volume under the candles" description="Faint bars of how many prices each candle had." />
      <SwitchRow setting="chartGrid" title="Grid lines" description="Faint lines at the prices and times on the axes." />
      <SwitchRow
        setting="chartAsk"
        title="Ask price line"
        description="A dashed line at the ask, where a buy opens."
        help="The candles show the bid, the price a sell closes at."
      />
      <SwitchRow setting="chartTrades" title="Trades on the chart" description="Arrows where your positions opened and closed." />
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
        description="For every sound below."
        help="The sounds are made in the browser, so the computer's own volume counts too."
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
          <span className="w-9 text-right text-xs tabular-nums">{volume}%</span>
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
      <SwitchRow setting="fillSound" title="Sound on fills" description="Two short rising tones." play={playFillSound} />
      <SwitchRow
        setting="closeSound"
        title="Sound on closes"
        description="Two soft falling tones, also at a stop loss or a limit."
        play={playCloseSound}
      />
      <SwitchRow
        setting="warningSound"
        title="Sound on warnings"
        description="A longer signal when a rule needs your attention."
        play={playWarningSound}
      />
      <NotificationsRow />
    </>
  );
}

function TradingSettings() {
  // Until the trader chooses, orders ask as the firm's profile says (ADR 0058), and the switch shows that.
  const confirmOrders = useConfirmOrders();
  return (
    <>
      <SwitchRow
        setting="confirmOrders"
        value={confirmOrders}
        title="Ask before placing an order"
        description="From the order panel or the chart, sent when you confirm."
      />
      <SwitchRow
        setting="confirmCloses"
        title="Ask before closing a position"
        description="Close asks for a second press, as Close all does."
      />
      <SwitchRow
        setting="chartTrading"
        title="Buy and sell on the chart"
        description="Market orders from the chart's corner, also in full screen."
        help="At the order panel's volume, without stops. Set the stops afterwards by dragging on the chart."
      />
    </>
  );
}

/**
 * The computer's own notifications while the terminal is in the background (ADR 0058). Turning them on asks the
 * browser for leave, and a browser that refuses leaves them off, with where to allow them.
 */
function NotificationsRow() {
  const on = useSettings((s) => s.notifications);
  const change = useSettings((s) => s.change);
  const [blocked, setBlocked] = useState(false);
  const id = useId();
  const supported = notificationsSupported();

  const toggle = async () => {
    if (on) {
      change("notifications", false);
      return;
    }

    const allowed = await askForNotifications();
    setBlocked(!allowed);
    if (allowed) {
      change("notifications", true);
    }
  };

  return (
    <Row
      title="Notifications from the computer"
      titleId={id}
      description={
        !supported
          ? "This browser cannot show notifications."
          : blocked
            ? "The browser blocks notifications for the terminal. Allow them in the browser's settings for this site, then turn this on."
            : "While the terminal is in the background."
      }
    >
      <SwitchButton on={on && supported} disabled={!supported} labelledBy={id} onClick={() => void toggle()} />
    </Row>
  );
}

/** A setting with its line of explanation on the left, more behind an info button, and the control on the right. */
function Row({
  title,
  titleId,
  description,
  help,
  children,
}: {
  title: string;
  titleId: string;
  description: string;
  help?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="flex items-center justify-between gap-6 border-b border-border py-3.5 last:border-b-0">
      <div className="flex min-w-0 flex-col gap-0.5">
        <span className="flex items-center gap-1.5">
          <span id={titleId} className="text-sm font-medium">
            {title}
          </span>
          {help && (
            <Tooltip content={help} focusable className="rounded">
              <InfoIcon className="size-3.5 text-muted" />
            </Tooltip>
          )}
        </span>
        <span className="text-xs leading-relaxed text-muted">{description}</span>
      </div>
      <span className="shrink-0">{children}</span>
    </div>
  );
}

/** A few choices side by side, such as the theme. */
function Choices<T extends string>({
  labelledBy,
  value,
  options,
  onChange,
}: {
  labelledBy: string;
  value: T;
  options: readonly { value: T; name: string }[];
  onChange: (value: T) => void;
}) {
  return (
    <div role="radiogroup" aria-labelledby={labelledBy} className="flex flex-col gap-0.5 rounded-lg bg-background p-0.5 text-xs sm:flex-row">
      {options.map((o) => (
        <button
          key={o.value}
          type="button"
          role="radio"
          aria-checked={o.value === value}
          onClick={() => onChange(o.value)}
          className={`rounded-md px-2.5 py-1 font-medium whitespace-nowrap transition duration-150 ${o.value === value ? "bg-raised text-foreground shadow-card" : "text-muted hover:text-foreground"}`}
        >
          {o.name}
        </button>
      ))}
    </div>
  );
}

/** A choice turned on or off. A sound plays once when it is turned on, so the trader hears it and the browser allows it later. */
function SwitchRow({
  setting,
  value,
  title,
  description,
  help,
  play,
}: {
  setting: Setting;
  /** What the choice is in effect, when it is not simply the stored one. */
  value?: boolean;
  title: string;
  description: string;
  help?: string;
  play?: () => void;
}) {
  const stored = useSettings((s) => s[setting]);
  const on = value ?? stored;
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
    <Row title={title} titleId={id} description={description} help={help}>
      <SwitchButton on={on} labelledBy={id} onClick={toggle} />
    </Row>
  );
}

function SwitchButton({ on, disabled = false, labelledBy, onClick }: { on: boolean; disabled?: boolean; labelledBy: string; onClick: () => void }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={on}
      aria-labelledby={labelledBy}
      disabled={disabled}
      onClick={onClick}
      className={`relative flex h-6 w-11 items-center rounded-full transition-colors duration-150 disabled:opacity-40 ${on ? "bg-accent" : "bg-border"}`}
    >
      <span className={`absolute left-1 size-4 rounded-full bg-foreground shadow transition-transform duration-150 ease-out-soft ${on ? "translate-x-5" : ""}`} />
    </button>
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
      style={{ backgroundColor: "var(--panel)" }}
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

// The terminal's icons, from Phosphor (ADR 0047) like the firm's portal, drawn in the text color. Icons for things,
// such as the phone's tabs, are duotone, a lighter fill behind the line that gives them depth; small symbols such as a
// plus or a caret are bold, to stay crisp at small sizes. Decorative, so hidden from screen readers.

import {
  ArrowCounterClockwiseIcon,
  ArrowLeftIcon as PhArrowLeftIcon,
  ArrowSquareOutIcon,
  ArrowsDownUpIcon,
  ArrowUpRightIcon,
  BellIcon as PhBellIcon,
  BookOpenIcon,
  CaretDownIcon,
  CheckIcon as PhCheckIcon,
  DotsThreeIcon,
  DownloadSimpleIcon,
  KeyboardIcon as PhKeyboardIcon,
  LayoutIcon as PhLayoutIcon,
  LightningIcon,
  MonitorIcon,
  PencilSimpleLineIcon,
  QuestionIcon,
  SunIcon as PhSunIcon,
  TextTIcon,
  WifiSlashIcon,
  ClockCountdownIcon,
  CopyIcon as PhCopyIcon,
  ChartBarIcon,
  ChartLineUpIcon,
  CheckCircleIcon,
  CornersInIcon,
  CornersOutIcon,
  EyeIcon as PhEyeIcon,
  EyeSlashIcon as PhEyeSlashIcon,
  GearSixIcon,
  InfoIcon as PhInfoIcon,
  LineSegmentIcon,
  ListBulletsIcon,
  ListChecksIcon,
  LockSimpleIcon,
  MagnifyingGlassIcon,
  MinusIcon as PhMinusIcon,
  MoonIcon,
  PlusIcon as PhPlusIcon,
  PrinterIcon as PhPrinterIcon,
  ShieldCheckIcon,
  RectangleIcon as PhRectangleIcon,
  SignOutIcon,
  SpeakerHighIcon,
  StackIcon,
  StackMinusIcon,
  StarIcon as PhStarIcon,
  TrashIcon as PhTrashIcon,
  WarningCircleIcon,
  WarningIcon as PhWarningIcon,
  XIcon,
} from "@phosphor-icons/react/ssr";
import type { Icon as PhosphorIcon } from "@phosphor-icons/react";

type IconProps = { className?: string };

function icon(Glyph: PhosphorIcon, weight: "duotone" | "bold", defaultClassName = "size-4") {
  function Drawn({ className = defaultClassName }: IconProps) {
    return <Glyph weight={weight} aria-hidden="true" className={`shrink-0 ${className}`} />;
  }

  return Drawn;
}

export const SearchIcon = icon(MagnifyingGlassIcon, "bold");
export const ChevronDownIcon = icon(CaretDownIcon, "bold");
export const ArrowLeftIcon = icon(PhArrowLeftIcon, "bold");
export const PlusIcon = icon(PhPlusIcon, "bold");
export const MinusIcon = icon(PhMinusIcon, "bold");
export const ExpandIcon = icon(CornersOutIcon, "bold");
export const CollapseIcon = icon(CornersInIcon, "bold");
export const InfoIcon = icon(PhInfoIcon, "duotone", "size-3.5");
export const LogOutIcon = icon(SignOutIcon, "bold");
export const EyeIcon = icon(PhEyeIcon, "bold");
export const EyeSlashIcon = icon(PhEyeSlashIcon, "bold");
export const TradeIcon = icon(ArrowsDownUpIcon, "duotone");
export const ListIcon = icon(ListBulletsIcon, "duotone");
export const LayersIcon = icon(StackIcon, "duotone");
export const VolumeBarsIcon = icon(ChartBarIcon, "duotone");
export const SoundIcon = icon(SpeakerHighIcon, "duotone");
export const SuccessIcon = icon(CheckCircleIcon, "duotone");
export const ErrorIcon = icon(WarningCircleIcon, "duotone");
export const WarningIcon = icon(PhWarningIcon, "duotone");
export const RulesIcon = icon(ListChecksIcon, "duotone");
export const OldPriceIcon = icon(ClockCountdownIcon, "bold", "size-3.5");
export const PrintIcon = icon(PhPrinterIcon, "duotone");
export const CopyIcon = icon(PhCopyIcon, "duotone");
export const ProofIcon = icon(ShieldCheckIcon, "duotone");
export const ClosedIcon = icon(StackMinusIcon, "duotone");
export const MarketClosedIcon = icon(MoonIcon, "duotone", "size-3.5");
export const CloseIcon = icon(XIcon, "bold");
export const IndicatorsIcon = icon(ChartLineUpIcon, "duotone");
export const TrendLineIcon = icon(LineSegmentIcon, "bold");
export const RectangleIcon = icon(PhRectangleIcon, "duotone");
export const TrashIcon = icon(PhTrashIcon, "duotone");
export const LockIcon = icon(LockSimpleIcon, "duotone");
export const SettingsIcon = icon(GearSixIcon, "duotone");
export const ArrowIcon = icon(ArrowUpRightIcon, "bold");
export const TextIcon = icon(TextTIcon, "bold");
export const DrawIcon = icon(PencilSimpleLineIcon, "duotone");
export const BellIcon = icon(PhBellIcon, "duotone");
export const MoreIcon = icon(DotsThreeIcon, "bold");
export const HelpIcon = icon(QuestionIcon, "duotone");
export const KeyboardIcon = icon(PhKeyboardIcon, "duotone");
export const LayoutIcon = icon(PhLayoutIcon, "duotone");
export const SunIcon = icon(PhSunIcon, "duotone");
export const SystemThemeIcon = icon(MonitorIcon, "duotone");
export const QuickTradeIcon = icon(LightningIcon, "duotone");
export const BookIcon = icon(BookOpenIcon, "duotone");
export const ExternalIcon = icon(ArrowSquareOutIcon, "bold", "size-3.5");
export const OfflineIcon = icon(WifiSlashIcon, "duotone");
export const DownloadIcon = icon(DownloadSimpleIcon, "bold");
export const ResetIcon = icon(ArrowCounterClockwiseIcon, "bold");
export const CheckIcon = icon(PhCheckIcon, "bold");

/** A vertical line with a dot, for the vertical line tool, drawn like the horizontal one. */
export function VerticalLineIcon({ className = "size-4" }: IconProps) {
  return (
    <svg viewBox="0 0 256 256" fill="none" stroke="currentColor" strokeWidth={20} strokeLinecap="round" aria-hidden="true" className={`shrink-0 ${className}`}>
      <path d="M128 24v208" />
      <circle cx="128" cy="128" r="18" fill="currentColor" />
    </svg>
  );
}

/** Levels between two points, for the Fibonacci retracement. */
export function FibonacciIcon({ className = "size-4" }: IconProps) {
  return (
    <svg viewBox="0 0 256 256" fill="none" stroke="currentColor" strokeWidth={16} strokeLinecap="round" aria-hidden="true" className={`shrink-0 ${className}`}>
      <path d="M40 48h176M40 96h176M40 136h176M40 208h176" />
      <path d="M56 208L200 48" strokeDasharray="12 16" />
    </svg>
  );
}

/** A horizontal line across the chart with a level on it. Phosphor has none, so drawn here in its bold style. */
export function HorizontalLineIcon({ className = "size-4" }: IconProps) {
  return (
    <svg viewBox="0 0 256 256" fill="none" stroke="currentColor" strokeWidth={20} strokeLinecap="round" aria-hidden="true" className={`shrink-0 ${className}`}>
      <path d="M24 128h208" />
      <circle cx="128" cy="128" r="18" fill="currentColor" />
    </svg>
  );
}

/** A favorite's star: filled when the symbol is a favorite. */
export function StarIcon({ filled, className = "size-3.5" }: IconProps & { filled: boolean }) {
  return <PhStarIcon weight={filled ? "fill" : "bold"} aria-hidden="true" className={`shrink-0 ${className}`} />;
}

/** Two candles, for the chart. Phosphor has none, so drawn here in its duotone style. */
export function CandlesIcon({ className = "size-4" }: IconProps) {
  return (
    <svg
      viewBox="0 0 256 256"
      fill="none"
      stroke="currentColor"
      strokeWidth={16}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className={`shrink-0 ${className}`}
    >
      <rect x="56" y="72" width="64" height="96" rx="8" fill="currentColor" fillOpacity={0.2} />
      <rect x="136" y="96" width="64" height="80" rx="8" fill="currentColor" fillOpacity={0.2} />
      <path d="M88 32v40M88 168v56M168 48v48M168 176v32" />
    </svg>
  );
}

/** OHLC bars, for the chart type. */
export function BarsIcon({ className = "size-4" }: IconProps) {
  return (
    <svg viewBox="0 0 256 256" fill="none" stroke="currentColor" strokeWidth={16} strokeLinecap="round" aria-hidden="true" className={`shrink-0 ${className}`}>
      <path d="M80 40v176M48 72h32M80 176h32M176 64v144M144 168h32M176 104h32" />
    </svg>
  );
}

/** A line of closes, for the chart type. */
export function LineChartIcon({ className = "size-4" }: IconProps) {
  return (
    <svg viewBox="0 0 256 256" fill="none" stroke="currentColor" strokeWidth={16} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" className={`shrink-0 ${className}`}>
      <path d="M32 184l56-64 48 40 88-104" />
    </svg>
  );
}

/** Smoothed candles, for Heikin Ashi. */
export function HeikinAshiIcon({ className = "size-4" }: IconProps) {
  return (
    <svg viewBox="0 0 256 256" fill="none" stroke="currentColor" strokeWidth={16} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" className={`shrink-0 ${className}`}>
      <rect x="48" y="112" width="48" height="72" rx="8" fill="currentColor" fillOpacity={0.2} />
      <rect x="104" y="88" width="48" height="72" rx="8" fill="currentColor" fillOpacity={0.2} />
      <rect x="160" y="56" width="48" height="72" rx="8" fill="currentColor" fillOpacity={0.2} />
      <path d="M72 96v16M128 64v24M184 32v24M184 128v24" />
    </svg>
  );
}

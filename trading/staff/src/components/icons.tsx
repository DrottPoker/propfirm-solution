// The staff panel's icons, from Phosphor (ADR 0047) like the terminal and the portal, drawn in the text color. Icons
// for places, such as the menu's, are duotone; small symbols such as a caret are bold. Decorative, so hidden from
// screen readers.

import {
  ArrowsClockwiseIcon,
  ArrowSquareOutIcon,
  BuildingsIcon,
  CaretRightIcon,
  ChartBarIcon,
  CheckCircleIcon,
  ClockIcon,
  CopyIcon as PhCopyIcon,
  CpuIcon,
  GaugeIcon,
  KeyIcon as PhKeyIcon,
  ListIcon,
  MagnifyingGlassIcon,
  PlusIcon as PhPlusIcon,
  ScalesIcon,
  SignOutIcon,
  SquaresFourIcon,
  WarningCircleIcon,
  WarningIcon as PhWarningIcon,
  WaveformIcon,
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

export const OverviewIcon = icon(SquaresFourIcon, "duotone");
export const ServersIcon = icon(BuildingsIcon, "duotone");
export const FeedIcon = icon(WaveformIcon, "duotone");
export const InstrumentsIcon = icon(ChartBarIcon, "duotone");
export const ExposureIcon = icon(ScalesIcon, "duotone");
export const EngineIcon = icon(CpuIcon, "duotone");
export const GaugeGlyph = icon(GaugeIcon, "duotone");
export const SearchIcon = icon(MagnifyingGlassIcon, "bold");
export const ChevronRightIcon = icon(CaretRightIcon, "bold");
export const ExternalIcon = icon(ArrowSquareOutIcon, "bold", "size-3.5");
export const LogOutIcon = icon(SignOutIcon, "bold");
export const MenuIcon = icon(ListIcon, "bold", "size-5");
export const CloseIcon = icon(XIcon, "bold");
export const PlusIcon = icon(PhPlusIcon, "bold");
export const CopyIcon = icon(PhCopyIcon, "duotone", "size-3.5");
export const KeyIcon = icon(PhKeyIcon, "duotone");
export const RetryIcon = icon(ArrowsClockwiseIcon, "bold");
export const WarningIcon = icon(PhWarningIcon, "duotone", "size-5");
export const LateIcon = icon(ClockIcon, "duotone", "size-5");
export const ErrorIcon = icon(WarningCircleIcon, "duotone", "size-5");
export const SuccessIcon = icon(CheckCircleIcon, "duotone", "size-5");

/** Kronant's mark, the crown on a coin, in brass, as in the terminal. */
export function KronantMark({ className = "size-7" }: IconProps) {
  return (
    <svg viewBox="0 0 28 28" fill="none" aria-hidden="true" className={`shrink-0 ${className}`}>
      <circle cx="14" cy="14" r="12.5" stroke="var(--brass)" strokeWidth="1.5" />
      <path d="M8.5 17.5h11l1-7-3.6 2.8L14 8.5l-2.9 4.8-3.6-2.8z" stroke="var(--brass)" strokeWidth="1.4" strokeLinejoin="round" />
    </svg>
  );
}

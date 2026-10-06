// The terminal's icons, from Phosphor (ADR 0047) like the firm's portal, drawn in the text color. Icons for things,
// such as the phone's tabs, are duotone, a lighter fill behind the line that gives them depth; small symbols such as a
// plus or a caret are bold, to stay crisp at small sizes. Decorative, so hidden from screen readers.

import {
  ArrowLeftIcon as PhArrowLeftIcon,
  ArrowsDownUpIcon,
  CaretDownIcon,
  ChartBarIcon,
  CheckCircleIcon,
  CornersInIcon,
  CornersOutIcon,
  EyeIcon as PhEyeIcon,
  EyeSlashIcon as PhEyeSlashIcon,
  InfoIcon as PhInfoIcon,
  ListBulletsIcon,
  MagnifyingGlassIcon,
  MinusIcon as PhMinusIcon,
  PlusIcon as PhPlusIcon,
  SignOutIcon,
  SpeakerHighIcon,
  StackIcon,
  StackMinusIcon,
  StarIcon as PhStarIcon,
  WarningCircleIcon,
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
export const ClosedIcon = icon(StackMinusIcon, "duotone");

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

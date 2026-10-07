// The portal's icons, from Phosphor (ADR 0047), drawn in the text color. Icons for places and things are duotone, a
// lighter fill behind the line that gives them depth; small symbols such as a check or a close are bold, to stay crisp.
// They are decoration: what they mean is always in text beside them.

import {
  ArrowSquareOutIcon,
  BuildingsIcon,
  CaretRightIcon,
  ChartLineUpIcon,
  ChatsCircleIcon,
  CheckIcon as PhCheckIcon,
  ChecksIcon,
  ClockIcon as PhClockIcon,
  CodeIcon as PhCodeIcon,
  CommandIcon as PhCommandIcon,
  ConfettiIcon as PhConfettiIcon,
  CopyIcon as PhCopyIcon,
  CreditCardIcon,
  EnvelopeSimpleIcon,
  EyeIcon as PhEyeIcon,
  EyeSlashIcon as PhEyeSlashIcon,
  FileIcon as PhFileIcon,
  FlagPennantIcon,
  GlobeHemisphereWestIcon,
  HandCoinsIcon,
  HardDrivesIcon,
  InfoIcon as PhInfoIcon,
  ListIcon,
  LockSimpleIcon,
  MagnifyingGlassIcon,
  MonitorPlayIcon,
  PaletteIcon as PhPaletteIcon,
  PaperclipIcon as PhPaperclipIcon,
  PauseIcon as PhPauseIcon,
  PencilSimpleIcon,
  PlayIcon as PhPlayIcon,
  PlusIcon as PhPlusIcon,
  PrinterIcon,
  PulseIcon,
  ReceiptIcon as PhReceiptIcon,
  RocketLaunchIcon,
  SealCheckIcon,
  ShieldCheckIcon as PhShieldCheckIcon,
  ShoppingBagOpenIcon,
  SignOutIcon,
  SlidersHorizontalIcon,
  SparkleIcon as PhSparkleIcon,
  SquaresFourIcon,
  TagIcon as PhTagIcon,
  TrashIcon as PhTrashIcon,
  TrophyIcon as PhTrophyIcon,
  UploadSimpleIcon,
  UsersFourIcon,
  UsersThreeIcon,
  WarningIcon,
  WarningOctagonIcon,
  XCircleIcon,
  XIcon,
} from "@phosphor-icons/react/ssr";
import type { Icon as PhosphorIcon } from "@phosphor-icons/react";

type IconProps = { className?: string };

function icon(Glyph: PhosphorIcon, weight: "duotone" | "bold" | "regular" = "duotone") {
  function Drawn({ className = "size-4" }: IconProps) {
    return <Glyph weight={weight} aria-hidden="true" className={`shrink-0 ${className}`} />;
  }

  return Drawn;
}

export const OverviewIcon = icon(SquaresFourIcon);
export const AccountsIcon = icon(UsersThreeIcon);
export const PayoutIcon = icon(HandCoinsIcon);
export const BagIcon = icon(ShoppingBagOpenIcon);
export const GlobeIcon = icon(GlobeHemisphereWestIcon);
export const TagIcon = icon(PhTagIcon);
export const FlagIcon = icon(FlagPennantIcon);
export const PaletteIcon = icon(PhPaletteIcon);
export const CardIcon = icon(CreditCardIcon);
export const CodeIcon = icon(PhCodeIcon);
export const TeamIcon = icon(UsersFourIcon);
export const ReceiptIcon = icon(PhReceiptIcon);
export const FileCheckIcon = icon(SealCheckIcon);
export const ExternalIcon = icon(ArrowSquareOutIcon, "bold");
export const LogoutIcon = icon(SignOutIcon, "bold");
export const PlusIcon = icon(PhPlusIcon, "bold");
export const SearchIcon = icon(MagnifyingGlassIcon, "bold");
export const CloseIcon = icon(XIcon, "bold");
export const MenuIcon = icon(ListIcon, "bold");
export const CheckIcon = icon(PhCheckIcon, "bold");
export const DoubleCheckIcon = icon(ChecksIcon, "bold");
export const ShieldCheckIcon = icon(PhShieldCheckIcon);
export const AlertIcon = icon(WarningIcon);
export const CrossIcon = icon(XCircleIcon);
export const ClockIcon = icon(PhClockIcon);
export const PlayIcon = icon(PhPlayIcon);
export const LockIcon = icon(LockSimpleIcon);
export const CopyIcon = icon(PhCopyIcon);
export const MailIcon = icon(EnvelopeSimpleIcon);
export const UploadIcon = icon(UploadSimpleIcon);
export const InfoIcon = icon(PhInfoIcon);
export const TrashIcon = icon(PhTrashIcon);
export const ChevronRightIcon = icon(CaretRightIcon, "bold");
export const BuildingIcon = icon(BuildingsIcon);
export const FileIcon = icon(PhFileIcon);
export const PencilIcon = icon(PencilSimpleIcon);
export const PauseIcon = icon(PhPauseIcon);
export const RocketIcon = icon(RocketLaunchIcon);
export const ServerIcon = icon(HardDrivesIcon);
export const SupportIcon = icon(ChatsCircleIcon);
export const PaperclipIcon = icon(PhPaperclipIcon, "bold");
export const SettingsIcon = icon(SlidersHorizontalIcon);
export const CommandIcon = icon(PhCommandIcon, "bold");
export const SparkleIcon = icon(PhSparkleIcon);
export const TrophyIcon = icon(PhTrophyIcon);
export const ConfettiIcon = icon(PhConfettiIcon);
export const ChartIcon = icon(ChartLineUpIcon);
export const TerminalIcon = icon(MonitorPlayIcon);
export const EyeIcon = icon(PhEyeIcon, "bold");
export const EyeSlashIcon = icon(PhEyeSlashIcon, "bold");
export const IncidentIcon = icon(WarningOctagonIcon);
export const StatusIcon = icon(PulseIcon);
export const PrintIcon = icon(PrinterIcon);

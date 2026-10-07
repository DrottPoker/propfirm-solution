"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { motion } from "motion/react";
import { useId, useState, useSyncExternalStore } from "react";

import { useBranding } from "@/app/providers";

import { dailyPlaces, isSettingsPath, isUnder, liveOrBilling, settingsPlaces } from "@/lib/adminNav";
import type { FirmStatus, Me } from "@/lib/api/types";
import { initials } from "@/lib/dashboard";
import { useFirmIncidents, useFirmSupportSummary, useLogout, usePayoutSummary, useWaitingAccounts } from "@/lib/queries";

import { BillingNotice } from "./BillingNotice";
import { CommandPalette } from "./CommandPalette";
import { FirmName } from "./FirmName";
import {
  AccountsIcon,
  BagIcon,
  CloseIcon,
  CommandIcon,
  ExternalIcon,
  FlagIcon,
  IncidentIcon,
  LogoutIcon,
  MenuIcon,
  OverviewIcon,
  PayoutIcon,
  ReceiptIcon,
  RocketIcon,
  SearchIcon,
  SettingsIcon,
  SupportIcon,
  TagIcon,
} from "./icons";

type NavLink = {
  href: string;
  label: string;
  icon: (props: { className?: string }) => React.ReactNode;
  matches: (path: string) => boolean;
  badge?: "accounts" | "payouts" | "support" | "incidents";
};

const placeIcons: Record<string, NavLink["icon"]> = {
  "/admin": OverviewIcon,
  "/admin/accounts": AccountsIcon,
  "/admin/payouts": PayoutIcon,
  "/admin/support": SupportIcon,
  "/admin/incidents": IncidentIcon,
  "/admin/orders": BagIcon,
  "/admin/challenges": FlagIcon,
  "/admin/discounts": TagIcon,
  "/admin/billing": ReceiptIcon,
  "/admin/go-live": RocketIcon,
};

const badges: Record<string, NavLink["badge"]> = { "/admin/accounts": "accounts", "/admin/payouts": "payouts", "/admin/support": "support", "/admin/incidents": "incidents" };

/**
 * The daily work first, then the firm's settings together under one link, and Go live until the firm is live, Plan and
 * billing from then. Each settings page has the others as tabs.
 */
function navigation(status: FirmStatus): NavLink[] {
  const live = liveOrBilling(status);
  return [
    ...dailyPlaces.map((place) => ({
      href: place.href,
      label: place.label,
      icon: placeIcons[place.href],
      matches: place.href === "/admin" ? (path: string) => path === "/admin" : (path: string) => isUnder(path, place.href),
      badge: badges[place.href],
    })),
    { href: "/admin/settings", label: "Settings", icon: SettingsIcon, matches: isSettingsPath },
    {
      href: live.href,
      label: live.label,
      icon: placeIcons[live.href],
      matches: status === "Live" ? (path: string) => isUnder(path, "/admin/billing") : (path: string) => isUnder(path, "/admin/go-live") || isUnder(path, "/admin/billing"),
    },
  ];
}

// The keyboard does not change while the page is open.
const noChanges = () => () => {};

const statusLine: Record<FirmStatus, { text: string; dot: string }> = {
  Provisioning: { text: "Setting up", dot: "bg-muted" },
  Sandbox: { text: "Sandbox", dot: "bg-warning" },
  Live: { text: "Live", dot: "bg-profit" },
};

/**
 * The admin panel around every page: the firm and its status, the way around with what waits for the firm, and who is
 * logged in. On a phone the menu folds into a button at the top.
 */
export function AdminShell({ me, children }: { me: Me; children: React.ReactNode }) {
  const path = usePathname();
  const [menuOpen, setMenuOpen] = useState(false);
  const [openedOn, setOpenedOn] = useState(path);
  const [searching, setSearching] = useState(false);
  const menuId = useId();

  // A new page closes the menu on a phone.
  if (menuOpen && openedOn !== path) {
    setMenuOpen(false);
  }

  return (
    <div className="flex flex-1 flex-col md:flex-row">
      <aside aria-label="Admin menu" className="hidden w-60 shrink-0 flex-col gap-5 border-r border-border bg-panel px-3 py-4 md:sticky md:top-0 md:flex md:h-screen md:overflow-y-auto print:!hidden">
        <FirmMark />
        <SearchButton onOpen={() => setSearching(true)} />
        <Navigation path={path} />
        <Footer me={me} />
      </aside>

      <div className="border-b border-border bg-panel md:hidden print:hidden">
        <div className="flex items-center gap-3 px-4 py-2.5">
          <FirmMark />
          <button
            type="button"
            aria-label={menuOpen ? "Close the menu" : "Open the menu"}
            aria-expanded={menuOpen}
            aria-controls={menuId}
            onClick={() => {
              setOpenedOn(path);
              setMenuOpen(!menuOpen);
            }}
            className="ml-auto grid size-11 place-items-center rounded-lg border border-border bg-background"
          >
            {menuOpen ? <CloseIcon className="size-5" /> : <MenuIcon className="size-5" />}
          </button>
        </div>
        {menuOpen && (
          <div id={menuId} className="flex flex-col gap-4 border-t border-border px-3 py-3">
            <SearchButton
              onOpen={() => {
                setMenuOpen(false);
                setSearching(true);
              }}
            />
            <Navigation path={path} />
            <Footer me={me} />
          </div>
        )}
      </div>

      <div className="flex min-w-0 flex-1 flex-col">
        <BillingNotice />
        {isSettingsPath(path) && path !== "/admin/settings" && <SettingsNav path={path} />}
        {children}
      </div>
      <CommandPalette open={searching} onOpenChange={setSearching} />
    </div>
  );
}

/** The firm's logo, or its initial and name, and where it is on its way to live. */
function FirmMark() {
  const branding = useBranding();
  const status = statusLine[branding.status];
  const line = (
    <span className="flex items-center gap-1.5 whitespace-nowrap text-xs text-muted">
      <span aria-hidden="true" className={`size-1.5 rounded-full ${status.dot}`} />
      Admin · {status.text}
    </span>
  );

  // A logo can be wide, so the role and the status go under it.
  return branding.logoUrl ? (
    <Link href="/admin" className="flex min-w-0 flex-col items-start gap-1.5 px-2 py-1">
      <FirmName />
      {line}
    </Link>
  ) : (
    <Link href="/admin" className="flex min-w-0 items-center gap-2.5 px-2 py-1">
      <span aria-hidden="true" className="grid size-8 shrink-0 place-items-center rounded-lg bg-accent font-bold text-accent-foreground">
        {branding.name.trim().charAt(0).toUpperCase()}
      </span>
      <span className="flex min-w-0 flex-col leading-tight">
        <span className="truncate font-semibold">{branding.name}</span>
        {line}
      </span>
    </Link>
  );
}

// The current page's mark slides to it when another page opens.
function Navigation({ path }: { path: string }) {
  const { status } = useBranding();
  const payouts = usePayoutSummary();
  const waiting = useWaitingAccounts(3);
  const support = useFirmSupportSummary();
  const incidents = useFirmIncidents();
  const counts = {
    payouts: payouts.data?.toApprove.count ?? 0,
    accounts: waiting.data?.counts.awaitingFunding ?? 0,
    support: support.data?.open ?? 0,
    incidents: incidents.data?.open ?? 0,
  };
  const links = navigation(status);

  return (
    <nav aria-label="Admin" className="flex flex-col gap-0.5 text-sm">
      {links.map((link, index) => {
        const current = link.matches(path);
        const badge = link.badge ? counts[link.badge] : 0;
        // The settings and the way to live stand apart from the daily work.
        const apart = index === links.length - 2;
        return (
          <Link
            key={link.href}
            href={link.href}
            aria-current={current ? "page" : undefined}
            className={`relative flex items-center gap-2.5 rounded-lg px-2.5 py-2 transition-colors ${apart ? "mt-3" : ""} ${current ? "font-medium text-foreground" : "text-muted hover:bg-raised/50 hover:text-foreground"}`}
          >
            {current && (
              <motion.span layoutId="admin-nav-current" transition={{ type: "spring", stiffness: 500, damping: 40 }} className="absolute inset-0 rounded-lg bg-raised shadow-card">
                <span className="absolute inset-y-2 left-0 w-0.5 rounded-full bg-accent" />
              </motion.span>
            )}
            <link.icon className={`relative size-[1.1rem] shrink-0 ${current ? "text-accent" : ""}`} />
            <span className="relative flex-1">{link.label}</span>
            {badge > 0 && (
              <span className="relative min-w-5 rounded-full bg-accent px-1.5 text-center text-[11px] font-semibold text-accent-foreground">
                {badge}
                <span className="sr-only"> waiting for you</span>
              </span>
            )}
          </Link>
        );
      })}
    </nav>
  );
}

/** Opens the search over the admin panel's pages and accounts, which Ctrl+K opens too, or Cmd+K on a Mac. */
function SearchButton({ onOpen }: { onOpen: () => void }) {
  const mac = useSyncExternalStore(
    noChanges,
    () => /Mac|iPhone|iPad/.test(navigator.userAgent),
    () => false,
  );
  return (
    <button
      type="button"
      onClick={onOpen}
      className="flex items-center gap-2.5 rounded-lg border border-border bg-background/50 px-2.5 py-2 text-sm text-muted transition-colors hover:border-muted/50 hover:text-foreground"
    >
      <SearchIcon className="size-4" />
      <span className="flex-1 text-left">Search</span>
      <kbd className="flex items-center gap-0.5 rounded-md border border-border px-1.5 font-sans text-[11px]">
        {mac ? <CommandIcon className="size-3" /> : "Ctrl "}K
      </kbd>
    </button>
  );
}

/** The firm's settings as tabs above each of them, so the others are one click away. */
function SettingsNav({ path }: { path: string }) {
  return (
    <nav aria-label="Settings" className="mx-auto w-full max-w-6xl px-4 pt-5 sm:px-7">
      <div className="flex gap-1 overflow-x-auto border-b border-border text-sm">
        {settingsPlaces.map((place) => {
          const current = isUnder(path, place.href);
          return (
            <Link
              key={place.href}
              href={place.href}
              aria-current={current ? "page" : undefined}
              className={`relative whitespace-nowrap px-3 py-2.5 transition-colors ${current ? "font-medium text-foreground" : "text-muted hover:text-foreground"}`}
            >
              {place.label}
              {current && <motion.span layoutId="settings-current" className="absolute inset-x-2 -bottom-px h-0.5 rounded-full bg-accent" />}
            </Link>
          );
        })}
      </div>
    </nav>
  );
}

/** The firm's portal as its traders see it, and the administrator with the way out. */
function Footer({ me }: { me: Me }) {
  const logout = useLogout("admin");
  const router = useRouter();
  return (
    <div className="mt-auto flex flex-col gap-1 border-t border-border pt-3 text-sm">
      <a href="/" target="_blank" rel="noopener" className="flex items-center gap-2.5 rounded-md px-2.5 py-2 text-muted hover:text-foreground">
        <ExternalIcon className="size-4" />
        Open your portal
      </a>
      <div className="flex items-center gap-2.5 py-1 pl-2.5">
        <span aria-hidden="true" className="grid size-7 shrink-0 place-items-center rounded-full border border-border bg-background text-[11px] font-semibold">
          {initials(me.email, me.name)}
        </span>
        <span className="min-w-0 flex-1 truncate text-muted">{me.email}</span>
        <button
          type="button"
          aria-label="Log out"
          title="Log out"
          disabled={logout.isPending}
          onClick={() => logout.mutate(undefined, { onSuccess: () => router.replace("/admin/login") })}
          className="grid size-9 shrink-0 place-items-center rounded-md text-muted hover:bg-background hover:text-foreground"
        >
          <LogoutIcon />
        </button>
      </div>
    </div>
  );
}

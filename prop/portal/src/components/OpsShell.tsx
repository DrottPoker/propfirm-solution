"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useId, useState } from "react";

import { useOps } from "@/app/providers";
import type { OpsMe } from "@/lib/api/types";
import { initials } from "@/lib/dashboard";
import { useOpsLogout, useOpsWaiting } from "@/lib/opsQueries";

import { BuildingIcon, CloseIcon, ExternalIcon, LogoutIcon, MenuIcon, OverviewIcon, ReceiptIcon } from "./icons";

type NavLink = {
  href: string;
  label: string;
  icon: (props: { className?: string }) => React.ReactNode;
  matches: (path: string) => boolean;
  badge?: "toReview" | "unpaid";
};

const startsWith = (prefix: string) => (path: string) => path === prefix || path.startsWith(`${prefix}/`);

const links: NavLink[] = [
  { href: "/ops", label: "Overview", icon: OverviewIcon, matches: (path) => path === "/ops" },
  { href: "/ops/firms", label: "Firms", icon: BuildingIcon, matches: startsWith("/ops/firms"), badge: "toReview" },
  { href: "/ops/billing", label: "Billing", icon: ReceiptIcon, matches: startsWith("/ops/billing"), badge: "unpaid" },
];

/**
 * Our own admin view around every page (ADR 0024): the platform, the way around with what waits for us, and who is
 * logged in. On a phone the menu folds into a button at the top.
 */
export function OpsShell({ me, children }: { me: OpsMe; children: React.ReactNode }) {
  const path = usePathname();
  const [menuOpen, setMenuOpen] = useState(false);
  const [openedOn, setOpenedOn] = useState(path);
  const menuId = useId();

  // A new page closes the menu on a phone.
  if (menuOpen && openedOn !== path) {
    setMenuOpen(false);
  }

  return (
    <div className="flex flex-1 flex-col md:flex-row">
      <aside aria-label="Staff menu" className="hidden w-60 shrink-0 flex-col gap-5 border-r border-border bg-panel px-3 py-4 md:sticky md:top-0 md:flex md:h-screen md:overflow-y-auto">
        <PlatformMark />
        <Navigation path={path} />
        <Footer me={me} />
      </aside>

      <div className="border-b border-border bg-panel md:hidden">
        <div className="flex items-center gap-3 px-4 py-2.5">
          <PlatformMark />
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
            <Navigation path={path} />
            <Footer me={me} />
          </div>
        )}
      </div>

      <div className="flex min-w-0 flex-1 flex-col">{children}</div>
    </div>
  );
}

/** The platform's initial and name, marked as our staff's view. */
function PlatformMark() {
  const ops = useOps();
  return (
    <Link href="/ops" className="flex min-w-0 items-center gap-2.5 px-2 py-1">
      <span aria-hidden="true" className="grid size-8 shrink-0 place-items-center rounded-lg bg-accent font-bold text-accent-foreground">
        {ops.name.trim().charAt(0).toUpperCase()}
      </span>
      <span className="flex min-w-0 flex-col leading-tight">
        <span className="truncate font-semibold">{ops.name}</span>
        <span className="text-xs text-muted">Staff</span>
      </span>
    </Link>
  );
}

function Navigation({ path }: { path: string }) {
  const waiting = useOpsWaiting();
  const badges = { toReview: waiting.data?.toReview ?? 0, unpaid: waiting.data?.unpaid ?? 0 };
  const badgeText = { toReview: "waiting for review", unpaid: "not paid" };

  return (
    <nav aria-label="Staff" className="flex flex-col gap-0.5 text-sm">
      {links.map((link) => {
        const current = link.matches(path);
        const badge = link.badge ? badges[link.badge] : 0;
        return (
          <Link
            key={link.href}
            href={link.href}
            aria-current={current ? "page" : undefined}
            className={`flex items-center gap-2.5 rounded-md px-2.5 py-2 ${current ? "bg-background font-medium text-foreground" : "text-muted hover:text-foreground"}`}
          >
            <link.icon className="size-4 shrink-0" />
            <span className="flex-1">{link.label}</span>
            {link.badge && badge > 0 && (
              <span className={`min-w-5 rounded-full px-1.5 text-center font-mono text-[11px] font-medium text-foreground tabular-nums ${link.badge === "unpaid" ? "bg-loss/25" : "bg-accent/20"}`}>
                {badge}
                <span className="sr-only"> {badgeText[link.badge]}</span>
              </span>
            )}
          </Link>
        );
      })}
    </nav>
  );
}

/** Where firms sign up, and the staff member with the way out. */
function Footer({ me }: { me: OpsMe }) {
  const ops = useOps();
  const logout = useOpsLogout();
  const router = useRouter();
  return (
    <div className="mt-auto flex flex-col gap-1 border-t border-border pt-3 text-sm">
      {ops.signupUrl && (
        <a href={ops.signupUrl} target="_blank" rel="noopener" className="flex items-center gap-2.5 rounded-md px-2.5 py-2 text-muted hover:text-foreground">
          <ExternalIcon className="size-4" />
          Open the sign-up page
        </a>
      )}
      <div className="flex items-center gap-2.5 py-1 pl-2.5">
        <span aria-hidden="true" className="grid size-7 shrink-0 place-items-center rounded-full border border-border bg-background text-[11px] font-semibold">
          {initials(me.email)}
        </span>
        <span className="min-w-0 flex-1 truncate text-muted">{me.email}</span>
        <button
          type="button"
          aria-label="Log out"
          title="Log out"
          disabled={logout.isPending}
          onClick={() => logout.mutate(undefined, { onSuccess: () => router.replace("/ops/login") })}
          className="grid size-9 shrink-0 place-items-center rounded-md text-muted hover:bg-background hover:text-foreground"
        >
          <LogoutIcon />
        </button>
      </div>
    </div>
  );
}

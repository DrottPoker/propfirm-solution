"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useId, useState } from "react";

import { useBranding } from "@/app/providers";

import type { FirmStatus, Me } from "@/lib/api/types";
import { initials } from "@/lib/dashboard";
import { useFirmSupportSummary, useLogout, usePayoutSummary, useWaitingAccounts } from "@/lib/queries";

import { BillingNotice } from "./BillingNotice";
import { FirmName } from "./FirmName";
import { AccountsIcon, BagIcon, CardIcon, CloseIcon, CodeIcon, ExternalIcon, FlagIcon, GlobeIcon, LogoutIcon, MailIcon, MenuIcon, OverviewIcon, PaletteIcon, PayoutIcon, ReceiptIcon, RocketIcon, ServerIcon, SupportIcon, TagIcon, TeamIcon } from "./icons";

type NavLink = {
  href: string;
  label: string;
  icon: (props: { className?: string }) => React.ReactNode;
  matches: (path: string) => boolean;
  badge?: "accounts" | "payouts" | "support";
};

const startsWith = (prefix: string) => (path: string) => path === prefix || path.startsWith(`${prefix}/`);

/** The daily work, then the firm's own settings. Go live is there until the firm is live, and Plan and billing from then. */
function navigation(status: FirmStatus): { label: string | null; links: NavLink[] }[] {
  return [
    {
      label: null,
      links: [
        { href: "/admin", label: "Overview", icon: OverviewIcon, matches: (path) => path === "/admin" },
        { href: "/admin/accounts", label: "Accounts", icon: AccountsIcon, matches: startsWith("/admin/accounts"), badge: "accounts" },
        { href: "/admin/payouts", label: "Payouts", icon: PayoutIcon, matches: startsWith("/admin/payouts"), badge: "payouts" },
        { href: "/admin/support", label: "Support", icon: SupportIcon, matches: startsWith("/admin/support"), badge: "support" },
        { href: "/admin/orders", label: "Orders", icon: BagIcon, matches: startsWith("/admin/orders") },
        { href: "/admin/discounts", label: "Discount codes", icon: TagIcon, matches: startsWith("/admin/discounts") },
        { href: "/admin/challenges", label: "Challenges", icon: FlagIcon, matches: startsWith("/admin/challenges") },
      ],
    },
    {
      label: "Your firm",
      links: [
        { href: "/admin/design", label: "Portal design", icon: PaletteIcon, matches: startsWith("/admin/design") },
        { href: "/admin/domain", label: "Your domain", icon: GlobeIcon, matches: startsWith("/admin/domain") },
        { href: "/admin/checkout", label: "Checkout", icon: CardIcon, matches: startsWith("/admin/checkout") },
        { href: "/admin/trading", label: "Trading conditions", icon: ServerIcon, matches: startsWith("/admin/trading") },
        { href: "/admin/notifications", label: "Notifications", icon: MailIcon, matches: startsWith("/admin/notifications") },
        { href: "/admin/integrations", label: "Integrations", icon: CodeIcon, matches: startsWith("/admin/integrations") },
        { href: "/admin/team", label: "Team", icon: TeamIcon, matches: startsWith("/admin/team") },
        status === "Live"
          ? { href: "/admin/billing", label: "Plan and billing", icon: ReceiptIcon, matches: startsWith("/admin/billing") }
          : { href: "/admin/go-live", label: "Go live", icon: RocketIcon, matches: (path) => startsWith("/admin/go-live")(path) || startsWith("/admin/billing")(path) },
      ],
    },
  ];
}

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
  const menuId = useId();

  // A new page closes the menu on a phone.
  if (menuOpen && openedOn !== path) {
    setMenuOpen(false);
  }

  return (
    <div className="flex flex-1 flex-col md:flex-row">
      <aside aria-label="Admin menu" className="hidden w-60 shrink-0 flex-col gap-5 border-r border-border bg-panel px-3 py-4 md:sticky md:top-0 md:flex md:h-screen md:overflow-y-auto">
        <FirmMark />
        <Navigation path={path} />
        <Footer me={me} />
      </aside>

      <div className="border-b border-border bg-panel md:hidden">
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
            <Navigation path={path} />
            <Footer me={me} />
          </div>
        )}
      </div>

      <div className="flex min-w-0 flex-1 flex-col">
        <BillingNotice />
        {children}
      </div>
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

function Navigation({ path }: { path: string }) {
  const { status } = useBranding();
  const payouts = usePayoutSummary();
  const waiting = useWaitingAccounts(3);
  const support = useFirmSupportSummary();
  const badges = { payouts: payouts.data?.toApprove.count ?? 0, accounts: waiting.data?.counts.awaitingFunding ?? 0, support: support.data?.open ?? 0 };

  return (
    <nav aria-label="Admin" className="flex flex-col gap-0.5 text-sm">
      {navigation(status).map((group) => (
        <div key={group.label ?? "main"} className="flex flex-col gap-0.5">
          {group.label && <span className="px-2.5 pb-1.5 pt-4 text-[11px] font-medium uppercase tracking-wider text-muted">{group.label}</span>}
          {group.links.map((link) => {
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
                {badge > 0 && (
                  <span className="min-w-5 rounded-full bg-accent/20 px-1.5 text-center font-mono text-[11px] font-medium text-foreground tabular-nums">
                    {badge}
                    <span className="sr-only"> waiting for you</span>
                  </span>
                )}
              </Link>
            );
          })}
        </div>
      ))}
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
          {initials(me.email)}
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

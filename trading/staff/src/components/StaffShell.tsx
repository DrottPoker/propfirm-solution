"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useId, useState } from "react";

import type { StaffMe } from "@/lib/api/types";
import { environmentName, productName, propStaffUrl } from "@/lib/config";
import { useLogout, useOverview } from "@/lib/queries";

import { EngineIcon, ExposureIcon, ExternalIcon, FeedIcon, InstrumentsIcon, KronantMark, LogOutIcon, MenuIcon, CloseIcon, OverviewIcon, SearchIcon, ServersIcon } from "./icons";
import { SearchPalette } from "./SearchPalette";

type NavLink = {
  href: string;
  label: string;
  icon: (props: { className?: string }) => React.ReactNode;
  matches: (path: string) => boolean;
};

const startsWith = (prefix: string) => (path: string) => path === prefix || path.startsWith(`${prefix}/`);

const links: NavLink[] = [
  { href: "/", label: "Overview", icon: OverviewIcon, matches: (path) => path === "/" },
  { href: "/servers", label: "Servers", icon: ServersIcon, matches: startsWith("/servers") },
  { href: "/price-feed", label: "Price feed", icon: FeedIcon, matches: startsWith("/price-feed") },
  { href: "/instruments", label: "Instruments", icon: InstrumentsIcon, matches: startsWith("/instruments") },
  { href: "/exposure", label: "Exposure", icon: ExposureIcon, matches: startsWith("/exposure") },
  { href: "/engine", label: "Engine", icon: EngineIcon, matches: startsWith("/engine") },
];

/**
 * Every page of the staff panel inside the menu (ADR 0057): the way around, with what needs us beside the places, the
 * search, and who is logged in. On a phone the menu folds into a button at the top.
 */
export function StaffShell({ me, children }: { me: StaffMe; children: React.ReactNode }) {
  const path = usePathname();
  const [menuOpen, setMenuOpen] = useState(false);
  const [openedOn, setOpenedOn] = useState(path);
  const [searchOpen, setSearchOpen] = useState(false);
  const menuId = useId();

  // A new page closes the menu on a phone.
  if (menuOpen && openedOn !== path) {
    setMenuOpen(false);
  }

  return (
    <div className="flex flex-1 flex-col md:flex-row">
      <aside aria-label="Staff menu" className="hidden w-60 shrink-0 flex-col gap-5 border-r border-border bg-panel px-3 py-5 md:sticky md:top-0 md:flex md:h-dvh md:overflow-y-auto">
        <Mark />
        <Navigation path={path} />
        <Footer me={me} />
      </aside>

      <div className="border-b border-border bg-panel md:hidden">
        <div className="flex items-center gap-2 px-4 py-2.5">
          <Mark />
          <button type="button" aria-label="Search" onClick={() => setSearchOpen(true)} className="ml-auto grid size-11 place-items-center rounded-lg border border-border bg-raised">
            <SearchIcon />
          </button>
          <button
            type="button"
            aria-label={menuOpen ? "Close the menu" : "Open the menu"}
            aria-expanded={menuOpen}
            aria-controls={menuId}
            onClick={() => {
              setOpenedOn(path);
              setMenuOpen(!menuOpen);
            }}
            className="grid size-11 place-items-center rounded-lg border border-border bg-raised"
          >
            {menuOpen ? <CloseIcon className="size-5" /> : <MenuIcon />}
          </button>
        </div>
        {menuOpen && (
          <div id={menuId} className="flex flex-col gap-4 border-t border-border px-3 py-3">
            <Navigation path={path} />
            <Footer me={me} />
          </div>
        )}
      </div>

      <main className="flex min-w-0 flex-1 flex-col gap-6 px-4 pt-6 pb-14 sm:px-8 sm:pt-7">
        <TopBar onSearch={() => setSearchOpen(true)} />
        {children}
      </main>
      <SearchPalette open={searchOpen} onOpenChange={setSearchOpen} />
    </div>
  );
}

/** Which platform this is, and the search, above every page. */
function TopBar({ onSearch }: { onSearch: () => void }) {
  // The panel draws only in the browser, once it knows who is logged in, so the platform is known here.
  const [shortcut] = useState(() => (/Mac|iPhone|iPad/.test(navigator.platform) ? "Cmd K" : "Ctrl K"));

  return (
    <div className="flex flex-wrap items-center justify-between gap-3">
      <span className="text-sm text-muted">
        <span className="font-semibold text-accent">{environmentName}</span>, times in UTC
      </span>
      <button
        type="button"
        onClick={onSearch}
        className="hidden h-10 min-w-72 items-center gap-2.5 rounded-lg border border-border bg-panel px-3 text-left text-muted transition hover:border-muted/60 md:flex"
      >
        <SearchIcon />
        <span className="flex-1">Find a server or account number</span>
        <kbd className="font-mono text-xs">{shortcut}</kbd>
      </button>
    </div>
  );
}

function Mark() {
  return (
    <Link href="/" className="flex min-w-0 items-center gap-2.5 px-2 py-1">
      <KronantMark />
      <span className="flex min-w-0 flex-col leading-tight">
        <span className="truncate font-semibold">{productName}</span>
        <span className="text-xs text-accent">Staff</span>
      </span>
    </Link>
  );
}

function Navigation({ path }: { path: string }) {
  const overview = useOverview();
  const needs = overview.data?.needsUs ?? [];
  const silent = needs.filter((n) => n.kind === "SymbolSilent").length;
  const notes: Record<string, { text: string; tone: string } | undefined> = {
    "/price-feed": needs.some((n) => n.kind === "FeedSilent")
      ? { text: "silent", tone: "text-loss" }
      : silent > 0
        ? { text: `${silent} silent`, tone: "text-warning" }
        : needs.some((n) => n.kind === "ChartGapNotFilled")
          ? { text: "gap", tone: "text-muted" }
          : undefined,
    "/servers": needs.some((n) => n.kind === "EventsNotRead")
      ? { text: `${needs.filter((n) => n.kind === "EventsNotRead").length} late`, tone: "text-warning" }
      : undefined,
    "/engine": needs.some((n) => n.kind === "QueueBehind" || n.kind === "SlowSaves") ? { text: "slow", tone: "text-warning" } : undefined,
  };

  return (
    <nav aria-label="Staff" className="flex flex-col gap-0.5 text-sm">
      {links.map((link) => {
        const current = link.matches(path);
        const note = notes[link.href];
        return (
          <Link
            key={link.href}
            href={link.href}
            aria-current={current ? "page" : undefined}
            className={`flex items-center gap-2.5 rounded-lg px-2.5 py-2 ${current ? "bg-raised font-semibold text-foreground" : "text-muted hover:text-foreground"}`}
          >
            <link.icon className="size-4" />
            <span className="flex-1">{link.label}</span>
            {note && <span className={`text-xs font-normal ${note.tone}`}>{note.text}</span>}
          </Link>
        );
      })}
    </nav>
  );
}

function Footer({ me }: { me: StaffMe }) {
  const logout = useLogout();
  const router = useRouter();
  return (
    <div className="mt-auto flex flex-col gap-1 border-t border-border pt-3 text-sm">
      {propStaffUrl && (
        <a href={propStaffUrl} target="_blank" rel="noopener" className="flex items-center gap-2.5 rounded-lg px-2.5 py-2 text-muted hover:text-foreground">
          <span className="flex-1">Kronant Prop staff view</span>
          <ExternalIcon />
        </a>
      )}
      <div className="flex items-center gap-2 px-2.5 py-2">
        <span className="min-w-0 flex-1 truncate text-muted" title={me.email}>
          {me.email}
        </span>
        <button
          type="button"
          aria-label="Log out"
          title="Log out"
          onClick={() => logout.mutate(undefined, { onSettled: () => router.replace("/login") })}
          className="grid size-9 place-items-center rounded-md text-muted hover:bg-raised hover:text-foreground"
        >
          <LogOutIcon />
        </button>
      </div>
    </div>
  );
}

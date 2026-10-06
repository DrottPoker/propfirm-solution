"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useId, useRef, useState } from "react";

import type { Me } from "@/lib/api/types";
import { initials } from "@/lib/dashboard";
import { useLogout, useMySupportSummary, useShop } from "@/lib/queries";

import { FirmName } from "./FirmName";
import { CloseIcon, MenuIcon } from "./icons";
import { buttonClass } from "./ui";

const traderLinks = [
  { href: "/", label: "Accounts", matches: (path: string) => path === "/" || path.startsWith("/accounts") },
  { href: "/payouts", label: "Payouts", matches: (path: string) => path.startsWith("/payouts") },
  { href: "/support", label: "Support", matches: (path: string) => path.startsWith("/support") },
];

/** How many tickets have a message from the firm the trader has not read, beside Support. */
function UnreadBadge({ href, unread }: { href: string; unread: number }) {
  if (href !== "/support" || unread === 0) {
    return null;
  }

  return (
    <span className="ml-1.5 min-w-5 rounded-full bg-accent px-1.5 text-center font-mono text-[11px] font-medium text-accent-foreground tabular-nums">
      {unread}
      <span className="sr-only"> {unread === 1 ? "unread message" : "unread messages"}</span>
    </span>
  );
}

/**
 * The firm's name and the trader's way around: accounts, payouts and support with the answers not read yet, the firm's
 * shop, and a menu with the email and log out. On a phone the links move into the menu. Administrators have the admin
 * panel's menu instead.
 */
export function PortalHeader({ me }: { me: Me }) {
  const shop = useShop(true);
  const support = useMySupportSummary();
  const path = usePathname();
  const canBuy = shop.data?.open === true;
  const unread = support.data?.unread ?? 0;

  return (
    <header className="border-b border-border bg-panel text-sm">
      <div className="mx-auto flex w-full max-w-6xl items-center gap-6 px-4 py-2.5 sm:px-6">
        <Link href="/" className="shrink-0">
          <FirmName />
        </Link>
        <nav aria-label="Main" className="hidden gap-1 sm:flex">
          {traderLinks.map((link) => (
            <Link
              key={link.href}
              href={link.href}
              aria-current={link.matches(path) ? "page" : undefined}
              className={`flex items-center rounded-md px-3 py-2 ${link.matches(path) ? "bg-background font-medium text-foreground" : "text-muted hover:text-foreground"}`}
            >
              {link.label}
              <UnreadBadge href={link.href} unread={unread} />
            </Link>
          ))}
        </nav>
        <div className="ml-auto flex items-center gap-3">
          {canBuy && (
            <Link href="/buy" className={`${buttonClass} hidden sm:inline-block`}>
              Buy a challenge
            </Link>
          )}
          <TraderMenu me={me} path={path} canBuy={canBuy} unread={unread} />
        </div>
      </div>
    </header>
  );
}

function TraderMenu({ me, path, canBuy, unread }: { me: Me; path: string; canBuy: boolean; unread: number }) {
  const [open, setOpen] = useState(false);
  const logout = useLogout("trader");
  const router = useRouter();
  const menuId = useId();
  const container = useRef<HTMLDivElement>(null);

  // A new page closes the menu.
  const [openedOn, setOpenedOn] = useState(path);
  if (open && openedOn !== path) {
    setOpen(false);
  }

  useEffect(() => {
    if (!open) {
      return;
    }

    const closeOutside = (event: PointerEvent) => {
      if (!container.current?.contains(event.target as Node)) {
        setOpen(false);
      }
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setOpen(false);
      }
    };
    document.addEventListener("pointerdown", closeOutside);
    document.addEventListener("keydown", closeOnEscape);
    return () => {
      document.removeEventListener("pointerdown", closeOutside);
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, [open]);

  return (
    <div ref={container} className="relative">
      <button
        type="button"
        aria-label={open ? "Close the menu" : `Menu for ${me.email}`}
        aria-expanded={open}
        aria-controls={menuId}
        onClick={() => {
          setOpenedOn(path);
          setOpen(!open);
        }}
        className="flex h-10 items-center gap-2 rounded-lg border border-border bg-background px-2.5 hover:border-muted sm:rounded-full sm:px-0 sm:size-10 sm:justify-center"
      >
        <span className="flex items-center gap-1.5 sm:hidden">
          {open ? <CloseIcon className="size-5" /> : <MenuIcon className="size-5" />}
          <span className="text-sm">Menu</span>
          {unread > 0 && <span aria-hidden="true" className="size-2 rounded-full bg-accent" />}
        </span>
        <span aria-hidden="true" className="hidden text-xs font-semibold sm:inline">
          {initials(me.email, me.name)}
        </span>
      </button>
      {open && (
        <div id={menuId} className="absolute right-0 z-20 mt-2 flex w-64 flex-col gap-1 rounded-lg border border-border bg-panel p-2 shadow-xl">
          <p className="flex flex-col px-3 py-2">
            {me.name && <span className="truncate font-medium">{me.name}</span>}
            <span className="truncate text-muted">{me.email}</span>
          </p>
          <nav aria-label="Main" className="flex flex-col border-t border-border pt-1 sm:hidden">
            {traderLinks.map((link) => (
              <Link
                key={link.href}
                href={link.href}
                aria-current={link.matches(path) ? "page" : undefined}
                className="flex items-center rounded-md px-3 py-2.5 hover:bg-background"
              >
                {link.label}
                <UnreadBadge href={link.href} unread={unread} />
              </Link>
            ))}
            {canBuy && (
              <Link href="/buy" className="rounded-md px-3 py-2.5 text-accent hover:bg-background">
                Buy a challenge
              </Link>
            )}
          </nav>
          <button
            type="button"
            disabled={logout.isPending}
            onClick={() => logout.mutate(undefined, { onSuccess: () => router.replace("/login") })}
            className="rounded-md border-t border-border px-3 py-2.5 text-left hover:bg-background"
          >
            Log out
          </button>
        </div>
      )}
    </div>
  );
}

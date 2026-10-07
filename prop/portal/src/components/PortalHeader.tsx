"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useId, useRef, useState } from "react";
import { toast } from "sonner";

import type { Me } from "@/lib/api/types";
import { initials } from "@/lib/dashboard";
import { useRequestPasswordReset } from "@/lib/passwordQueries";
import { useLogout, useMySupportSummary, useShop } from "@/lib/queries";

import { FirmName } from "./FirmName";
import { CloseIcon, LockIcon, LogoutIcon, MenuIcon, StatusIcon } from "./icons";
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
    <span className="ml-1.5 min-w-5 rounded-full bg-accent px-1.5 text-center text-[11px] font-medium text-accent-foreground tabular-nums">
      {unread}
      <span className="sr-only"> {unread === 1 ? "unread message" : "unread messages"}</span>
    </span>
  );
}

/**
 * The firm's name and the trader's way around: accounts, payouts and support with the answers not read yet, the firm's
 * shop, and a menu with the trader's name and email, a new password and log out. On a phone the links move into the
 * menu. Administrators have the admin panel's menu instead.
 */
export function PortalHeader({ me }: { me: Me }) {
  const shop = useShop(true);
  const support = useMySupportSummary();
  const path = usePathname();
  const canBuy = shop.data?.open === true;
  const unread = support.data?.unread ?? 0;

  return (
    <header className="sticky top-0 z-30 border-b border-border bg-panel/85 text-sm backdrop-blur-md print:hidden">
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
              className={`flex items-center rounded-lg px-3 py-2 transition-colors ${link.matches(path) ? "bg-raised font-medium text-foreground" : "text-muted hover:text-foreground"}`}
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
  const reset = useRequestPasswordReset("trader");
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
        className="flex h-10 items-center gap-2 rounded-lg border border-border bg-background px-2.5 transition-colors hover:border-muted sm:rounded-full sm:py-0 sm:pr-3 sm:pl-1"
      >
        <span className="flex items-center gap-1.5 sm:hidden">
          {open ? <CloseIcon className="size-5" /> : <MenuIcon className="size-5" />}
          <span className="text-sm">Menu</span>
          {unread > 0 && <span aria-hidden="true" className="size-2 rounded-full bg-accent" />}
        </span>
        <span aria-hidden="true" className="hidden items-center gap-2 sm:flex">
          <Avatar me={me} />
          <span className="max-w-32 truncate">{firstName(me)}</span>
        </span>
      </button>
      {open && (
        <div id={menuId} className="absolute right-0 z-20 mt-2 flex w-72 origin-top-right animate-pop flex-col gap-1 rounded-xl border border-border bg-panel p-1.5 shadow-float">
          <div className="flex items-center gap-3 px-3 py-2.5">
            <Avatar me={me} large />
            <p className="flex min-w-0 flex-col">
              {me.name && <span className="truncate font-medium">{me.name}</span>}
              <span className="truncate text-muted">{me.email}</span>
            </p>
          </div>
          <nav aria-label="Main" className="flex flex-col border-t border-border pt-1 sm:hidden">
            {traderLinks.map((link) => (
              <Link
                key={link.href}
                href={link.href}
                aria-current={link.matches(path) ? "page" : undefined}
                className="flex items-center rounded-lg px-3 py-2.5 transition-colors hover:bg-raised"
              >
                {link.label}
                <UnreadBadge href={link.href} unread={unread} />
              </Link>
            ))}
            {canBuy && (
              <Link href="/buy" className="rounded-lg px-3 py-2.5 text-accent transition-colors hover:bg-raised">
                Buy a challenge
              </Link>
            )}
          </nav>
          <div className="flex flex-col border-t border-border pt-1">
            <Link href="/status" className="flex items-center gap-2.5 rounded-lg px-3 py-2.5 transition-colors hover:bg-raised">
              <StatusIcon className="size-4 text-muted" />
              Status of trading
            </Link>
            <button
              type="button"
              disabled={reset.isPending}
              onClick={() =>
                reset.mutate(me.email, {
                  onSuccess: () => toast.success(`We sent a link to ${me.email}. Open it to choose a new password.`),
                  onError: (error) => toast.error(error.message),
                })
              }
              className="flex items-center gap-2.5 rounded-lg px-3 py-2.5 text-left transition-colors hover:bg-raised"
            >
              <LockIcon className="size-4 text-muted" />
              {reset.isPending ? "Sending..." : "Change password"}
            </button>
            <button
              type="button"
              disabled={logout.isPending}
              onClick={() => logout.mutate(undefined, { onSuccess: () => router.replace("/login") })}
              className="flex items-center gap-2.5 rounded-lg px-3 py-2.5 text-left transition-colors hover:bg-raised"
            >
              <LogoutIcon className="size-4 text-muted" />
              Log out
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

/** The trader's initials in a ring of the firm's color. */
function Avatar({ me, large = false }: { me: Me; large?: boolean }) {
  return (
    <span className={`grid shrink-0 place-items-center rounded-full bg-accent/15 font-semibold text-accent ring-1 ring-accent/30 ${large ? "size-10 text-sm" : "size-8 text-xs"}`}>
      {initials(me.email, me.name)}
    </span>
  );
}

/** The trader's first name, or the email's name part before the trader has given a name. */
function firstName(me: Me): string {
  return me.name?.trim().split(/\s+/)[0] || me.email.split("@")[0];
}

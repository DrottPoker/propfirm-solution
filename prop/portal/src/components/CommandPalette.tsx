"use client";

import { useQuery } from "@tanstack/react-query";
import { Command } from "cmdk";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";

import { useBranding } from "@/app/providers";
import { accountStatus } from "@/lib/admin";
import { dailyPlaces, liveOrBilling, settingsPlaces, type AdminPlace } from "@/lib/adminNav";
import { api, resultOf } from "@/lib/api/client";
import { useDebounced } from "@/lib/useDebounced";

import { AccountsIcon, ChevronRightIcon, SearchIcon, SettingsIcon } from "./icons";

/**
 * Search from any page of the admin panel, opened with Ctrl+K (Cmd+K on a Mac) or the search button: its pages and
 * settings by name, and the firm's accounts by email, number or order reference, as the account list finds them.
 */
export function CommandPalette({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const router = useRouter();
  const { status } = useBranding();
  const [search, setSearch] = useState("");
  const query = useDebounced(search.trim(), 200);
  const accounts = useQuery({
    queryKey: ["palette-accounts", query],
    enabled: open && query.length >= 2,
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/accounts", { params: { query: { search: query, limit: 8 } } }), "the accounts"),
    staleTime: 10_000,
  });

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key.toLowerCase() === "k" && (event.metaKey || event.ctrlKey)) {
        event.preventDefault();
        onOpenChange(!open);
      }
    };
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [open, onOpenChange]);

  const go = (href: string) => {
    onOpenChange(false);
    setSearch("");
    router.push(href);
  };

  const place = (item: AdminPlace, icon: React.ReactNode) => (
    <Command.Item key={item.href} value={`${item.label} ${item.description} ${(item.keywords ?? []).join(" ")}`} onSelect={() => go(item.href)} className={itemClass}>
      {icon}
      <span className="flex min-w-0 flex-1 flex-col">
        <span className="truncate">{item.label}</span>
        <span className="truncate text-xs text-muted">{item.description}</span>
      </span>
      <ChevronRightIcon className="size-3.5 text-muted" />
    </Command.Item>
  );

  return (
    <Command.Dialog
      open={open}
      onOpenChange={onOpenChange}
      label="Search the admin panel"
      overlayClassName="fixed inset-0 z-40 animate-fade bg-background/60 backdrop-blur-sm"
      contentClassName="fixed inset-x-4 top-[12vh] z-50 mx-auto max-w-xl animate-pop overflow-hidden rounded-2xl border border-border bg-panel shadow-float"
    >
      <div className="flex items-center gap-2.5 border-b border-border px-4">
        <SearchIcon className="size-4 text-muted" />
        <Command.Input
          value={search}
          onValueChange={setSearch}
          placeholder="Search pages, or accounts by email, number or order"
          className="h-12 flex-1 bg-transparent text-sm outline-none placeholder:text-muted/80 focus-visible:outline-none"
        />
        <kbd className="rounded-md border border-border px-1.5 py-0.5 text-[11px] text-muted">Esc</kbd>
      </div>
      <Command.List className="max-h-[min(26rem,60vh)] overflow-y-auto p-2">
        <Command.Empty className="px-3 py-8 text-center text-sm text-muted">
          {accounts.isFetching ? "Searching..." : "Nothing found. Try an email, an account number or a page's name."}
        </Command.Empty>
        {accounts.data && accounts.data.accounts.length > 0 && (
          <Command.Group heading="Accounts" className={groupClass}>
            {accounts.data.accounts.map((account) => (
              <Command.Item
                key={account.id}
                value={`account ${account.number} ${account.email} ${account.reference ?? ""} ${query}`}
                onSelect={() => go(`/admin/accounts/${account.id}`)}
                className={itemClass}
              >
                <AccountsIcon className="size-4 text-muted" />
                <span className="flex min-w-0 flex-1 flex-col">
                  <span className="truncate">
                    #{account.number} · {account.email}
                  </span>
                  <span className="truncate text-xs text-muted">
                    {account.stageName} · {accountStatus(account).label}
                  </span>
                </span>
                <ChevronRightIcon className="size-3.5 text-muted" />
              </Command.Item>
            ))}
          </Command.Group>
        )}
        <Command.Group heading="Go to" className={groupClass}>
          {[...dailyPlaces, liveOrBilling(status)].map((item) => place(item, <ChevronRightIcon className="size-4 text-muted" />))}
        </Command.Group>
        <Command.Group heading="Settings" className={groupClass}>
          {settingsPlaces.map((item) => place(item, <SettingsIcon className="size-4 text-muted" />))}
        </Command.Group>
      </Command.List>
    </Command.Dialog>
  );
}

const groupClass = "[&_[cmdk-group-heading]]:px-3 [&_[cmdk-group-heading]]:pb-1.5 [&_[cmdk-group-heading]]:pt-3 [&_[cmdk-group-heading]]:text-[11px] [&_[cmdk-group-heading]]:font-semibold [&_[cmdk-group-heading]]:uppercase [&_[cmdk-group-heading]]:tracking-[0.14em] [&_[cmdk-group-heading]]:text-muted";

const itemClass = "flex cursor-pointer items-center gap-3 rounded-lg px-3 py-2 text-sm data-[selected=true]:bg-raised data-[selected=true]:shadow-card";

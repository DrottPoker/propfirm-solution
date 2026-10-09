"use client";

import { Command } from "cmdk";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";

import { formatMoney } from "@/lib/format";
import { useSearch } from "@/lib/queries";
import { useDebounced } from "@/lib/useDebounced";

import { ChevronRightIcon, SearchIcon, ServersIcon } from "./icons";

const places = [
  { href: "/", label: "Overview", keywords: "needs us figures" },
  { href: "/servers", label: "Servers", keywords: "firms tenants" },
  { href: "/price-feed", label: "Price feed", keywords: "prices gaps history silent" },
  { href: "/instruments", label: "Instruments", keywords: "symbols trading hours holidays" },
  { href: "/exposure", label: "Exposure", keywords: "positions net long short" },
  { href: "/engine", label: "Engine", keywords: "queue journal snapshots partners" },
];

/**
 * Search from any page, opened with Ctrl+K (Cmd+K on a Mac) or the search button: servers by id or name, an account by
 * its number, and the panel's pages. An account opens on its server's page.
 */
export function SearchPalette({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const router = useRouter();
  const [search, setSearch] = useState("");
  const query = useDebounced(search.trim(), 200);
  const results = useSearch(open ? query : "");

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

  const account = query.length > 0 ? results.data?.account : null;
  const servers = query.length > 0 ? (results.data?.servers ?? []) : [];

  return (
    <Command.Dialog
      open={open}
      onOpenChange={onOpenChange}
      label="Search the staff panel"
      overlayClassName="fixed inset-0 z-40 animate-fade bg-background/60 backdrop-blur-sm"
      contentClassName="fixed inset-x-4 top-[12vh] z-50 mx-auto max-w-xl animate-pop overflow-hidden rounded-2xl border border-border bg-panel shadow-float"
    >
      <div className="flex items-center gap-2.5 border-b border-border px-4">
        <SearchIcon className="size-4 text-muted" />
        <Command.Input
          value={search}
          onValueChange={setSearch}
          placeholder="A server, or an account number"
          className="h-12 flex-1 bg-transparent text-sm outline-none placeholder:text-muted/80 focus-visible:outline-none"
        />
        <kbd className="rounded-md border border-border px-1.5 py-0.5 text-[11px] text-muted">Esc</kbd>
      </div>
      <Command.List className="max-h-[min(26rem,60vh)] overflow-y-auto p-2">
        <Command.Empty className="px-3 py-8 text-center text-sm text-muted">
          {results.isFetching ? "Searching..." : "Nothing found. Try a server's id or name, or a whole account number."}
        </Command.Empty>
        {account && (
          <Command.Group heading="Account" className={groupClass}>
            <Command.Item
              value={`account ${account.accountId} ${query}`}
              onSelect={() => go(`/servers/${encodeURIComponent(account.serverId)}?account=${encodeURIComponent(account.accountId)}`)}
              className={itemClass}
            >
              <span className="flex min-w-0 flex-1 flex-col">
                <span className="truncate font-medium">#{account.accountId}</span>
                <span className="truncate text-xs text-muted">
                  {account.serverName}, equity {formatMoney(account.equity)} {account.currency}
                </span>
              </span>
              <ChevronRightIcon className="size-3.5 text-muted" />
            </Command.Item>
          </Command.Group>
        )}
        {servers.length > 0 && (
          <Command.Group heading="Servers" className={groupClass}>
            {servers.map((server) => (
              <Command.Item key={server.id} value={`server ${server.id} ${server.name} ${query}`} onSelect={() => go(`/servers/${encodeURIComponent(server.id)}`)} className={itemClass}>
                <ServersIcon className="size-4 text-muted" />
                <span className="flex min-w-0 flex-1 flex-col">
                  <span className="truncate">{server.name}</span>
                  <span className="truncate font-mono text-xs text-muted">{server.id}</span>
                </span>
                <ChevronRightIcon className="size-3.5 text-muted" />
              </Command.Item>
            ))}
          </Command.Group>
        )}
        <Command.Group heading="Go to" className={groupClass}>
          {places.map((place) => (
            <Command.Item key={place.href} value={`${place.label} ${place.keywords}`} onSelect={() => go(place.href)} className={itemClass}>
              <span className="flex-1">{place.label}</span>
              <ChevronRightIcon className="size-3.5 text-muted" />
            </Command.Item>
          ))}
        </Command.Group>
      </Command.List>
    </Command.Dialog>
  );
}

const groupClass =
  "[&_[cmdk-group-heading]]:px-3 [&_[cmdk-group-heading]]:pb-1.5 [&_[cmdk-group-heading]]:pt-3 [&_[cmdk-group-heading]]:text-[11px] [&_[cmdk-group-heading]]:font-semibold [&_[cmdk-group-heading]]:uppercase [&_[cmdk-group-heading]]:tracking-[0.14em] [&_[cmdk-group-heading]]:text-muted";

const itemClass = "flex cursor-pointer items-center gap-3 rounded-lg px-3 py-2 text-sm data-[selected=true]:bg-raised data-[selected=true]:shadow-card";

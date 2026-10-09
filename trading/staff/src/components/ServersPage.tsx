"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";

import type { ServerRow, TerminalKind } from "@/lib/api/types";
import { formatAgo, formatCount, formatDate } from "@/lib/format";
import { kindNames } from "@/lib/kinds";
import { type ServerGroup, useCreateServer, useCurrencies, useServerName, useServers } from "@/lib/queries";
import { useDebounced } from "@/lib/useDebounced";
import { useNow } from "@/lib/useNow";

import { PlusIcon, SearchIcon } from "./icons";
import { KindChoice } from "./KindChoice";
import { buttonClass, CopyButton, ErrorText, fieldClass, Loading, Modal, PageHeader, secondaryButtonClass, TableBox, tdClass, thClass } from "./ui";

const shownAtFirst = 25;

/** Every firm on the platform, with who made it and how its system keeps up (ADR 0057). */
export function ServersPage() {
  const [group, setGroup] = useState<ServerGroup>("all");
  const [search, setSearch] = useState("");
  const [showAll, setShowAll] = useState(false);
  const [creating, setCreating] = useState(false);
  const servers = useServers(group, useDebounced(search, 200));
  const now = useNow(5_000);

  const counts = servers.data?.counts;
  const tabs: { group: ServerGroup; label: string; count?: number; tone?: string }[] = [
    { group: "all", label: "All", count: counts?.all },
    { group: "needsUs", label: "Needs us", count: counts?.needsUs, tone: counts?.needsUs ? "text-warning" : undefined },
    { group: "listed", label: "Listed", count: counts?.listed },
    { group: "notListed", label: "Not listed", count: counts?.notListed },
    { group: "configuration", label: "From configuration", count: counts?.configuration },
  ];
  const rows = servers.data?.servers ?? [];
  const shown = showAll ? rows : rows.slice(0, shownAtFirst);

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Servers"
        text="Every firm on Kronant Trader. A server is the firm's own part of the platform, with its groups, traders and accounts."
        actions={
          <button type="button" onClick={() => setCreating(true)} className={buttonClass}>
            <PlusIcon />
            New server
          </button>
        }
      />

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div role="group" aria-label="Which servers" className="flex flex-wrap gap-1 rounded-xl border border-border bg-panel p-1">
          {tabs.map((tab) => (
            <button
              key={tab.group}
              type="button"
              aria-pressed={group === tab.group}
              onClick={() => {
                setGroup(tab.group);
                setShowAll(false);
              }}
              className={`h-9 rounded-lg px-3 text-sm ${group === tab.group ? "bg-raised font-semibold text-foreground" : "text-muted hover:text-foreground"}`}
            >
              {tab.label} {tab.count !== undefined && <span className={`font-normal ${tab.tone ?? "text-muted"}`}>{formatCount(tab.count)}</span>}
            </button>
          ))}
        </div>
        <label className="flex h-10 min-w-64 items-center gap-2 rounded-lg border border-border bg-panel px-3 text-muted focus-within:border-accent">
          <SearchIcon />
          <span className="sr-only">Search servers</span>
          <input
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Server or firm name"
            className="flex-1 bg-transparent text-foreground outline-none placeholder:text-muted/70 focus-visible:outline-none"
          />
        </label>
      </div>

      {servers.isPending ? (
        <Loading label="Loading the servers" />
      ) : servers.isError ? (
        <ErrorText error={servers.error} />
      ) : (
        <div className="rounded-2xl border border-border bg-panel px-5 py-3 shadow-card">
          {rows.length === 0 ? (
            <p className="py-6 text-center text-muted">No servers here.</p>
          ) : (
            <TableBox minWidth={1080}>
              <thead>
                <tr className="text-right">
                  <th scope="col" className={`${thClass} text-left`}>
                    Server
                  </th>
                  <th scope="col" className={`${thClass} text-left`}>
                    Type
                  </th>
                  <th scope="col" className={`${thClass} text-left`}>
                    Made by
                  </th>
                  <th scope="col" className={`${thClass} text-left`}>
                    Login list
                  </th>
                  <th scope="col" className={thClass}>
                    Traders
                  </th>
                  <th scope="col" className={thClass}>
                    Accounts trading
                  </th>
                  <th scope="col" className={thClass}>
                    Open positions
                  </th>
                  <th scope="col" className={thClass}>
                    Events read
                  </th>
                  <th scope="col" className={thClass}>
                    Since
                  </th>
                </tr>
              </thead>
              <tbody>
                {shown.map((row) => (
                  <Row key={row.id} row={row} now={now} />
                ))}
              </tbody>
            </TableBox>
          )}
        </div>
      )}

      {rows.length > shownAtFirst && (
        <div className="flex flex-wrap justify-between gap-2 text-sm text-muted">
          <span>
            {formatCount(shown.length)} of {formatCount(rows.length)} servers, the most accounts first
          </span>
          {!showAll && (
            <button type="button" onClick={() => setShowAll(true)} className="text-accent hover:underline">
              Show {formatCount(rows.length - shownAtFirst)} more
            </button>
          )}
        </div>
      )}

      <NewServerDialog open={creating} onClose={() => setCreating(false)} />
    </div>
  );
}

function Row({ row, now }: { row: ServerRow; now: Date }) {
  const madeBy = row.madeBy === "Partner" ? (row.partnerName ?? "A partner") : row.madeBy === "Staff" ? `Us, ${row.createdBy}` : "Configuration";
  return (
    <tr className="text-right">
      <th scope="row" className={`${tdClass} text-left font-normal`}>
        <Link href={`/servers/${encodeURIComponent(row.id)}`} className="flex flex-col gap-0.5 hover:text-accent">
          <span className="font-semibold">{row.name}</span>
          <span className="font-mono text-xs text-muted">
            {row.id} {row.currencies.length > 0 && <span className="font-sans">in {row.currencies.join(", ")}</span>}
          </span>
        </Link>
      </th>
      <td className={`${tdClass} text-left`}>{kindNames[row.kind]}</td>
      <td className={`${tdClass} text-left text-muted`}>{madeBy}</td>
      <td className={`${tdClass} text-left font-medium ${row.listed ? "" : "text-muted"}`}>{row.listed ? "Listed" : "Not listed"}</td>
      <td className={tdClass}>{formatCount(row.traders)}</td>
      <td className={tdClass}>{formatCount(row.accountsTrading)}</td>
      <td className={tdClass}>{formatCount(row.openPositions)}</td>
      <td className={`${tdClass} ${row.eventsStale ? "font-medium text-warning" : "text-muted"}`}>{row.eventsReadAt ? formatAgo(row.eventsReadAt, now) : "Not since the start"}</td>
      <td className={`${tdClass} text-muted`}>{row.madeBy === "Configuration" ? "Every start" : row.createdAt ? formatDate(row.createdAt) : "-"}</td>
    </tr>
  );
}

/** Makes a server for a firm that uses Kronant Trader on its own, of the type of business chosen, and shows its admin key once. */
function NewServerDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const router = useRouter();
  const currencies = useCurrencies();
  const create = useCreateServer();
  const [id, setId] = useState("");
  const [name, setName] = useState("");
  const [currency, setCurrency] = useState("USD");
  // Nothing is chosen for the firm, so the type is always a decision.
  const [kind, setKind] = useState<TerminalKind | null>(null);
  const availability = useServerName(useDebounced(id.trim(), 250));
  const validId = /^[a-z0-9][a-z0-9-]{1,62}$/.test(id.trim());
  const created = create.data;

  const close = () => {
    if (created) {
      router.push(`/servers/${encodeURIComponent(created.id)}`);
    }

    create.reset();
    setId("");
    setName("");
    setKind(null);
    onClose();
  };

  return (
    <Modal
      open={open}
      onClose={close}
      title={created ? `Server ${created.id} is made` : "New server"}
      description={
        created
          ? "Give the admin key to the firm's technical contact now. It is shown only this once, and we keep only a hash of it."
          : "For a firm that uses Kronant Trader on its own, without Kronant Prop. Its traders log in with the server's name."
      }
      footer={
        created ? (
          <button type="button" onClick={close} className={buttonClass}>
            Done
          </button>
        ) : (
          <>
            <button type="button" onClick={close} className={secondaryButtonClass}>
              Cancel
            </button>
            <button
              type="submit"
              form="new-server"
              disabled={create.isPending || !validId || name.trim().length === 0 || kind === null || availability.data?.available === false}
              className={buttonClass}
            >
              {create.isPending ? "Making..." : "Make the server"}
            </button>
          </>
        )
      }
    >
      {created ? (
        <div className="flex flex-col gap-2">
          <span className="font-medium">Admin key</span>
          <div className="flex items-center gap-2">
            <code className="min-w-0 flex-1 overflow-x-auto rounded-lg border border-border bg-background px-3 py-2.5 font-mono text-sm">{created.adminApiKey}</code>
            <CopyButton value={created.adminApiKey} label="the admin key" />
          </div>
        </div>
      ) : (
        <form
          id="new-server"
          onSubmit={(event) => {
            event.preventDefault();
            if (kind) {
              create.mutate({ id: id.trim(), name: name.trim(), currency, kind });
            }
          }}
          className="flex flex-col gap-4"
        >
          <label className="flex flex-col gap-1.5">
            <span className="font-medium">Server</span>
            <input
              value={id}
              onChange={(event) => setId(event.target.value.toLowerCase())}
              placeholder="for example helix-markets"
              autoComplete="off"
              spellCheck={false}
              className={`${fieldClass} font-mono text-sm`}
            />
            <span className={`text-xs ${id.length > 0 && (!validId || availability.data?.available === false) ? "text-warning" : "text-muted"}`}>
              {id.length > 0 && !validId
                ? "2 to 63 lowercase letters, digits and dashes, starting with a letter or digit."
                : availability.data?.available === false
                  ? "That server is taken."
                  : "What traders type to log in. It cannot be changed later."}
            </span>
          </label>
          <label className="flex flex-col gap-1.5">
            <span className="font-medium">Firm name</span>
            <input value={name} onChange={(event) => setName(event.target.value)} maxLength={100} className={fieldClass} />
          </label>
          <label className="flex flex-col gap-1.5">
            <span className="font-medium">Account currency</span>
            <select value={currency} onChange={(event) => setCurrency(event.target.value)} className={fieldClass}>
              {(currencies.data ?? ["USD"]).map((c) => (
                <option key={c}>{c}</option>
              ))}
            </select>
          </label>
          <KindChoice value={kind} onChange={setKind} />
          <ErrorText error={create.error} />
        </form>
      )}
    </Modal>
  );
}

"use client";

import Link from "next/link";
import { useState } from "react";

import type { Server, StaffAccount, StaffGroup, TerminalKind } from "@/lib/api/types";
import { describeEvent } from "@/lib/events";
import { formatAgo, formatCount, formatCountOf, formatDate, formatDuration, formatMoney, formatTime, formatWhen } from "@/lib/format";
import { kindNames, partsShown } from "@/lib/kinds";
import { useAccount, useInstruments, useReplaceKey, useServer, useServerEvents, useSetKind, useSetListed } from "@/lib/queries";
import { useNow } from "@/lib/useNow";

import { KeyIcon, LateIcon } from "./icons";
import { KindChoice } from "./KindChoice";
import { LatestPanel } from "./OverviewPage";
import {
  buttonClass,
  CopyButton,
  dangerButtonClass,
  ErrorText,
  Facts,
  FigureCard,
  FigureGrid,
  fieldClass,
  Loading,
  Modal,
  Panel,
  secondaryButtonClass,
  Status,
  TableBox,
  tdClass,
  thClass,
} from "./ui";

/** One server: its figures, groups, terminal, login, key, message and latest events (ADR 0057). */
export function ServerPage({ id, account }: { id: string; account: string | null }) {
  const server = useServer(id);
  const now = useNow();
  const [listing, setListing] = useState(false);
  const [stoppingKey, setStoppingKey] = useState(false);
  const [changingKind, setChangingKind] = useState(false);

  if (server.isPending) {
    return <Loading label="Loading the server" />;
  }

  if (server.data === null) {
    return (
      <div className="flex flex-col gap-3">
        <Link href="/servers" className="text-sm text-accent hover:underline">
          Servers
        </Link>
        <p className="text-muted">There is no server called {id}.</p>
      </div>
    );
  }

  if (!server.data) {
    return <ErrorText error={server.error} />;
  }

  const data = server.data;
  const figures = data.figures;
  const madeBy =
    data.madeBy === "Partner" ? `made by ${data.partnerName}` : data.madeBy === "Staff" ? `made by ${data.createdBy}` : "from the service's configuration";
  return (
    <div className="flex flex-col gap-6">
      <Link href="/servers" className="text-sm text-accent hover:underline">
        Servers
      </Link>
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div className="flex max-w-3xl flex-col gap-1.5">
          <h1 className="font-serif text-4xl leading-tight">{data.name}</h1>
          <p className="leading-relaxed text-muted">
            <span className="font-mono text-[13px] text-foreground">{data.id}</span>, {madeBy}
            {data.createdAt && data.madeBy !== "Configuration" ? ` on ${formatDate(data.createdAt)}` : ""}.{" "}
            {data.listed ? "Listed" : "Not listed"}
            {data.loginUrl ? ", and its traders log in through its portal." : ", and its traders log in with a password."}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <button type="button" onClick={() => setListing(true)} className={secondaryButtonClass}>
            {data.listed ? "Stop listing" : "Put on the list"}
          </button>
          <button
            type="button"
            onClick={() => setStoppingKey(true)}
            disabled={data.madeBy === "Configuration"}
            title={data.madeBy === "Configuration" ? "A configured server's key is set in the configuration" : undefined}
            className={secondaryButtonClass}
          >
            <KeyIcon />
            New admin key
          </button>
        </div>
      </header>

      {data.events.stale && data.events.lastReadAt && (
        <div role="alert" className="flex flex-wrap items-start gap-3 rounded-2xl border border-warning/40 bg-warning/10 px-4 py-3.5">
          <LateIcon className="mt-0.5 size-5 text-warning" />
          <div className="flex min-w-0 flex-[1_1_28rem] flex-col gap-0.5">
            <strong className="font-semibold text-warning">
              Its system has not read events for {formatDuration((now.getTime() - new Date(data.events.lastReadAt).getTime()) / 1000)}
            </strong>
            <span className="leading-relaxed">
              Last asked at {formatTime(data.events.lastReadAt)}, for events after {formatCount(data.events.after ?? 0)}.{" "}
              {data.events.waiting > 1000 ? "More than 1,000" : formatCount(data.events.waiting)} events have come since.
              {data.madeBy === "Partner" ? ` ${data.partnerName} usually reads within a second, so its service is the place to look first.` : ""}
            </span>
          </div>
        </div>
      )}

      {account && <AccountCard serverId={data.id} accountId={account} />}

      <FigureGrid label="Key figures" count={5}>
        <FigureCard label="Traders" value={formatCount(figures.traders)} sub={`${formatCount(figures.newTraders)} new this week`} />
        <FigureCard label="Accounts trading" value={formatCount(figures.accountsTrading)} sub={`${formatCount(figures.accountsPaused)} paused, ${formatCount(figures.accountsClosed)} closed`} />
        <FigureCard label="Open positions" value={formatCount(figures.openPositions)} sub={`On ${formatCountOf(figures.accountsWithPositions, "account")}`} />
        <FigureCard label="Positions opened today" value={formatCount(figures.positionsOpenedToday)} sub={`${formatCountOf(figures.refusedToday, "request")} refused`} />
        <FigureCard label="Terminals open" value={formatCount(figures.terminalsOpen)} sub={`Watching ${formatCountOf(figures.accountsWatched, "account")}`} />
      </FigureGrid>

      <div className="grid items-start gap-4 lg:grid-cols-[3fr_2fr]">
        <div className="flex min-w-0 flex-col gap-4">
          {data.groups.map((group) => (
            <GroupPanel key={group.id} group={group} />
          ))}
          {data.groups.length === 0 && <Panel title="Groups">No groups.</Panel>}
        </div>
        <div className="flex min-w-0 flex-col gap-4">
          <TerminalPanel server={data} onChange={() => setChangingKind(true)} />
          <Panel title="Login">
            <Facts
              items={[
                { label: "Login list", value: data.listed ? (data.listedAt ? `Listed since ${formatDate(data.listedAt)}` : "Listed") : "Not listed" },
                {
                  label: "Traders log in",
                  value: data.loginUrl ? (
                    <span className="flex flex-col gap-0.5">
                      <span>Through the firm&apos;s portal</span>
                      <span className="break-all font-mono text-xs text-muted">{data.loginUrl}</span>
                    </span>
                  ) : (
                    "With a password, in the terminal"
                  ),
                },
                {
                  label: "Logo",
                  value: data.logoUrl ? (
                    // eslint-disable-next-line @next/next/no-img-element -- the firm's logo is on its own address
                    <img src={data.logoUrl} alt={`${data.name}'s logo`} className="size-8 rounded-lg border border-border bg-raised object-contain" />
                  ) : (
                    <span className="text-muted">None</span>
                  ),
                },
              ]}
            />
          </Panel>
          <Panel title="Admin key">
            <Facts
              items={[
                { label: "Held by", value: data.adminKey.heldBy ? `${data.adminKey.heldBy}, which made the server` : data.madeBy === "Configuration" ? "The configuration" : "The firm" },
                { label: "Made", value: data.adminKey.madeAt ? formatWhen(data.adminKey.madeAt, now) : "-" },
                { label: "Last used", value: data.adminKey.lastUsedAt ? formatAgo(data.adminKey.lastUsedAt, now) : "Not since the start" },
              ]}
            />
            <p className="text-sm leading-relaxed text-muted">We never see the key, only a hash of it.</p>
          </Panel>
          <Panel title="Message in its terminals">
            {data.notice ? (
              <div className="flex flex-col gap-1">
                <Status tone={data.notice.level === "Warning" ? "warning" : "accent"}>{data.notice.title}</Status>
                <p className="leading-relaxed">{data.notice.text}</p>
                <span className="text-xs text-muted">Since {formatWhen(data.notice.updatedAt, now)}</span>
              </div>
            ) : (
              <p className="leading-relaxed text-muted">None now. The firm&apos;s system sets it, and so does an incident from Kronant Prop&apos;s staff view.</p>
            )}
          </Panel>
        </div>
      </div>

      <EventsPanel server={data} account={account} />
      <LatestPanel entries={data.log} now={now} title="On this server" />

      <ListingDialog server={data} open={listing} onClose={() => setListing(false)} />
      <NewKeyDialog server={data} open={stoppingKey} onClose={() => setStoppingKey(false)} />
      <KindDialog server={data} open={changingKind} onClose={() => setChangingKind(false)} />
    </div>
  );
}

/** How the server's terminal works for its traders (ADR 0058): its type of business, what it shows and how orders go. */
function TerminalPanel({ server, onChange }: { server: Server; onChange: () => void }) {
  const terminal = server.terminal;
  const setBy =
    terminal.setBy === "Partner"
      ? `${server.partnerName} sets the type for its firms.`
      : terminal.setBy === "Configuration"
        ? "The service's configuration sets the type at every start."
        : "A new type reaches its terminals the next time they open.";
  return (
    <Panel
      title="Terminal"
      aside={
        terminal.setBy === "Staff" && (
          <button type="button" onClick={onChange} className="font-medium text-accent hover:underline">
            Change the type
          </button>
        )
      }
    >
      <Facts
        items={[
          { label: "Type", value: kindNames[terminal.kind] },
          { label: "Shows", value: partsShown(terminal.modules) },
          { label: "Orders", value: terminal.confirmOrders ? "Ask before they are sent" : "Go at the first click" },
        ]}
      />
      <p className="text-sm leading-relaxed text-muted">{setBy}</p>
    </Panel>
  );
}

/** Makes the server another type of business. Its terminals take the type's words and parts the next time they open. */
function KindDialog({ server, open, onClose }: { server: Server; open: boolean; onClose: () => void }) {
  const setKind = useSetKind(server.id);
  const [kind, setChosen] = useState<TerminalKind>(server.terminal.kind);
  const close = () => {
    setKind.reset();
    setChosen(server.terminal.kind);
    onClose();
  };

  return (
    <Modal
      open={open}
      onClose={close}
      title={
        <>
          Type of <span className="font-mono text-base">{server.id}</span>
        </>
      }
      description="The terminal's words, the parts it shows and whether orders ask first follow the type. The starting size, the login, the firm's pages and its risk warning stay as they are."
      footer={
        <>
          <button type="button" onClick={close} className={secondaryButtonClass}>
            Cancel
          </button>
          <button
            type="button"
            disabled={setKind.isPending || kind === server.terminal.kind}
            onClick={() => setKind.mutate(kind, { onSuccess: close })}
            className={buttonClass}
          >
            {setKind.isPending ? "Changing..." : "Change the type"}
          </button>
        </>
      }
    >
      <KindChoice value={kind} onChange={setChosen} />
      <ErrorText error={setKind.error} />
    </Modal>
  );
}

const shownSymbols = 8;

function GroupPanel({ group }: { group: StaffGroup }) {
  const [all, setAll] = useState(false);
  const symbols = all ? group.symbols : group.symbols.slice(0, shownSymbols);
  return (
    <Panel
      title={
        <>
          Group <span className="font-mono text-[13px] font-normal">{group.id}</span>
        </>
      }
      aside={`${group.currency}, ${formatCount(group.symbols.length)} symbols, ${formatCountOf(group.accounts, "account")}. ${group.changeable ? "The firm changes its own conditions." : "Set in the configuration."}`}
    >
      <TableBox minWidth={480}>
        <thead>
          <tr className="text-right">
            <th scope="col" className={`${thClass} text-left`}>
              Symbol
            </th>
            <th scope="col" className={thClass}>
              Leverage
            </th>
            <th scope="col" className={thClass}>
              Markup in points
            </th>
            <th scope="col" className={thClass}>
              Commission per lot and side
            </th>
            <th scope="col" className={thClass}>
              Open positions
            </th>
          </tr>
        </thead>
        <tbody>
          {symbols.map((symbol) => (
            <tr key={symbol.symbol} className="text-right">
              <th scope="row" className={`${tdClass} text-left font-semibold`}>
                {symbol.symbol}
              </th>
              <td className={tdClass}>1:{symbol.leverage}</td>
              <td className={tdClass}>{formatCount(symbol.spreadMarkupPoints)}</td>
              <td className={tdClass}>{symbol.commissionPerLotPerSide === 0 ? "None" : `${formatMoney(symbol.commissionPerLotPerSide)} ${group.currency}`}</td>
              <td className={`${tdClass} text-muted`}>{formatCount(symbol.openPositions)}</td>
            </tr>
          ))}
        </tbody>
      </TableBox>
      {group.symbols.length > shownSymbols && (
        <button type="button" onClick={() => setAll(!all)} className="self-start text-sm text-accent hover:underline">
          {all ? "Show fewer" : `Show all ${formatCount(group.symbols.length)} symbols`}
        </button>
      )}
    </Panel>
  );
}

/** The account a search found: its numbers, never who the trader is. */
function AccountCard({ serverId, accountId }: { serverId: string; accountId: string }) {
  const account = useAccount(accountId);
  const data: StaffAccount | null | undefined = account.data;
  return (
    <Panel
      title={<>Account #{accountId}</>}
      aside={
        <Link href={`/servers/${encodeURIComponent(serverId)}`} className="text-accent hover:underline">
          Show the whole server
        </Link>
      }
    >
      {account.isPending ? (
        <span className="skeleton block h-12 rounded-lg" />
      ) : !data ? (
        <p className="text-muted">There is no account #{accountId}.</p>
      ) : data.serverId !== serverId ? (
        <p className="text-muted">
          Account #{accountId} is on <Link href={`/servers/${encodeURIComponent(data.serverId)}?account=${encodeURIComponent(accountId)}`} className="text-accent hover:underline">{data.serverName}</Link>.
        </p>
      ) : (
        <dl className="grid grid-cols-[repeat(auto-fit,minmax(130px,1fr))] gap-3">
          {[
            { label: "Status", value: <Status tone={data.status === "Active" ? "profit" : data.status === "Suspended" ? "warning" : "muted"}>{data.status === "Active" ? "Trading" : data.status === "Suspended" ? "Paused" : "Closed"}</Status> },
            { label: "Balance", value: `${formatMoney(data.balance)} ${data.currency}` },
            { label: "Equity", value: `${formatMoney(data.equity)} ${data.currency}` },
            { label: "Margin in use", value: `${formatMoney(data.usedMargin)} ${data.currency}` },
            { label: "Open positions", value: formatCount(data.openPositions) },
            { label: "Pending orders", value: formatCount(data.pendingOrders) },
          ].map((fact) => (
            <div key={fact.label} className="flex flex-col gap-0.5">
              <dt className="text-[13px] text-muted">{fact.label}</dt>
              <dd className="font-semibold">{fact.value}</dd>
            </div>
          ))}
        </dl>
      )}
    </Panel>
  );
}

function EventsPanel({ server, account }: { server: Server; account: string | null }) {
  const events = useServerEvents(server.id, account);
  const instruments = useInstruments();
  const digits = new Map(instruments.data?.instruments.map((i) => [i.symbol, i.digits]));
  const now = useNow(5_000);
  const toneClass = { normal: "", warning: "text-warning", loss: "text-loss" };
  return (
    <Panel title={account ? `Latest events of #${account}` : "Latest events"} aside="The firm's own stream, as its system reads it">
      {events.isPending ? (
        <span className="skeleton block h-40 rounded-lg" />
      ) : events.isError ? (
        <ErrorText error={events.error} />
      ) : events.data.events.length === 0 ? (
        <p className="text-muted">No events yet.</p>
      ) : (
        <TableBox minWidth={760}>
          <thead>
            <tr>
              <th scope="col" className={thClass}>
                Event
              </th>
              <th scope="col" className={thClass}>
                Time
              </th>
              <th scope="col" className={thClass}>
                What
              </th>
              <th scope="col" className={thClass}>
                Account
              </th>
              <th scope="col" className={thClass}>
                Detail
              </th>
            </tr>
          </thead>
          <tbody>
            {events.data.events.map((envelope) => {
              const line = describeEvent(envelope, (symbol) => digits.get(symbol));
              return (
                <tr key={line.sequence} className={toneClass[line.tone]}>
                  <td className={`${tdClass} font-mono text-xs text-muted`}>{formatCount(line.sequence)}</td>
                  <td className={`${tdClass} whitespace-nowrap font-mono text-xs`}>{formatWhen(line.time, now)}</td>
                  <td className={`${tdClass} font-medium`}>{line.what}</td>
                  <td className={tdClass}>
                    {line.accountId ? (
                      <Link href={`/servers/${encodeURIComponent(server.id)}?account=${encodeURIComponent(line.accountId)}`} className="hover:text-accent">
                        #{line.accountId}
                      </Link>
                    ) : (
                      "-"
                    )}
                  </td>
                  <td className={`${tdClass} text-muted`}>{line.detail}</td>
                </tr>
              );
            })}
          </tbody>
        </TableBox>
      )}
    </Panel>
  );
}

function ListingDialog({ server, open, onClose }: { server: Server; open: boolean; onClose: () => void }) {
  const setListed = useSetListed(server.id);
  const listing = !server.listed;
  const close = () => {
    setListed.reset();
    onClose();
  };

  return (
    <Modal
      open={open}
      onClose={close}
      title={listing ? `Put ${server.id} on the list?` : `Take ${server.id} off the list?`}
      description={
        listing
          ? "Traders then see the firm on the terminal's login page."
          : `Traders no longer see the firm on the terminal's login page. Those who know the server still log in.${
              server.madeBy === "Partner" ? ` ${server.partnerName} may list it again, for example when the firm goes live.` : ""
            }`
      }
      footer={
        <>
          <button type="button" onClick={close} className={secondaryButtonClass}>
            Cancel
          </button>
          <button type="button" disabled={setListed.isPending} onClick={() => setListed.mutate(listing, { onSuccess: close })} className={buttonClass}>
            {listing ? "Put on the list" : "Stop listing"}
          </button>
        </>
      }
    >
      <ErrorText error={setListed.error} />
    </Modal>
  );
}

/** Stops the server's admin key, for when it may have leaked, and shows the new one once if we hold it. */
function NewKeyDialog({ server, open, onClose }: { server: Server; open: boolean; onClose: () => void }) {
  const replace = useReplaceKey(server.id);
  const [reason, setReason] = useState("");
  const [confirm, setConfirm] = useState("");
  const result = replace.data;
  const close = () => {
    replace.reset();
    setReason("");
    setConfirm("");
    onClose();
  };

  return (
    <Modal
      open={open}
      onClose={close}
      title={
        result ? (
          "The old key has stopped"
        ) : (
          <>
            New admin key for <span className="font-mono text-base">{server.id}</span>
          </>
        )
      }
      description={
        result
          ? result.adminApiKey
            ? "Give the new key to the firm's technical contact now. It is shown only this once, and we keep only a hash of it."
            : `${result.heldBy} asks for a new key by itself the next time the old one is turned away, within a minute, and saves it encrypted. Nobody sees the new key here.`
          : "The key lets the firm's system reach its traders, accounts and events. The old key stops working the moment you confirm, so use this when a key may have leaked."
      }
      footer={
        result ? (
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
              form="new-key"
              disabled={replace.isPending || reason.trim().length === 0 || confirm.trim() !== server.id}
              className={dangerButtonClass}
            >
              {replace.isPending ? "Stopping..." : "Stop the old key"}
            </button>
          </>
        )
      }
    >
      {result ? (
        result.adminApiKey && (
          <div className="flex items-center gap-2">
            <code className="min-w-0 flex-1 overflow-x-auto rounded-lg border border-border bg-background px-3 py-2.5 font-mono text-sm">{result.adminApiKey}</code>
            <CopyButton value={result.adminApiKey} label="the admin key" />
          </div>
        )
      ) : (
        <form
          id="new-key"
          onSubmit={(event) => {
            event.preventDefault();
            replace.mutate(reason.trim());
          }}
          className="flex flex-col gap-4"
        >
          {server.madeBy === "Partner" && (
            <p className="rounded-xl bg-raised px-4 py-3 leading-relaxed">
              {server.partnerName} made this server. When the old key is turned away, {server.partnerName} asks for a new one by itself, within a minute, and saves it encrypted. Nobody sees the new key here.
            </p>
          )}
          <label className="flex flex-col gap-1.5">
            <span className="font-medium">Why</span>
            <textarea
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              rows={2}
              maxLength={500}
              placeholder="For example: the key was in a log that was shared with a supplier"
              className={`${fieldClass} h-auto py-2.5 leading-relaxed`}
            />
            <span className="text-xs text-muted">Saved with your email in the platform&apos;s log</span>
          </label>
          <label className="flex flex-col gap-1.5">
            <span className="font-medium">
              Type <span className="font-mono text-sm">{server.id}</span> to confirm
            </span>
            <input value={confirm} onChange={(event) => setConfirm(event.target.value)} autoComplete="off" spellCheck={false} className={`${fieldClass} font-mono text-sm`} />
          </label>
          <ErrorText error={replace.error} />
        </form>
      )}
    </Modal>
  );
}

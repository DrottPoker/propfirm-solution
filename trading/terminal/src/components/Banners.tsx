"use client";

import { useCallback, useState } from "react";

import { backLink, endedText } from "@/lib/account";
import type { AccountDetails, ServerInfo } from "@/lib/api/types";
import type { ConnectionNotice } from "@/lib/connection";
import { formatClock, timeZoneName } from "@/lib/format";
import { useClock } from "@/lib/marketHours";
import { noticeParagraphs } from "@/lib/notices";
import { lockDescription, lockEvent, lockTitle } from "@/lib/ownLimits";
import { wordsFor } from "@/lib/profile";
import { useProfile } from "@/lib/profileContext";
import { ageText } from "@/lib/priceAge";
import { useNotice } from "@/lib/queries";
import { useSheet } from "@/lib/sheet";
import { useTradingStore } from "@/lib/store";
import { useTimeZone } from "@/lib/timeZone";

import { ErrorIcon, InfoIcon, LockIcon, OfflineIcon, OldPriceIcon, SuccessIcon } from "./icons";
import { Popover } from "./Popover";
import { useFeedStopped } from "./PriceAlerts";

// The terminal's messages sit in two lines at most under the account bar (ADR 0058): one for the account, such as an
// ended account or a locked day, and one for the platform, such as a lost connection, stopped prices or the firm's
// notice. Each is one line edge to edge, with the whole text behind Details, so the chart is never pushed down.

type Tone = "loss" | "warning" | "accent" | "profit" | "muted";

const tones: Record<Tone, { bar: string; icon: string; title: string }> = {
  loss: { bar: "border-loss/30 bg-loss/10", icon: "text-loss", title: "text-loss" },
  warning: { bar: "border-warning/30 bg-warning/10", icon: "text-warning", title: "text-warning" },
  accent: { bar: "border-accent/30 bg-accent/10", icon: "text-accent", title: "text-accent" },
  profit: { bar: "border-profit/30 bg-profit/10", icon: "text-profit", title: "text-profit" },
  muted: { bar: "border-border bg-panel", icon: "text-accent", title: "text-foreground" },
};

/** One message: a title, a line of text, and the whole of it with links for Details. */
interface Message {
  key: string;
  tone: Tone;
  Icon: (props: { className?: string }) => React.ReactNode;
  title: string;
  text?: string;
  /** Said as an alert, which a screen reader reads at once, rather than as news. */
  urgent?: boolean;
  testId?: string;
  paragraphs?: string[];
  actions?: React.ReactNode;
  links?: React.ReactNode;
}

/** The first message in one line, and every message in full behind Details, with how many more there are. */
function Banner({ messages }: { messages: Message[] }) {
  const [open, setOpen] = useState(false);
  const close = useCallback(() => setOpen(false), [setOpen]);
  const [anchor, setAnchor] = useState<HTMLButtonElement | null>(null);
  const [first] = messages;
  if (!first) {
    return null;
  }

  const tone = tones[first.tone];
  const more = messages.length - 1;
  const hasDetails = more > 0 || (first.paragraphs?.length ?? 0) > 0 || first.links !== undefined;

  return (
    <div
      role={first.urgent ? "alert" : "status"}
      data-testid={first.testId}
      className={`flex h-9 shrink-0 animate-fade items-center gap-2.5 border-b px-3 text-sm ${tone.bar}`}
    >
      <first.Icon className={`size-4 shrink-0 ${tone.icon}`} />
      <p className="flex min-w-0 flex-1 items-baseline gap-2.5">
        <span className={`min-w-0 shrink-0 truncate font-semibold ${tone.title}`}>{first.title}</span>
        {first.text && <span className="min-w-0 truncate text-foreground/80 max-sm:hidden">{first.text}</span>}
      </p>
      <span className="flex shrink-0 items-center gap-1">
        {first.actions}
        {hasDetails && (
          <button
            ref={setAnchor}
            type="button"
            aria-expanded={open}
            onClick={() => setOpen((o) => !o)}
            className="rounded-md px-2 py-1 text-xs font-medium text-muted transition-colors duration-150 hover:bg-raised hover:text-foreground"
          >
            {more > 0 ? `Details (${more + 1})` : "Details"}
          </button>
        )}
        {open && (
          <Popover anchor={anchor} label="Details" onClose={close} className="max-h-[min(28rem,70vh)] w-[min(30rem,calc(100vw-1rem))] gap-4 overflow-y-auto p-4">
            {messages.map((m) => (
              <section key={m.key} className="flex flex-col gap-1.5">
                <h3 className={`flex items-center gap-2 font-semibold ${tones[m.tone].title}`}>
                  <m.Icon className={`size-4 shrink-0 ${tones[m.tone].icon}`} />
                  {m.title}
                </h3>
                {(m.paragraphs ?? (m.text ? [m.text] : [])).map((p, i) => (
                  <p key={i} className="leading-relaxed text-foreground/85">
                    {p}
                  </p>
                ))}
                {m.links && <div className="flex flex-wrap gap-2 pt-1">{m.links}</div>}
              </section>
            ))}
          </Popover>
        )}
      </span>
    </div>
  );
}

const actionClass = "rounded-md px-2 py-1 text-xs font-medium transition-colors duration-150 hover:bg-raised";

/** The account's own state: that trading on it has ended and why, or that the trader's own lock holds today. */
export function AccountBanner({ details, server }: { details: AccountDetails | undefined; server: ServerInfo }) {
  const account = useTradingStore((s) => s.account);
  const events = useTradingStore((s) => s.events);
  const timeZone = useTimeZone();
  const now = useClock();
  const openSheet = useSheet((s) => s.open);
  const profile = useProfile();

  if (account?.status === "Disabled") {
    const all = events.map((e) => e.event);
    const broken = profile.modules.breachReports && all.some((e) => e.kind === "EquityFloorBreached");
    const link = backLink(details, server);
    return (
      <Banner
        messages={[
          {
            key: "ended",
            testId: "account-ended",
            tone: "loss",
            Icon: ErrorIcon,
            urgent: true,
            title: "Trading on this account has ended",
            text: endedText(all, timeZone, now),
            actions: (
              <>
                {broken && (
                  <button type="button" onClick={() => openSheet({ kind: "breach" })} className={`${actionClass} text-loss`}>
                    Breach report
                  </button>
                )}
                {link && (
                  <a href={link} className={`${actionClass} text-foreground max-sm:hidden`}>
                    See the account at {server.name}
                  </a>
                )}
              </>
            ),
          },
        ]}
      />
    );
  }

  const own = account?.ownLimits;
  if (own?.lock) {
    const lockTimeZone = own.tradingDay.timeZone;
    const locked = lockEvent(
      events.map((e) => e.event),
      own.lock,
    );
    return (
      <Banner
        messages={[
          {
            key: "lock",
            testId: "own-lock",
            tone: "accent",
            Icon: LockIcon,
            title: lockTitle(own.lock, locked?.timestamp ?? null, lockTimeZone),
            text: lockDescription(own.lock, locked?.positionsClosed ?? null, lockTimeZone, wordsFor(profile.kind).carriesOn),
          },
        ]}
      />
    );
  }

  return null;
}

/**
 * The platform's messages, the most pressing first: a lost connection, prices that stopped coming, and the firm's
 * notice for its terminals, such as what it does about an outage (ADR 0053).
 */
export function PlatformBanner({ accountId, serverName, connection }: { accountId: string; serverName: string; connection: ConnectionNotice }) {
  const stopped = useFeedStopped(accountId);
  const notice = useNotice(accountId).data ?? null;
  const timeZone = useTimeZone();
  const messages: Message[] = [];

  if (connection === "lost" || connection === "unreachable") {
    messages.push({
      key: "connection",
      tone: "warning",
      Icon: OfflineIcon,
      urgent: true,
      title: connection === "lost" ? `Connection to ${serverName} lost` : `Cannot reach ${serverName}`,
      text:
        connection === "lost"
          ? "Trying again. Prices and the account are not updating, and new orders wait until it is back."
          : "Trying again. Check the internet connection if this goes on.",
    });
  } else if (connection === "back") {
    messages.push({ key: "connection", tone: "profit", Icon: SuccessIcon, title: "Connected again", text: "Prices and the account are up to date." });
  }

  if (stopped !== null) {
    messages.push({
      key: "feed",
      tone: "warning",
      Icon: OldPriceIcon,
      urgent: true,
      title: `No new prices since ${formatClock(stopped.since, timeZone)} ${timeZoneName(timeZone)}, ${ageText(stopped.ms)} ago`,
      text: "New orders, closes and stop changes wait for prices, so nothing fills at an old price.",
      paragraphs: [
        "The price feed has stopped. Until prices return, new orders, closes and stop changes are refused, so nothing is filled at an old price.",
        "Stops and loss limits are checked again on the first new price.",
      ],
    });
  }

  if (notice) {
    const paragraphs = noticeParagraphs(notice.text);
    messages.push({
      key: "notice",
      tone: notice.level === "Warning" ? "warning" : "muted",
      Icon: InfoIcon,
      urgent: notice.level === "Warning",
      title: notice.title,
      text: paragraphs[0],
      paragraphs,
      links: notice.url ? (
        <a href={notice.url} target="_blank" rel="noreferrer" className="text-xs font-medium text-accent hover:underline">
          Read more on the status page
        </a>
      ) : undefined,
    });
  }

  return <Banner messages={messages} />;
}

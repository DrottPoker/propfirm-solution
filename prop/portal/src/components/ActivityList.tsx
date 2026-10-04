import Link from "next/link";

import { activityView, whenText, type ActivityIcon, type StatusTone } from "@/lib/admin";
import type { Activity } from "@/lib/api/types";

import { BagIcon, CheckIcon, ClockIcon, CrossIcon, DoubleCheckIcon, PayoutIcon, PlayIcon, ShieldCheckIcon } from "./icons";

const icons: Record<ActivityIcon, (props: { className?: string }) => React.ReactNode> = {
  start: PlayIcon,
  bag: BagIcon,
  check: CheckIcon,
  shield: ShieldCheckIcon,
  cross: CrossIcon,
  clock: ClockIcon,
  payout: PayoutIcon,
  paid: DoubleCheckIcon,
};

/** A tone's tinted circle behind an icon. */
export const toneMarks: Record<StatusTone, string> = {
  accent: "bg-accent/15 text-accent",
  profit: "bg-profit/15 text-profit",
  loss: "bg-loss/15 text-loss",
  warning: "bg-warning/15 text-warning",
  muted: "bg-muted/15 text-muted",
};

/** What happened to the firm's accounts lately, the newest first, each with its trader and account. */
export function ActivityList({ activity, now }: { activity: Activity[]; now: number }) {
  if (activity.length === 0) {
    return <p className="text-sm text-muted">Nothing has happened in the last 30 days.</p>;
  }

  return (
    <ol className="flex flex-col">
      {activity.map((item) => {
        const view = activityView(item);
        const Icon = icons[view.icon];
        return (
          <li key={`${item.kind}-${item.accountId}-${item.time}`} className="flex items-start gap-3.5 border-t border-border py-3 first:border-t-0">
            <span aria-hidden="true" className={`grid size-7 shrink-0 place-items-center rounded-full ${toneMarks[view.tone]}`}>
              <Icon className="size-3.5" />
            </span>
            <span className="flex min-w-0 flex-1 flex-col gap-0.5">
              <span>
                <span className="font-medium">{view.title}</span>
                {view.note && <span className="text-muted"> · {view.note}</span>}
              </span>
              <span className="truncate text-xs text-muted">
                {item.email} ·{" "}
                <Link href={`/admin/accounts/${item.accountId}`} className="font-mono text-accent hover:underline">
                  #{item.accountNumber}
                </Link>{" "}
                {item.challengeName}
              </span>
            </span>
            <time dateTime={item.time} className="shrink-0 text-xs text-muted">
              {whenText(item.time, now)}
            </time>
          </li>
        );
      })}
    </ol>
  );
}

"use client";

import { useRouter } from "next/navigation";

import { productName } from "@/lib/config";
import { formatMoney, formatPercent } from "@/lib/format";
import { useLogout } from "@/lib/queries";
import { useTradingStore, type ConnectionState } from "@/lib/store";

const connectionStyles: Record<ConnectionState, { label: string; dot: string }> = {
  connecting: { label: "Connecting", dot: "bg-warning" },
  connected: { label: "Live", dot: "bg-profit" },
  reconnecting: { label: "Reconnecting", dot: "bg-warning" },
  disconnected: { label: "Offline", dot: "bg-loss" },
};

/**
 * Account figures and the distance to every equity floor, so the trader always sees the limits. A trader
 * with several accounts, such as one per challenge stage, switches between them here.
 */
export function AccountBar({ accounts, email, serverName }: { accounts: string[]; email: string; serverName: string }) {
  const account = useTradingStore((s) => s.account);
  const connection = connectionStyles[useTradingStore((s) => s.connection)];
  const logout = useLogout();
  const router = useRouter();

  return (
    <header className="flex flex-wrap items-center gap-x-6 gap-y-1 bg-panel px-4 py-2 text-sm">
      <span className="font-semibold">{productName}</span>
      <span className="flex items-center gap-2 text-muted" title="Connection to the trading service">
        <span className={`size-2 rounded-full ${connection.dot}`} />
        {connection.label}
      </span>

      {account && (
        <>
          <span className="flex items-center gap-1">
            {accounts.length > 1 ? (
              <select
                aria-label="Account"
                value={account.accountId}
                onChange={(e) => router.push(`/?account=${encodeURIComponent(e.target.value)}`)}
                className="rounded border border-border bg-background px-1 py-0.5 font-medium"
              >
                {accounts.map((id) => (
                  <option key={id} value={id}>
                    {id}
                  </option>
                ))}
              </select>
            ) : (
              <span className="font-medium">{account.accountId}</span>
            )}
            <span className="text-muted" title="The firm's server">
              {serverName}
            </span>
          </span>
          {account.status === "Disabled" && <span className="rounded bg-loss/20 px-2 py-0.5 text-loss">Disabled</span>}
          <Figure label="Balance" value={`${formatMoney(account.balance)} ${account.currency}`} />
          <Figure label="Equity" value={formatMoney(account.equity)} />
          <Figure label="Margin" value={formatMoney(account.usedMargin)} />
          <Figure label="Free margin" value={formatMoney(account.freeMargin)} />
          <Figure label="Margin level" value={formatPercent(account.marginLevelPercent)} />
          {account.floors.map((floor) => (
            <Figure
              key={floor.floorId}
              label={`Floor ${floor.floorId}`}
              value={`${formatMoney(floor.level)} (${formatMoney(floor.headroom)} left)`}
            />
          ))}
        </>
      )}

      <span className="ml-auto flex items-center gap-3 text-muted">
        {email}
        <button
          type="button"
          className="hover:text-foreground"
          disabled={logout.isPending}
          onClick={() => logout.mutate(undefined, { onSuccess: () => router.replace("/login") })}
        >
          Log out
        </button>
      </span>
    </header>
  );
}

function Figure({ label, value }: { label: string; value: string }) {
  return (
    <span className="flex gap-2">
      <span className="text-muted">{label}</span>
      <span className="font-mono tabular-nums">{value}</span>
    </span>
  );
}

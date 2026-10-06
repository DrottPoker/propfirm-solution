import type { AccountDetails } from "@/lib/api/types";
import { formatMoney } from "@/lib/format";
import { ruleTable, type RuleRow } from "@/lib/ruleTable";

import { Panel } from "./ui";

/**
 * The rules of every phase of the challenge, as it was bought. Later changes by the firm do not touch it. A rule that
 * is the same in every phase is said once. A table on wider screens and a list on a phone, so nothing is cut off. The
 * firm sees them about its trader.
 */
export function ChallengeRules({ details, audience = "trader" }: { details: AccountDetails; audience?: "trader" | "firm" }) {
  const { challenge, account } = details;
  const { columns, rows } = ruleTable(details, audience);
  return (
    <Panel title={audience === "firm" ? "Rules the trader bought" : "Rules of this challenge"}>
      <table className="hidden w-full text-sm sm:table">
        <thead className="text-left text-muted">
          <tr>
            <td className="py-2" />
            {columns.map((column) => (
              <th key={column.stage} scope="col" className={`py-2 pl-4 font-normal ${column.stage === account.stage ? "font-medium text-foreground" : ""}`}>
                {column.name}
                {column.now && <span className="ml-1.5 rounded-full bg-accent/15 px-1.5 py-0.5 text-[0.7rem] font-medium text-accent">now</span>}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.label} className="border-t border-border align-top">
              <th scope="row" className="w-44 py-2.5 pr-2 text-left font-normal text-muted">
                {row.label}
              </th>
              {row.same !== null ? (
                <td colSpan={columns.length} className="py-2.5 pl-4">
                  {row.same} <span className="text-xs text-muted">· every phase</span>
                </td>
              ) : (
                row.values.map((value, index) => (
                  <td key={columns[index].stage} className={`py-2.5 pl-4 ${value === null ? "text-muted" : ""}`}>
                    {value ?? "-"}
                  </td>
                ))
              )}
            </tr>
          ))}
        </tbody>
      </table>

      <dl className="flex flex-col divide-y divide-border text-sm sm:hidden">
        {rows.map((row) => (
          <div key={row.label} className="flex flex-col gap-1 py-2.5 first:pt-0">
            <dt className="text-muted">{row.label}</dt>
            <RuleValues row={row} names={columns.map((c) => c.name)} />
          </div>
        ))}
      </dl>

      <p className="text-xs text-muted">
        A trading day starts at {challenge.tradingDay.start.slice(0, 5)} in {challenge.tradingDay.timeZone.replaceAll("_", " ")}.
        {challenge.inactivityDays != null && ` The challenge ends if no new trade is opened for ${challenge.inactivityDays} days.`} The account starts at{" "}
        {formatMoney(challenge.initialBalance)} {challenge.currency} in every phase.
        {account.tradingAccountId && (
          <>
            {" "}
            The trading account now is <span className="font-mono text-foreground">{account.tradingAccountId}</span>.
          </>
        )}
      </p>
    </Panel>
  );
}

// On a phone: the one value, or a line per phase where the rule applies.
function RuleValues({ row, names }: { row: RuleRow; names: string[] }) {
  if (row.same !== null) {
    return (
      <dd>
        {row.same} <span className="text-xs text-muted">· every phase</span>
      </dd>
    );
  }

  return (
    <>
      {row.values.map((value, index) =>
        value === null ? null : (
          <dd key={names[index]}>
            <span className="text-muted">{names[index]}:</span> {value}
          </dd>
        ),
      )}
    </>
  );
}

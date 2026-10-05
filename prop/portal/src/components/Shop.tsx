"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";

import type { Me, ShopItem } from "@/lib/api/types";
import { countries } from "@/lib/countries";
import { formatMoney } from "@/lib/format";
import { useCreateOrder, useDiscountQuote, useMe, useShop } from "@/lib/queries";
import { shopTables, sizeLabel, type ShopTable } from "@/lib/shop";

import { FirmName } from "./FirmName";
import { buttonClass, ErrorText, fieldClass, Message, Panel, secondaryButtonClass } from "./ui";

/**
 * The challenges the firm sells, as price tables: the account sizes as columns and the rules as rows. The buyer picks a
 * size and goes on to pay with the firm's payment provider. Visitors come here first, with a way to log in. A link can
 * choose the challenge and fill in a discount code.
 */
export function Shop({ challenge = null, code = null }: { challenge?: string | null; code?: string | null }) {
  const shop = useShop();
  const me = useMe("trader");
  const [chosen, setChosen] = useState<string | null>(challenge);

  if (shop.isError) {
    return <Message text="The challenges cannot be loaded right now. Try again shortly." />;
  }

  if (!shop.data || me.isPending) {
    return <Message text="Loading..." />;
  }

  const { items, open, full, test, teamOnly, termsUrl } = shop.data;
  const tables = open ? shopTables(items) : [];
  const chosenItem = items.find((i) => i.challenge.id === chosen) ?? null;
  return (
    <main className="mx-auto flex w-full max-w-5xl flex-col gap-6 p-4 sm:p-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-col gap-1">
          <FirmName size="lg" />
          <h1 className="text-sm text-muted">Buy a challenge</h1>
        </div>
        <Link href={me.data ? "/" : "/login"} className={me.data ? "text-sm text-muted hover:text-foreground" : `${buttonClass} text-sm`}>
          {me.data ? "Your accounts" : "Log in"}
        </Link>
      </header>

      {!open && <p className="text-muted">{full ? "No new challenges can be bought right now. Try again later." : "No challenges are for sale here right now."}</p>}
      {open && teamOnly && (
        <p role="note" className="rounded border border-warning/40 bg-warning/10 px-4 py-2 text-sm text-warning">
          This shop does not sell yet. Only the firm&apos;s own team can buy here, with their own email, to try it.
        </p>
      )}
      {test && (
        <p role="note" className="rounded border border-warning/40 bg-warning/10 px-4 py-2 text-sm text-warning">
          Test payments: you pay on a test page, and no money is taken.
        </p>
      )}

      {tables.map((table) => (
        <PriceTable key={table.key} table={table} chosen={chosen} onChoose={setChosen} />
      ))}

      {chosenItem && (
        <Checkout
          key={chosenItem.challenge.id}
          item={chosenItem}
          me={me.data ?? null}
          termsUrl={termsUrl}
          initialCode={chosenItem.challenge.id === challenge ? code : null}
          onCancel={() => setChosen(null)}
        />
      )}
    </main>
  );
}

/** One table of challenges with the same rules. On a phone it scrolls sideways, with the rules' names kept in view. */
function PriceTable({ table, chosen, onChoose }: { table: ShopTable; chosen: string | null; onChoose: (id: string) => void }) {
  return (
    <section aria-labelledby={`table-${table.key}`} className="flex flex-col gap-3 rounded-lg border border-border bg-panel p-4 sm:p-5">
      <h2 id={`table-${table.key}`} className="text-lg font-semibold">
        {table.title}
      </h2>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr>
              <th scope="col" className="sticky left-0 min-w-28 bg-panel py-2 pr-4 text-left font-normal text-muted">
                Account size
              </th>
              {table.items.map((item) => (
                <th key={item.challenge.id} scope="col" className="px-3 py-2 text-right font-mono text-base font-semibold tabular-nums">
                  {sizeLabel(item.challenge.initialBalance)}
                  <span className="block text-xs font-normal text-muted">{item.challenge.currency}</span>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {table.rows.map((row, index) => (
              <tr key={row.label} className="border-t border-border">
                <th scope="row" className="sticky left-0 min-w-28 bg-panel py-2 pr-4 text-left font-normal text-muted">
                  {row.label}
                </th>
                {row.values.map((value, i) => (
                  <td key={table.items[i].challenge.id} className={`min-w-28 px-3 py-2 text-right tabular-nums ${index === 0 ? "font-mono font-semibold" : ""}`}>
                    {value}
                  </td>
                ))}
              </tr>
            ))}
            <tr className="border-t border-border">
              <td className="sticky left-0 bg-panel py-3 pr-4" />
              {table.items.map((item) => (
                <td key={item.challenge.id} className="px-3 py-3 text-right">
                  <button
                    type="button"
                    aria-label={`Buy ${item.challenge.name}`}
                    aria-pressed={chosen === item.challenge.id}
                    onClick={() => onChoose(item.challenge.id)}
                    className={chosen === item.challenge.id ? `${buttonClass} ring-2 ring-accent ring-offset-2 ring-offset-panel` : buttonClass}
                  >
                    Buy
                  </button>
                </td>
              ))}
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  );
}

/**
 * The buyer's email, name and country, or the logged-in trader's, a discount code and the firm's terms. Then on to the
 * payment page. A trader who gave the name and country before is not asked again.
 */
function Checkout({
  item,
  me,
  termsUrl,
  initialCode,
  onCancel,
}: {
  item: ShopItem;
  me: Me | null;
  termsUrl: string | null;
  initialCode: string | null;
  onCancel: () => void;
}) {
  const order = useCreateOrder();
  const quote = useDiscountQuote();
  const [code, setCode] = useState(initialCode ?? "");
  const [buyerEmail, setBuyerEmail] = useState("");
  const [name, setName] = useState(me?.name ?? "");
  const [country, setCountry] = useState(me?.country ?? "");
  const [accepted, setAccepted] = useState(false);
  const askDetails = !me?.name || !me.country;
  const form = useRef<HTMLFormElement>(null);

  // The form comes after the tables, so the buyer is taken to it.
  useEffect(() => {
    form.current?.scrollIntoView({ behavior: "smooth", block: "nearest" });
  }, []);

  // A code from the link is applied at once, so the buyer sees the price with it.
  const applyCode = () => {
    if (code.trim()) {
      quote.mutate({ code: code.trim(), challengeId: item.challenge.id, email: me?.email ?? (buyerEmail || null) });
    } else {
      quote.reset();
    }
  };
  const applied = useRef(false);
  useEffect(() => {
    if (!applied.current && initialCode) {
      applied.current = true;
      quote.mutate({ code: initialCode, challengeId: item.challenge.id, email: me?.email ?? null });
    }
  }, [initialCode, item.challenge.id, me?.email, quote]);

  const discounted = quote.data && quote.data.code.toUpperCase() === code.trim().toUpperCase() ? quote.data : null;
  const price = discounted?.amount ?? item.price;

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    order.mutate(
      {
        challengeId: item.challenge.id,
        email: me?.email ?? buyerEmail,
        acceptTerms: accepted,
        name: name.trim() || null,
        country: country || null,
        discountCode: code.trim() || null,
      },
      { onSuccess: (created) => window.location.assign(created.checkoutUrl) },
    );
  };

  return (
    <Panel title={`${item.challenge.name}: ${formatMoney(item.price)} ${item.currency}`}>
      <form ref={form} onSubmit={submit} className="flex flex-col gap-4 text-sm">
        {me ? (
          <p className="text-muted">
            You buy as <span className="text-foreground">{me.email}</span>, and the challenge shows up among your accounts.
          </p>
        ) : (
          <label className="flex flex-col gap-1">
            <span className="text-muted">Your email</span>
            <input type="email" required autoComplete="email" value={buyerEmail} onChange={(e) => setBuyerEmail(e.target.value)} className={fieldClass} />
            <span className="text-xs text-muted">You choose your password right after paying.</span>
          </label>
        )}
        {askDetails && (
          <div className="grid gap-4 sm:grid-cols-2">
            <label className="flex flex-col gap-1">
              <span className="text-muted">Your name</span>
              <input required autoComplete="name" maxLength={100} value={name} onChange={(e) => setName(e.target.value)} className={fieldClass} />
            </label>
            <label className="flex flex-col gap-1">
              <span className="text-muted">Country</span>
              <select aria-label="Country" required autoComplete="country" value={country} onChange={(e) => setCountry(e.target.value)} className={fieldClass}>
                <option value="">Choose your country</option>
                {countries.map((c) => (
                  <option key={c.code} value={c.code}>
                    {c.name}
                  </option>
                ))}
              </select>
            </label>
          </div>
        )}
        <div className="flex flex-col gap-1">
          <label htmlFor="discount-code" className="text-muted">
            Discount code <span className="text-xs">(optional)</span>
          </label>
          <div className="flex flex-wrap gap-2">
            <input
              id="discount-code"
              value={code}
              onChange={(e) => setCode(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  e.preventDefault();
                  applyCode();
                }
              }}
              autoComplete="off"
              className={`${fieldClass} w-48 font-mono uppercase`}
            />
            <button type="button" onClick={applyCode} disabled={quote.isPending} className={secondaryButtonClass}>
              {quote.isPending ? "Checking..." : "Apply"}
            </button>
          </div>
          {discounted ? (
            <span role="status" className="text-xs text-profit">
              {formatMoney(discounted.discount)} {discounted.currency} off, so you pay {formatMoney(discounted.amount)} instead of {formatMoney(discounted.listAmount)}.
              {discounted.forRetries && " The code is for a new try, so buy with the email of your earlier challenge."}
            </span>
          ) : (
            <ErrorText error={quote.error} />
          )}
        </div>
        {termsUrl && (
          <label className="flex items-start gap-2">
            <input type="checkbox" required checked={accepted} onChange={(e) => setAccepted(e.target.checked)} className="mt-1" />
            <span>
              I accept the{" "}
              <a href={termsUrl} target="_blank" rel="noreferrer" className="text-accent hover:underline">
                terms
              </a>
              .
            </span>
          </label>
        )}
        <ErrorText error={order.error} />
        <div className="flex flex-wrap items-center gap-4">
          <button type="submit" disabled={order.isPending || order.isSuccess} className={buttonClass}>
            {order.isPending || order.isSuccess ? "Going to payment..." : `Pay ${formatMoney(price)} ${item.currency}`}
          </button>
          <button type="button" onClick={onCancel} className="text-muted hover:text-foreground">
            Choose another
          </button>
        </div>
      </form>
    </Panel>
  );
}

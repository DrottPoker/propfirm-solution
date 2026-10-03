"use client";

import Link from "next/link";
import { useState } from "react";

import type { ShopItem } from "@/lib/api/types";
import { formatMoney } from "@/lib/format";
import { useCreateOrder, useMe, useShop } from "@/lib/queries";

import { ChallengeSummary } from "./ChallengeSummary";
import { FirmName } from "./FirmName";
import { buttonClass, ErrorText, fieldClass, Message, Panel } from "./ui";

/** The challenges the firm sells. The buyer picks one and goes on to pay with the firm's payment provider. */
export function Shop() {
  const shop = useShop();
  const me = useMe("trader");
  const [chosen, setChosen] = useState<string | null>(null);

  if (shop.isError) {
    return <Message text="The challenges cannot be loaded right now. Try again shortly." />;
  }

  if (!shop.data || me.isPending) {
    return <Message text="Loading..." />;
  }

  const { items, open, test, termsUrl } = shop.data;
  const email = me.data?.email ?? null;
  return (
    <main className="mx-auto flex w-full max-w-5xl flex-col gap-6 p-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-col gap-1">
          <FirmName size="lg" />
          <h1 className="text-sm text-muted">Buy a challenge</h1>
        </div>
        <Link href={email ? "/" : "/login"} className="text-sm text-muted hover:text-foreground">
          {email ? "Your accounts" : "Log in"}
        </Link>
      </header>

      {!open && <p className="text-muted">No challenges are for sale here right now.</p>}
      {test && (
        <p role="note" className="rounded border border-warning/40 bg-warning/10 px-4 py-2 text-sm text-warning">
          Test payments: you pay on a test page, and no money is taken.
        </p>
      )}

      <ul className="grid gap-4 md:grid-cols-2">
        {items.map((item) => (
          <li key={item.challenge.id}>
            <Panel>
              <ChallengeSummary challenge={item.challenge} showId={false} />
              <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border pt-4">
                <span className="font-mono text-xl tabular-nums">
                  {formatMoney(item.price)} {item.currency}
                </span>
                {chosen !== item.challenge.id && (
                  <button type="button" onClick={() => setChosen(item.challenge.id)} className={buttonClass}>
                    Buy
                  </button>
                )}
              </div>
              {chosen === item.challenge.id && <Checkout item={item} email={email} termsUrl={termsUrl} />}
            </Panel>
          </li>
        ))}
      </ul>
    </main>
  );
}

/** The buyer's email, or the logged-in trader's, and the firm's terms. Then on to the payment page. */
function Checkout({ item, email, termsUrl }: { item: ShopItem; email: string | null; termsUrl: string | null }) {
  const order = useCreateOrder();
  const [buyerEmail, setBuyerEmail] = useState("");
  const [accepted, setAccepted] = useState(false);

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    order.mutate(
      { challengeId: item.challenge.id, email: email ?? buyerEmail, acceptTerms: accepted },
      { onSuccess: (created) => window.location.assign(created.checkoutUrl) },
    );
  };

  return (
    <form onSubmit={submit} className="flex flex-col gap-3 text-sm">
      {email ? (
        <p className="text-muted">
          You buy as <span className="text-foreground">{email}</span>, and the challenge shows up among your accounts.
        </p>
      ) : (
        <label className="flex flex-col gap-1">
          <span className="text-muted">Your email</span>
          <input type="email" required autoComplete="email" value={buyerEmail} onChange={(e) => setBuyerEmail(e.target.value)} className={fieldClass} />
          <span className="text-xs text-muted">After paying you get an email with a link to choose your password.</span>
        </label>
      )}
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
      <button type="submit" disabled={order.isPending || order.isSuccess} className={`${buttonClass} self-start`}>
        {order.isPending || order.isSuccess ? "Going to payment..." : `Pay ${formatMoney(item.price)} ${item.currency}`}
      </button>
    </form>
  );
}

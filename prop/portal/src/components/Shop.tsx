"use client";

import NumberFlow from "@number-flow/react";
import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";

import { useBranding } from "@/app/providers";
import type { Me, ShopItem } from "@/lib/api/types";
import { countries } from "@/lib/countries";
import { formatMoney, priceText, wholeAmount } from "@/lib/format";
import { useCreateOrder, useDiscountQuote, useMe, useShop } from "@/lib/queries";
import { payoutLines, shopQuestions, shopRules, shopTables, sizeLabel, type ShopRule, type ShopTable } from "@/lib/shop";

import { Sheet } from "./Dialog";
import { FirmName } from "./FirmName";
import { BagIcon, ChartIcon, CheckIcon, ChevronRightIcon, ClockIcon, InfoIcon, LockIcon, PayoutIcon, PlusIcon, ShieldCheckIcon, TrophyIcon } from "./icons";
import { PhaseJourney } from "./PhaseJourney";
import { buttonClass, EmptyState, ErrorText, fieldClass, Loading, Message, SegmentedControl, secondaryButtonClass } from "./ui";

/**
 * The firm's shop (ADR 0047): what a trader gets in a few words, the way from evaluation to payout, each program as a
 * card with its sizes, price and main rules, every rule side by side, and the questions buyers ask. Buying opens a panel
 * from the side with the order and the buyer's details, and goes on to the firm's payment provider. Visitors come here
 * first, with a way to log in. A link can choose the challenge and fill in a discount code.
 */
export function Shop({ challenge = null, code = null }: { challenge?: string | null; code?: string | null }) {
  const shop = useShop();
  const me = useMe("trader");
  const branding = useBranding();
  const [chosen, setChosen] = useState<string | null>(challenge);

  if (shop.isError) {
    return <Message text="The challenges cannot be loaded right now. Try again shortly." />;
  }

  if (!shop.data || me.isPending) {
    return <Loading />;
  }

  const { items, open, full, test, teamOnly, termsUrl, payouts } = shop.data;
  const tables = open ? shopTables(items) : [];
  const chosenItem = items.find((i) => i.challenge.id === chosen) ?? null;
  const splits = items.map((i) => i.challenge.funded.profitSplitPercent).filter((split): split is number => split != null);
  const split = splits.length > 0 ? Math.max(...splits) : null;
  return (
    <main className="mx-auto flex w-full max-w-6xl flex-col gap-10 px-4 py-6 sm:px-6 sm:py-8">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <FirmName size="lg" />
        <Link href={me.data ? "/" : "/login"} className={`${secondaryButtonClass} text-sm`}>
          {me.data ? "Your accounts" : "Log in"}
        </Link>
      </header>

      <section className="stagger flex flex-col gap-4">
        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-accent">Buy a challenge</p>
        <h1 className="max-w-3xl font-serif text-[2.6rem] leading-[1.05] tracking-tight sm:text-6xl">
          Trade our capital.{split !== null && <> Keep {split}% of the profit.</>}
        </h1>
        <p className="max-w-2xl text-lg text-muted">Prove your trading in an evaluation with {branding.name}, get a funded account, and ask for payouts.</p>
        {payouts && (
          <ul aria-label={`What ${branding.name} paid out`} className="flex flex-wrap gap-x-6 gap-y-2 text-sm">
            {payoutLines(payouts).map((line, index) => (
              <li key={line} className="flex items-center gap-2">
                {index === 0 ? <PayoutIcon className="size-4 text-profit" /> : <ClockIcon className="size-4 text-profit" />}
                {line}
              </li>
            ))}
          </ul>
        )}
        {(teamOnly || test) && (
          <p role="note" className="flex items-start gap-2 self-start rounded-lg border border-accent/25 bg-accent/[0.07] px-3 py-2 text-sm text-foreground/80">
            <InfoIcon className="mt-0.5 size-4 text-accent" />
            <span>
              {teamOnly && "This shop does not sell yet. Only the firm's own team can buy here, with their own email, to try it. "}
              {test && "Test payments: you pay on a test page, and no money is taken."}
            </span>
          </p>
        )}
      </section>

      {open && <Journey split={split} />}

      {!open && (
        <EmptyState
          icon={<BagIcon className="size-6" />}
          title={full ? "No new challenges can be bought right now" : "No challenges are for sale here right now"}
          text={full ? "Try again later." : undefined}
        />
      )}

      {tables.length > 0 && (
        <section aria-label="Challenges" className="stagger grid grid-cols-1 gap-5 md:grid-cols-2 xl:grid-cols-3">
          {tables.map((table) => (
            <ProgramCard key={table.key} table={table} initial={challenge} onBuy={setChosen} />
          ))}
        </section>
      )}

      {tables.length > 0 && (
        <details className="group rounded-xl border border-border bg-panel shadow-card">
          <summary className="flex cursor-pointer list-none items-center justify-between gap-3 px-5 py-4 font-medium">
            Compare every rule
            <ChevronRightIcon className="size-4 text-muted transition-transform duration-200 group-open:rotate-90" />
          </summary>
          <div className="flex flex-col gap-6 border-t border-border px-5 py-5">
            {tables.map((table) => (
              <PriceTable key={table.key} table={table} chosen={chosen} onChoose={setChosen} />
            ))}
          </div>
        </details>
      )}

      {tables.length > 0 && <Questions items={items} firmName={branding.name} />}

      <Sheet
        open={chosenItem !== null}
        onClose={() => setChosen(null)}
        title={chosenItem ? chosenItem.challenge.name : ""}
        description={chosenItem ? `${formatMoney(chosenItem.challenge.initialBalance)} ${chosenItem.challenge.currency} account` : undefined}
      >
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
      </Sheet>
    </main>
  );
}

/** The way from buying to being paid, in three steps. */
function Journey({ split }: { split: number | null }) {
  const steps = [
    { icon: ChartIcon, title: "Pass the evaluation", text: "Reach the profit target without breaking a loss limit, at your own pace." },
    { icon: TrophyIcon, title: "Get funded", text: "Trade a funded account with the same rules, and the profit is shared." },
    { icon: PayoutIcon, title: "Get paid", text: split !== null ? `Ask for a payout and keep ${split}% of the profit.` : "Ask for a payout of your share of the profit." },
  ];
  return (
    <ol aria-label="How it works" className="stagger grid grid-cols-1 gap-3 sm:grid-cols-3">
      {steps.map((step, index) => (
        <li key={step.title} className="relative flex items-start gap-3.5 rounded-xl border border-border bg-panel/60 p-4">
          <span className="grid size-10 shrink-0 place-items-center rounded-xl border border-border bg-raised text-accent shadow-card">
            <step.icon className="size-5" />
          </span>
          <span className="flex flex-col gap-0.5">
            <span className="text-xs text-muted">Step {index + 1}</span>
            <span className="font-medium">{step.title}</span>
            <span className="text-sm text-muted">{step.text}</span>
          </span>
        </li>
      ))}
    </ol>
  );
}

const ruleIcons: Record<ShopRule["key"], (props: { className?: string }) => React.ReactNode> = {
  target: ChartIcon,
  daily: ClockIcon,
  max: ShieldCheckIcon,
  split: PayoutIcon,
  days: CheckIcon,
  time: ClockIcon,
  activity: ClockIcon,
};

/**
 * One program, such as the two-step challenge: its sizes to choose from, the price of the one chosen, its phases and
 * main rules, and the button that buys it. The price and the figures change in place when another size is chosen.
 */
function ProgramCard({ table, initial, onBuy }: { table: ShopTable; initial: string | null; onBuy: (id: string) => void }) {
  const [chosenId, setChosenId] = useState(() => table.items.find((i) => i.challenge.id === initial)?.challenge.id ?? table.items[0].challenge.id);
  const item = table.items.find((i) => i.challenge.id === chosenId) ?? table.items[0];
  const phases = item.challenge.evaluation.length;
  const { card, button, barShown } = useBuyBar();
  return (
    <article
      ref={card}
      className="group relative flex flex-col gap-5 overflow-hidden rounded-2xl border border-border bg-panel p-5 shadow-card transition duration-300 ease-out-soft hover:-translate-y-1 hover:border-accent/40 hover:shadow-raised"
    >
      <span aria-hidden="true" className="pointer-events-none absolute -right-16 -top-16 size-48 rounded-full bg-accent/15 opacity-60 blur-3xl transition-opacity duration-300 group-hover:opacity-100" />
      <div className="relative flex flex-col gap-1">
        <h2 className="text-xl font-semibold tracking-tight">{table.title}</h2>
        <p className="text-sm text-muted">{phases === 0 ? "Funded from the start" : `${phases} ${phases === 1 ? "phase" : "phases"}, then funded`}</p>
      </div>
      {table.items.length > 1 && (
        <SegmentedControl
          label={`Account size of ${table.title}`}
          value={item.challenge.id}
          options={table.items.map((i) => ({ value: i.challenge.id, label: sizeLabel(i.challenge.initialBalance) }))}
          onChange={setChosenId}
        />
      )}
      <div className="relative flex flex-col gap-0.5">
        <span className="text-4xl font-semibold tracking-tight">
          <NumberFlow
            value={item.price}
            locales="en-US"
            format={{ style: "currency", currency: item.currency, minimumFractionDigits: Number.isInteger(item.price) ? 0 : 2 }}
            respectMotionPreference
          />
        </span>
        <span className="text-sm text-muted">
          Once, for a {wholeAmount(item.challenge.initialBalance)} {item.challenge.currency} account
        </span>
      </div>
      <PhaseJourney challenge={item.challenge} compact />
      <ul className="relative flex flex-col divide-y divide-border border-y border-border text-sm">
        {shopRules(item.challenge).map((rule) => {
          const Icon = ruleIcons[rule.key];
          return (
            <li key={rule.key} className="flex items-center gap-3 py-2.5">
              <Icon className="size-4 text-muted" />
              <span className="flex-1 text-muted">{rule.label}</span>
              <span className="font-medium">{rule.value}</span>
            </li>
          );
        })}
      </ul>
      <button ref={button} type="button" aria-label={`Buy ${item.challenge.name}`} onClick={() => onBuy(item.challenge.id)} className={`${buttonClass} relative mt-auto py-2.5`}>
        Start for {priceText(item.price, item.currency)}
      </button>
      {/* In the page's body, since a card that lifts on hover would otherwise carry a fixed bar with it. */}
      {barShown &&
        createPortal(
          <div className="fixed inset-x-0 bottom-0 z-20 flex animate-enter items-center gap-3 border-t border-border bg-panel/90 px-4 py-3 shadow-float backdrop-blur-md sm:hidden">
            <span className="flex min-w-0 flex-1 flex-col">
              <span className="truncate text-sm font-medium">{item.challenge.name}</span>
              <span className="text-xs text-muted">Once, {priceText(item.price, item.currency)}</span>
            </span>
            <button type="button" onClick={() => onBuy(item.challenge.id)} className={`${buttonClass} px-5 py-2.5`}>
              Start<span className="sr-only"> {item.challenge.name}</span>
            </button>
          </div>,
          document.body,
        )}
    </article>
  );
}

/**
 * On a phone, a bar fixed to the bottom of the screen with the card's buy button, while the card is in the middle of
 * the screen and its own button is out of sight. Only one card can cross the middle, so there is never more than one bar.
 */
function useBuyBar() {
  const card = useRef<HTMLElement>(null);
  const button = useRef<HTMLButtonElement>(null);
  const [centered, setCentered] = useState(false);
  const [buttonSeen, setButtonSeen] = useState(true);
  useEffect(() => {
    if (!card.current || !button.current) {
      return;
    }

    const middle = new IntersectionObserver(([entry]) => setCentered(entry.isIntersecting), { rootMargin: "-50% 0px -50% 0px" });
    const seen = new IntersectionObserver(([entry]) => setButtonSeen(entry.isIntersecting));
    middle.observe(card.current);
    seen.observe(button.current);
    return () => {
      middle.disconnect();
      seen.disconnect();
    };
  }, []);
  return { card, button, barShown: centered && !buttonSeen };
}

/** The questions buyers ask, answered from the firm's own rules. */
function Questions({ items, firmName }: { items: ShopItem[]; firmName: string }) {
  return (
    <section aria-labelledby="questions" className="flex flex-col gap-4">
      <h2 id="questions" className="font-serif text-3xl tracking-tight">
        Questions
      </h2>
      <div className="flex flex-col divide-y divide-border rounded-xl border border-border bg-panel shadow-card">
        {shopQuestions(items, firmName).map((q) => (
          <details key={q.question} className="group px-5">
            <summary className="flex cursor-pointer list-none items-center justify-between gap-3 py-4 font-medium">
              {q.question}
              <PlusIcon className="size-4 text-muted transition-transform duration-200 group-open:rotate-45" />
            </summary>
            <p className="animate-fade pb-4 text-sm text-muted">{q.answer}</p>
          </details>
        ))}
      </div>
    </section>
  );
}

/** One table of challenges with the same rules. On a phone it scrolls sideways, with the rules' names kept in view. */
function PriceTable({ table, chosen, onChoose }: { table: ShopTable; chosen: string | null; onChoose: (id: string) => void }) {
  return (
    <section aria-labelledby={`table-${table.key}`} className="flex flex-col gap-3">
      <h3 id={`table-${table.key}`} className="font-semibold">
        {table.title}
      </h3>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr>
              <th scope="col" className="sticky left-0 min-w-28 bg-panel py-2 pr-4 text-left font-normal text-muted">
                Account size
              </th>
              {table.items.map((item) => (
                <th key={item.challenge.id} scope="col" className="px-3 py-2 text-right text-base font-semibold">
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
                  <td key={table.items[i].challenge.id} className={`min-w-28 px-3 py-2 text-right ${index === 0 ? "font-semibold" : ""}`}>
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
                    aria-label={`From the table: buy ${item.challenge.name}`}
                    aria-pressed={chosen === item.challenge.id}
                    onClick={() => onChoose(item.challenge.id)}
                    className={`${secondaryButtonClass} text-sm`}
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

  // The panel opens on the first field the buyer has to fill in.
  useEffect(() => {
    form.current?.querySelector<HTMLElement>("input[required], select[required]")?.focus();
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
    <div className="flex flex-col gap-5">
      <dl className="flex flex-col gap-2 rounded-xl border border-border bg-background/40 p-4 text-sm">
        <div className="flex justify-between gap-3">
          <dt className="text-muted">{item.challenge.name}</dt>
          <dd className={discounted ? "text-muted line-through" : ""}>
            {formatMoney(item.price)} {item.currency}
          </dd>
        </div>
        {discounted && (
          <div className="flex justify-between gap-3 text-profit">
            <dt>Code {discounted.code}</dt>
            <dd>
              -{formatMoney(discounted.discount)} {discounted.currency}
            </dd>
          </div>
        )}
        <div className="flex justify-between gap-3 border-t border-border pt-2 text-base font-semibold">
          <dt>To pay</dt>
          <dd>
            {formatMoney(price)} {item.currency}
          </dd>
        </div>
      </dl>
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
          <button type="submit" disabled={order.isPending || order.isSuccess} className={`${buttonClass} flex-1 py-2.5`}>
            {order.isPending || order.isSuccess ? "Going to payment..." : `Pay ${formatMoney(price)} ${item.currency}`}
          </button>
          <button type="button" onClick={onCancel} className="text-muted hover:text-foreground">
            Choose another
          </button>
        </div>
        <p className="flex items-center gap-2 text-xs text-muted">
          <LockIcon className="size-3.5" />
          You pay on the payment provider&apos;s own page. Your account opens as soon as the payment is through.
        </p>
      </form>
    </div>
  );
}

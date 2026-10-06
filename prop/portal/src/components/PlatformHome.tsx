"use client";

import Link from "next/link";

import { usePlatform } from "@/app/providers";
import { monthlyPrices } from "@/lib/billing";
import { formatMoney } from "@/lib/format";
import { reviewTime } from "@/lib/review";

import { buttonClass, secondaryButtonClass } from "./ui";

const included: { title: string; text: string }[] = [
  { title: "Your own portal", text: "Traders buy challenges, follow their accounts and ask for payouts, with your name, logo and colors." },
  { title: "Kronant Trader", text: "Our trading terminal in the browser, with forex and metals on the trading conditions you choose." },
  { title: "Challenges that run themselves", text: "Profit targets, loss limits, trading days and time limits are checked as traders trade." },
  { title: "Payments and payouts", text: "Sell with Stripe or your own checkout. Traders ask for payouts, and you approve and pay them." },
  { title: "Emails to you and your traders", text: "When a challenge is bought, passed or ended, and when a payout moves." },
  { title: "An admin panel and an API", text: "Accounts, payouts, sales and your team in one place, and webhooks for your own systems." },
];

const steps: { title: string; text: string }[] = [
  { title: "Try it in a sandbox", text: "Sign up in a minute, set up your challenges and shop, and trade with test accounts. Free, without a card." },
  { title: "Apply to go live", text: `Tell us about the company and its owners. We check it, usually ${reviewTime}.` },
  { title: "Go live", text: "Pay the startup fee and your first month, and sell to real traders." },
];

/** The platform's front page: what a firm gets, what it costs and how to start. */
export function PlatformHome() {
  const platform = usePlatform();
  const prices = platform.prices;

  return (
    <div className="flex flex-1 flex-col">
      <header className="flex items-center justify-between border-b border-border px-6 py-4">
        <span className="text-lg font-semibold">{platform.name}</span>
        <Link href="/login" className="text-sm text-muted hover:text-foreground">
          Log in
        </Link>
      </header>

      <main className="mx-auto flex w-full max-w-4xl flex-col gap-14 px-6 py-12">
        <section className="flex flex-col gap-5">
          <h1 className="text-3xl font-semibold sm:text-4xl">Start your own prop firm</h1>
          <p className="max-w-2xl text-muted">
            Everything a prop firm needs: a portal for your traders in your brand, the Kronant Trader terminal, challenges that check themselves,
            payments and payouts. Try it all for free before you pay anything.
          </p>
          <div className="flex flex-wrap gap-3">
            <Link href="/signup" className={buttonClass}>
              Start free sandbox
            </Link>
            <Link href="/login" className={secondaryButtonClass}>
              Log in to your firm
            </Link>
          </div>
        </section>

        <section className="flex flex-col gap-4" aria-labelledby="included">
          <h2 id="included" className="text-xl font-semibold">
            What you get
          </h2>
          <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {included.map((item) => (
              <li key={item.title} className="flex flex-col gap-1 rounded-lg border border-border bg-panel p-4">
                <span className="font-medium">{item.title}</span>
                <span className="text-sm text-muted">{item.text}</span>
              </li>
            ))}
          </ul>
        </section>

        <section className="flex flex-col gap-4" aria-labelledby="steps">
          <h2 id="steps" className="text-xl font-semibold">
            How it works
          </h2>
          <ol className="grid gap-3 sm:grid-cols-3">
            {steps.map((step, i) => (
              <li key={step.title} className="flex flex-col gap-1 rounded-lg border border-border bg-panel p-4">
                <span className="text-xs text-muted">Step {i + 1}</span>
                <span className="font-medium">{step.title}</span>
                <span className="text-sm text-muted">{step.text}</span>
              </li>
            ))}
          </ol>
        </section>

        <section className="flex flex-col gap-4" aria-labelledby="prices">
          <h2 id="prices" className="text-xl font-semibold">
            What it costs
          </h2>
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="flex flex-col gap-2 rounded-lg border border-border bg-panel p-4">
              <span className="font-medium">Sandbox</span>
              <span className="text-2xl font-semibold">Free</span>
              <span className="text-sm text-muted">
                Up to {platform.sandboxMaxOpenAccounts} test accounts at a time, test payments, and every feature. As long as you like.
              </span>
            </div>
            <div className="flex flex-col gap-2 rounded-lg border border-border bg-panel p-4">
              <span className="font-medium">Live</span>
              <span className="text-2xl font-semibold">
                {formatMoney(prices.packagePrice)} {prices.currency}
                <span className="text-sm font-normal text-muted"> per month</span>
              </span>
              <ul className="flex flex-col gap-1 text-sm text-muted">
                <li>
                  Startup fee: {formatMoney(prices.startupFee)} {prices.currency} once
                  {prices.reviewDeposit > 0 && (
                    <>
                      , of which {formatMoney(prices.reviewDeposit)} {prices.currency} is paid when you apply to go live
                    </>
                  )}
                  .
                </li>
                {monthlyPrices(prices).map((line) => (
                  <li key={line}>{line}.</li>
                ))}
                <li>A slot is one challenge that is open. Prices are without VAT.</li>
              </ul>
            </div>
          </div>
        </section>

        <section className="flex flex-col items-start gap-3 rounded-lg border border-border bg-panel p-6">
          <h2 className="text-xl font-semibold">Ready to try it?</h2>
          <p className="text-sm text-muted">Your sandbox is ready in a minute. Nothing is charged until you choose to go live.</p>
          <Link href="/signup" className={buttonClass}>
            Start free sandbox
          </Link>
        </section>
      </main>
    </div>
  );
}

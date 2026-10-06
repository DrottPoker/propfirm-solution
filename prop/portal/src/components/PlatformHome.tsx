"use client";

import NumberFlow from "@number-flow/react";
import Link from "next/link";
import { useEffect, useState } from "react";

import { usePlatform } from "@/app/providers";
import type { StageRules } from "@/lib/api/types";
import { monthlyPriceFor } from "@/lib/billing";
import { priceText } from "@/lib/format";
import { reviewTime } from "@/lib/review";

import { BagIcon, ChartIcon, CheckIcon, GlobeIcon, OverviewIcon, PlusIcon, ShieldCheckIcon, TeamIcon } from "./icons";
import { KronantWordmark } from "./KronantMark";
import { AnimatedMoney, LiveDot, RoomBar } from "./Live";
import { PhaseJourney } from "./PhaseJourney";
import { buttonClass, secondaryButtonClass, Sparkline } from "./ui";

// The platform's front page: what Kronant Prop is, shown with the product itself, what it costs with a calculator,
// the questions firms ask, and who we are. The product pictures are drawn with the portal's own parts, so they look
// like what a firm gets, and their figures move a little, as they do for real.

const steps: { title: string; text: string }[] = [
  { title: "Try it in a sandbox", text: "Sign up in a minute, set up your challenges and shop, and trade with test accounts. Free, without a card." },
  { title: "Apply to go live", text: `Tell us about the company and its owners. We check it, usually ${reviewTime}.` },
  { title: "Go live", text: "Pay the startup fee and your first month, and sell to real traders." },
];

/** The platform's front page: what a firm gets, what it costs and how to start. */
export function PlatformHome() {
  return (
    <div className="flex flex-1 flex-col">
      <SiteHeader />
      <main className="flex flex-col">
        <Hero />
        <Parts />
        <Steps />
        <Pricing />
        <Trust />
        <Questions />
        <Closing />
      </main>
      <SiteFooter />
    </div>
  );
}

function SiteHeader() {
  return (
    <header className="sticky top-0 z-30 border-b border-border bg-background/80 backdrop-blur-md">
      <div className="mx-auto flex w-full max-w-6xl items-center gap-6 px-4 py-3 sm:px-6">
        <Link href="/" aria-label="Kronant Prop, home">
          <KronantWordmark product="Prop" />
        </Link>
        <nav aria-label="Sections" className="hidden gap-1 text-sm md:flex">
          {[
            ["#product", "Product"],
            ["#pricing", "Pricing"],
            ["#questions", "Questions"],
          ].map(([href, label]) => (
            <a key={href} href={href} className="rounded-lg px-3 py-2 text-muted transition-colors hover:text-foreground">
              {label}
            </a>
          ))}
        </nav>
        <div className="ml-auto flex items-center gap-2">
          <Link href="/login" className="rounded-lg px-3 py-2 text-sm text-muted transition-colors hover:text-foreground">
            Log in
          </Link>
          <Link href="/signup" className={`${buttonClass} hidden text-sm sm:inline-flex`}>
            Start free sandbox
          </Link>
        </div>
      </div>
    </header>
  );
}

function Hero() {
  return (
    <section className="relative isolate overflow-hidden">
      <div aria-hidden="true" className="absolute inset-x-0 -top-40 -z-10 h-[38rem] bg-[radial-gradient(50%_60%_at_70%_30%,color-mix(in_oklab,var(--accent)_16%,transparent),transparent)]" />
      <div className="mx-auto grid w-full max-w-6xl items-center gap-12 px-4 py-14 sm:px-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.05fr)] lg:py-24">
        <div className="stagger flex flex-col gap-6">
          <span className="self-start rounded-full border border-accent/30 bg-accent/10 px-3 py-1 text-xs font-medium text-accent">
            For new and growing prop firms
          </span>
          <h1 className="font-serif text-5xl leading-[1.02] tracking-tight sm:text-6xl">
            Start your own <em className="text-accent">prop firm</em>
          </h1>
          <p className="max-w-xl text-lg text-muted">
            Your own portal in your brand, the Kronant Trader terminal, challenges that check themselves, payments and payouts. Try all of it for free
            before you pay anything.
          </p>
          <div className="grid gap-3 sm:flex">
            <Link href="/signup" className={`${buttonClass} px-6 py-3`}>
              Start free sandbox
            </Link>
            <Link href="/login" className={`${secondaryButtonClass} px-6 py-3`}>
              Log in to your firm
            </Link>
          </div>
          <ul className="flex flex-wrap gap-x-5 gap-y-2 text-sm text-muted">
            {["Free sandbox, no card", "Your brand, your domain", "Live after our review"].map((point) => (
              <li key={point} className="flex items-center gap-1.5">
                <CheckIcon className="size-4 text-accent" />
                {point}
              </li>
            ))}
          </ul>
        </div>
        <ProductShot />
      </div>
    </section>
  );
}

// How far equity has moved from where the picture starts, a step each few seconds, so the figures tick as they do live.
const walk = [0, 42.1, 18.7, 96.3, 74.05, 131.5, 102.2, 160.8, 139.45, 188, 151.3, 117.6];

/** The step of the walk now. It stands still for those who asked their system for less motion. */
function useTicker(every: number): number {
  const [step, setStep] = useState(0);
  useEffect(() => {
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      return;
    }

    const timer = window.setInterval(() => setStep((s) => (s + 1) % walk.length), every);
    return () => window.clearInterval(timer);
  }, [every]);
  return step;
}

/** The portal's account card in front of the terminal, tilted, with figures that tick. */
function ProductShot() {
  const step = useTicker(2200);
  const equity = 101_245.6 + walk[step];
  const daily = 4_245.6 + walk[step];
  return (
    <div aria-hidden="true" className="relative mx-auto w-full max-w-xl [perspective:1800px]">
      <div className="relative pt-36 transition-transform duration-700 ease-out-soft [transform:rotateY(-11deg)_rotateX(6deg)] [transform-style:preserve-3d] hover:[transform:rotateY(-4deg)_rotateX(2deg)] sm:pt-44">
        <div className="absolute top-0 right-0 w-[92%] overflow-hidden rounded-xl border border-border bg-panel shadow-float [transform:translateZ(-60px)]">
          <TerminalPicture />
        </div>
        <div className="relative w-[74%] min-w-72 rounded-2xl border border-border bg-panel p-5 shadow-float [transform:translateZ(40px)]">
          <div className="flex items-start justify-between gap-3">
            <div className="flex flex-col gap-0.5">
              <span className="font-serif text-2xl leading-tight">Two-step 100K</span>
              <span className="text-xs text-muted">#1043 · 100,000 USD</span>
            </div>
            <span className="rounded-full bg-accent/15 px-2.5 py-0.5 text-xs font-medium text-accent ring-1 ring-accent/25 ring-inset">Phase 2</span>
          </div>
          <div className="mt-5 flex items-end justify-between gap-3">
            <div className="flex flex-col gap-1">
              <LiveDot />
              <span className="text-4xl font-medium tracking-tight">
                <AnimatedMoney value={equity} />
              </span>
            </div>
            <span className="text-sm text-profit">
              <AnimatedMoney value={equity - 100_000} signed />
            </span>
          </div>
          <div className="mt-5 flex flex-col gap-3 text-xs">
            <div className="flex flex-col gap-1.5">
              <span className="flex justify-between">
                <span className="text-muted">Daily loss limit</span>
                <span>
                  <AnimatedMoney value={daily} /> left
                </span>
              </span>
              <RoomBar share={(100 * daily) / 5_000} state="ok" label="Daily loss limit" />
            </div>
            <div className="flex flex-col gap-1.5">
              <span className="flex justify-between">
                <span className="text-muted">Profit target</span>
                <span>62%</span>
              </span>
              <span className="h-1.5 overflow-hidden rounded-full bg-border">
                <span className="block h-full w-[62%] rounded-full bg-accent" />
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

// Candles for the pictures of the terminal, the same each time.
const candles = (() => {
  let seed = 11;
  const random = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
  let price = 100;
  return Array.from({ length: 36 }, () => {
    const open = price;
    const close = open + (random() - 0.44) * 3;
    price = close;
    return { open, close, high: Math.max(open, close) + random() * 1.3, low: Math.min(open, close) - random() * 1.3 };
  });
})();

function TerminalPicture({ tall = false }: { tall?: boolean }) {
  const width = 360;
  const height = tall ? 190 : 150;
  const high = Math.max(...candles.map((c) => c.high));
  const low = Math.min(...candles.map((c) => c.low));
  const y = (price: number) => 8 + ((high - price) / (high - low)) * (height - 16);
  const step = width / candles.length;
  return (
    <div className="flex flex-col">
      <div className="flex items-center gap-3 border-b border-border px-4 py-2.5 text-xs">
        <span className="font-semibold">EURUSD</span>
        <span className="font-mono text-profit">1.08724</span>
        <span className="ml-auto flex gap-1 text-muted">
          {["M5", "M15", "H1"].map((frame) => (
            <span key={frame} className={`rounded px-1.5 py-0.5 ${frame === "M15" ? "bg-raised text-foreground" : ""}`}>
              {frame}
            </span>
          ))}
        </span>
      </div>
      <svg viewBox={`0 0 ${width} ${height}`} className="w-full" preserveAspectRatio="none">
        {candles.map((c, i) => {
          const up = c.close >= c.open;
          const x = i * step + step / 2;
          return (
            <g key={i} stroke={up ? "var(--profit)" : "var(--loss)"} fill={up ? "var(--profit)" : "var(--loss)"}>
              <line x1={x} x2={x} y1={y(c.high)} y2={y(c.low)} strokeWidth="1" />
              <rect x={x - step * 0.32} width={step * 0.64} y={y(Math.max(c.open, c.close))} height={Math.max(1, Math.abs(y(c.open) - y(c.close)))} />
            </g>
          );
        })}
        <line x1="0" x2={width} y1={y(candles.at(-1)!.close)} y2={y(candles.at(-1)!.close)} stroke="var(--accent)" strokeDasharray="3 3" strokeWidth="1" />
      </svg>
      <div className="grid grid-cols-2 gap-2 border-t border-border p-3 text-center text-xs font-semibold">
        <span className="rounded-lg bg-loss/90 py-2 text-background">SELL 1.08720</span>
        <span className="rounded-lg bg-profit/90 py-2 text-background">BUY 1.08724</span>
      </div>
    </div>
  );
}

/** The three parts of the product, each with a picture of it. */
function Parts() {
  const parts = [
    {
      icon: <BagIcon className="size-5" />,
      title: "Your portal, in your brand",
      text: "Traders buy challenges in your shop, follow their accounts with live figures, and ask for payouts. Your name, logo, colors and domain on every page.",
      points: ["A shop that compares sizes and rules", "Accounts with loss limits that change color as they close in", "Payouts, certificates and support"],
      picture: <ShopPicture />,
    },
    {
      icon: <ChartIcon className="size-5" />,
      title: "Kronant Trader, in the browser",
      text: "A fast terminal for forex and metals on the trading conditions you choose. Nothing to install, on a computer or a phone.",
      points: ["Charts, one-click orders, stop loss and take profit", "The room to each limit, always in view", "Your leverage, spreads and commissions"],
      picture: <TerminalPicture tall />,
    },
    {
      icon: <OverviewIcon className="size-5" />,
      title: "An admin panel that keeps up",
      text: "What needs you first, sales and payouts as they happen, and every trader's account in detail. Webhooks and an API for your own systems.",
      points: ["Payouts to approve and tickets to answer", "Challenges with your own rules and prices", "Discount codes, KYC and your team"],
      picture: <AdminPicture />,
    },
  ];
  return (
    <section id="product" aria-labelledby="product-heading" className="scroll-mt-20 border-t border-border bg-panel/40">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-16 px-4 py-20 sm:px-6">
        <div className="flex max-w-2xl flex-col gap-3">
          <h2 id="product-heading" className="font-serif text-4xl tracking-tight">
            Everything a prop firm runs on
          </h2>
          <p className="text-muted">Three parts that work as one, from the first sale to the payout.</p>
        </div>
        {parts.map((part, index) => (
          <article key={part.title} className="grid items-center gap-10 lg:grid-cols-2">
            <div className={`flex flex-col gap-4 ${index % 2 === 1 ? "lg:order-2" : ""}`}>
              <span aria-hidden="true" className="grid size-10 place-items-center rounded-xl bg-accent/12 text-accent ring-1 ring-accent/25 ring-inset">
                {part.icon}
              </span>
              <h3 className="font-serif text-3xl tracking-tight">{part.title}</h3>
              <p className="text-muted">{part.text}</p>
              <ul className="flex flex-col gap-2 text-sm">
                {part.points.map((point) => (
                  <li key={point} className="flex items-start gap-2">
                    <CheckIcon className="mt-0.5 size-4 text-accent" />
                    {point}
                  </li>
                ))}
              </ul>
            </div>
            <div aria-hidden="true" className="overflow-hidden rounded-2xl border border-border bg-panel shadow-raised transition-transform duration-500 ease-out-soft hover:-translate-y-1">
              {part.picture}
            </div>
          </article>
        ))}
      </div>
    </section>
  );
}

function pictureStage(name: string, profitTargetPercent: number | null, profitSplitPercent: number | null = null): StageRules {
  return {
    name,
    profitTargetPercent,
    minTradingDays: 0,
    dailyLoss: { percent: 5, reference: "Balance" },
    maxLoss: { percent: 10, kind: "Fixed" },
    profitSplitPercent,
    maxDays: null,
    consistencyPercent: null,
  };
}

const pictureChallenge = { evaluation: [pictureStage("Phase 1", 8), pictureStage("Phase 2", 5)], funded: pictureStage("Funded", null, 80) };

function ShopPicture() {
  return (
    <div className="flex flex-col gap-5 p-6">
      <div className="flex items-center justify-between gap-3">
        <span className="flex items-center gap-2 text-sm font-semibold">
          <span className="size-5 rounded-md bg-accent" />
          Aurora Funded
        </span>
        <span className="flex rounded-lg border border-border p-0.5 text-xs">
          {["25K", "50K", "100K"].map((size) => (
            <span key={size} className={`rounded-md px-2.5 py-1 ${size === "100K" ? "bg-raised font-medium" : "text-muted"}`}>
              {size}
            </span>
          ))}
        </span>
      </div>
      <div className="flex items-end justify-between gap-3">
        <div className="flex flex-col">
          <span className="font-serif text-2xl">Two-step 100K</span>
          <span className="text-xs text-muted">Once, for a 100,000 USD account</span>
        </div>
        <span className="text-3xl font-semibold">$349</span>
      </div>
      <PhaseJourney challenge={pictureChallenge} compact />
      <span className={`${buttonClass} justify-center`}>Start for $349</span>
    </div>
  );
}

function AdminPicture() {
  const rows = [
    { tone: "bg-warning", text: "Payout of $1,240 to approve", detail: "#1043 · Sara Lind" },
    { tone: "bg-profit", text: "#1051 passed Phase 2", detail: "Funded account to approve" },
    { tone: "bg-accent", text: "New ticket", detail: "About account #1038" },
  ];
  return (
    <div className="flex flex-col gap-5 p-6">
      <div className="grid grid-cols-2 gap-3">
        <div className="flex flex-col gap-1 rounded-xl border border-border bg-background/40 p-4">
          <span className="text-xs text-muted">Sales, 30 days</span>
          <span className="text-2xl font-semibold">$18,420</span>
          <Sparkline values={[4, 6, 5, 8, 7, 11, 10, 14]} className="h-8 w-full" />
        </div>
        <div className="flex flex-col gap-1 rounded-xl border border-border bg-background/40 p-4">
          <span className="text-xs text-muted">Pass rate, 90 days</span>
          <span className="text-2xl font-semibold">13%</span>
          <Sparkline values={[9, 12, 10, 13, 11, 14, 12, 13]} tone="profit" className="h-8 w-full" />
        </div>
      </div>
      <div className="flex flex-col gap-2">
        <span className="text-sm font-medium">
          Needs you <span className="text-accent">3</span>
        </span>
        {rows.map((row) => (
          <span key={row.text} className="flex items-center gap-3 rounded-lg border border-border bg-background/40 px-3 py-2.5 text-sm">
            <span className={`size-2 rounded-full ${row.tone}`} />
            <span className="flex-1">{row.text}</span>
            <span className="text-xs text-muted">{row.detail}</span>
          </span>
        ))}
      </div>
    </div>
  );
}

function Steps() {
  return (
    <section aria-labelledby="steps-heading" className="mx-auto flex w-full max-w-6xl flex-col gap-10 px-4 py-20 sm:px-6">
      <h2 id="steps-heading" className="font-serif text-4xl tracking-tight">
        How it works
      </h2>
      <ol className="grid gap-8 md:grid-cols-3">
        {steps.map((step, i) => (
          <li key={step.title} className="relative flex flex-col gap-2 border-t border-border pt-6">
            <span aria-hidden="true" className="absolute -top-px left-0 h-px w-16 bg-accent" />
            <span className="font-serif text-5xl leading-none text-accent/70">{i + 1}</span>
            <span className="text-lg font-medium">{step.title}</span>
            <span className="text-sm text-muted">{step.text}</span>
          </li>
        ))}
      </ol>
    </section>
  );
}

/** Our prices in whole amounts, a calculator for a month at a number of open challenges, and what others charge. */
function Pricing() {
  const platform = usePlatform();
  const prices = platform.prices;
  const money = (value: number) => priceText(value, prices.currency);
  const [slots, setSlots] = useState(40);
  const monthly = monthlyPriceFor(prices, slots);
  const extra = Math.max(0, slots - prices.packageSlots);

  return (
    <section id="pricing" aria-labelledby="pricing-heading" className="scroll-mt-20 border-t border-border bg-panel/40">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-10 px-4 py-20 sm:px-6">
        <div className="flex max-w-2xl flex-col gap-3">
          <h2 id="pricing-heading" className="font-serif text-4xl tracking-tight">
            Simple pricing
          </h2>
          <p className="text-muted">You pay for the challenges that are open at a time, not for your sales. Prices are without VAT.</p>
        </div>

        <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.2fr)]">
          <div className="flex flex-col gap-5 rounded-2xl border border-border bg-panel p-6 shadow-card">
            <span className="text-sm font-medium text-muted">Sandbox</span>
            <span className="font-serif text-5xl">Free</span>
            <p className="text-sm text-muted">
              Every feature, test payments and up to {platform.sandboxMaxOpenAccounts} test accounts at a time. As long as you like.
            </p>
            <span className="mt-auto text-sm font-medium text-muted">Live</span>
            <p className="flex flex-wrap items-baseline gap-x-2">
              <span className="font-serif text-5xl">{money(prices.packagePrice)}</span>
              <span className="text-muted">a month</span>
              <span className="text-2xl text-muted">+</span>
              <span className="font-serif text-3xl">{money(prices.startupFee)}</span>
              <span className="text-muted">once</span>
            </p>
            <ul className="flex flex-col gap-1.5 text-sm text-muted">
              <li>
                Startup fee: {money(prices.startupFee)} once
                {prices.reviewDeposit > 0 && `, of which ${money(prices.reviewDeposit)} is paid when you apply to go live`}.
              </li>
              <li>
                The month covers {prices.packageSlots} open challenges.
                {prices.slotPrices.map((tier, i) => {
                  const next = prices.slotPrices[i + 1];
                  return ` ${i === 0 ? "Then" : "From"} ${tier.from}${next ? ` to ${next.from - 1}` : " and up"}, ${money(tier.price)} each.`;
                })}
              </li>
            </ul>
          </div>

          <div className="flex flex-col gap-6 rounded-2xl border border-accent/30 bg-panel p-6 shadow-raised">
            <label className="flex flex-col gap-3">
              <span className="flex items-baseline justify-between gap-3">
                <span className="font-medium">Open challenges at a time</span>
                <span className="text-2xl font-semibold">{slots}</span>
              </span>
              <input type="range" min={5} max={300} step={5} value={slots} onChange={(e) => setSlots(Number(e.target.value))} className="w-full accent-accent" />
            </label>
            <div className="flex flex-col gap-1">
              <span className="text-sm text-muted">A month with {slots} open challenges</span>
              <span className="font-serif text-6xl leading-none">
                <NumberFlow value={monthly} locales="en-US" format={{ style: "currency", currency: prices.currency, maximumFractionDigits: 0 }} respectMotionPreference />
              </span>
              <span className="text-sm text-muted">
                {extra === 0 ? `All within the package of ${prices.packageSlots}.` : `The package, and ${extra} more at their tier's price.`} About{" "}
                {priceText(Math.round((monthly / slots) * 100) / 100, prices.currency)} per open challenge.
              </span>
            </div>
            <div className="flex flex-col gap-2 border-t border-border pt-5 text-sm">
              <span className="font-medium">What others charge</span>
              <dl className="grid gap-2">
                {[
                  ["White-label platforms", "Often $2,000 to $4,000 a month, after a sales call"],
                  ["Turnkey packages", "Often $2,000 to $3,000 to start, then 50 to 70% of your sales"],
                ].map(([label, value]) => (
                  <div key={label} className="flex flex-wrap justify-between gap-x-4 gap-y-0.5">
                    <dt className="text-muted">{label}</dt>
                    <dd>{value}</dd>
                  </div>
                ))}
              </dl>
              <span className="text-xs text-muted">From providers&apos; public price lists, October 2026, without VAT.</span>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}

function Trust() {
  const items = [
    { icon: <GlobeIcon className="size-5" />, title: "Data in the EU", text: "Firms' and traders' data is stored in the EU. We are a Swedish company." },
    { icon: <ShieldCheckIcon className="size-5" />, title: "Every firm reviewed", text: "We check each firm's company and owners before it sells to real traders." },
    { icon: <TeamIcon className="size-5" />, title: "Each firm on its own", text: "A firm's traders, accounts and settings are kept apart from every other firm's." },
  ];
  return (
    <section aria-label="Trust" className="mx-auto grid w-full max-w-6xl gap-8 px-4 py-20 sm:px-6 md:grid-cols-3">
      {items.map((item) => (
        <div key={item.title} className="flex gap-4">
          <span aria-hidden="true" className="grid size-10 shrink-0 place-items-center rounded-xl bg-accent/12 text-accent ring-1 ring-accent/25 ring-inset">
            {item.icon}
          </span>
          <div className="flex flex-col gap-1">
            <span className="font-medium">{item.title}</span>
            <span className="text-sm text-muted">{item.text}</span>
          </div>
        </div>
      ))}
    </section>
  );
}

function Questions() {
  const platform = usePlatform();
  const questions = [
    {
      question: "What is an open challenge?",
      answer:
        "A challenge that is bought and has not ended yet, in any phase or funded. When it ends, it frees its place. An order waiting for payment holds one too, until it is paid or given up.",
    },
    {
      question: "Do I need a trading platform of my own?",
      answer: "No. Kronant Trader, our terminal, is included, with prices for forex and metals. You set the leverage, spreads and commissions.",
    },
    {
      question: "How do my traders pay, and who gets the money?",
      answer: "With Stripe in your own Stripe account, or on your own checkout page. The money goes to you, and we never hold it.",
    },
    {
      question: "Can I use my own domain?",
      answer: "Yes. Your portal gets an address with us from the start, and you can point a domain of your own at it whenever you like.",
    },
    {
      question: "What does the sandbox include?",
      answer: `Every feature, with test payments and up to ${platform.sandboxMaxOpenAccounts} test accounts at a time. It is free for as long as you like, and nothing is charged until you choose to go live.`,
    },
    {
      question: "How long does it take to go live?",
      answer: `Apply from your admin panel when you are ready. We check the company and its owners, usually ${reviewTime}, and then you pay and go live.`,
    },
  ];
  return (
    <section id="questions" aria-labelledby="questions-heading" className="scroll-mt-20 border-t border-border bg-panel/40">
      <div className="mx-auto flex w-full max-w-3xl flex-col gap-8 px-4 py-20 sm:px-6">
        <h2 id="questions-heading" className="font-serif text-4xl tracking-tight">
          Questions
        </h2>
        <div className="flex flex-col divide-y divide-border rounded-2xl border border-border bg-panel shadow-card">
          {questions.map((q) => (
            <details key={q.question} className="group px-5">
              <summary className="flex cursor-pointer list-none items-center justify-between gap-3 py-4 font-medium">
                {q.question}
                <PlusIcon className="size-4 text-muted transition-transform duration-200 group-open:rotate-45" />
              </summary>
              <p className="animate-fade pb-4 text-sm text-muted">{q.answer}</p>
            </details>
          ))}
        </div>
      </div>
    </section>
  );
}

function Closing() {
  return (
    <section className="mx-auto w-full max-w-6xl px-4 py-20 sm:px-6">
      <div className="relative isolate flex flex-col items-start gap-5 overflow-hidden rounded-3xl border border-accent/30 bg-panel p-8 shadow-raised sm:p-12">
        <div aria-hidden="true" className="absolute inset-0 -z-10 bg-[radial-gradient(60%_120%_at_100%_0%,color-mix(in_oklab,var(--accent)_20%,transparent),transparent)]" />
        <h2 className="font-serif text-4xl tracking-tight sm:text-5xl">Ready to try it?</h2>
        <p className="max-w-xl text-muted">Your sandbox is ready in a minute. Nothing is charged until you choose to go live.</p>
        <Link href="/signup" className={`${buttonClass} px-6 py-3`}>
          Start free sandbox
        </Link>
      </div>
    </section>
  );
}

function SiteFooter() {
  const platform = usePlatform();
  const links = [
    platform.termsUrl && { href: platform.termsUrl, label: "Terms of service" },
    platform.dpaUrl && { href: platform.dpaUrl, label: "Data processing agreement" },
  ].filter((link): link is { href: string; label: string } => Boolean(link));
  return (
    <footer className="border-t border-border">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-6 px-4 py-10 text-sm sm:flex-row sm:items-start sm:justify-between sm:px-6">
        <div className="flex flex-col gap-2">
          <KronantWordmark product="Prop" />
          <span className="text-muted">Kronant Prop and Kronant Trader are made by Ludware AB, Sweden.</span>
        </div>
        <nav aria-label="Footer" className="flex flex-wrap gap-x-6 gap-y-2 text-muted">
          {links.map((link) => (
            <a key={link.href} href={link.href} className="hover:text-foreground">
              {link.label}
            </a>
          ))}
          <Link href="/login" className="hover:text-foreground">
            Log in
          </Link>
          <Link href="/signup" className="hover:text-foreground">
            Start free sandbox
          </Link>
        </nav>
      </div>
    </footer>
  );
}

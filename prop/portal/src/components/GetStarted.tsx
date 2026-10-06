"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";

import type { FirmSettings } from "@/lib/api/types";
import { guideSteps, stepAfter, tryChecklist, type GuideStep } from "@/lib/getStarted";
import { logoHardToSee } from "@/lib/logoContrast";
import { useChallenges, useFirmSettings, useNewestAccounts, usePrices, useSaveColors, useSavePayments } from "@/lib/queries";
import { defaultColors } from "@/lib/theme";

import { PriceControl } from "./AdminChallenges";
import { CheckIcon, ExternalIcon } from "./icons";
import { BrandSwatches, Logo, useLogoEdges } from "./PortalDesign";
import { AdminPage, buttonClass, ErrorText, Loading, Message, Panel, secondaryButtonClass } from "./ui";

/** Where the guide is, for a step's own buttons at the bottom. */
type Nav = { step: GuideStep; index: number; go: (step: GuideStep) => void };

/**
 * Right after signing up: the look, the first challenge's price and how traders pay, one short step at a time, and then
 * trying it as a trader. Every step can be skipped, and the overview has the rest of the way to live. The step is in the
 * address, so the browser's back button and a reload keep it.
 */
export function GetStarted({ step }: { step: GuideStep }) {
  const router = useRouter();
  const settings = useFirmSettings();
  const go = (next: GuideStep) => router.push(`/admin/get-started?step=${next}`, { scroll: false });

  if (settings.isError) {
    return <Message text={settings.error.message} />;
  }

  if (!settings.data) {
    return <Loading />;
  }

  const index = guideSteps.findIndex((s) => s.key === step);
  const current = guideSteps[index];
  const nav = { step, index, go };
  return (
    <AdminPage narrow>
      <div className="flex flex-col gap-2">
        <span className="text-sm text-muted">
          Welcome to {settings.data.name}. Step {index + 1} of {guideSteps.length}
        </span>
        <h1 className="font-serif text-[2.35rem] leading-[1.05] tracking-tight">{current.title}</h1>
        <p className="text-muted">{current.text}</p>
      </div>
      <ol aria-label="Steps" className="grid grid-cols-4 gap-2">
        {guideSteps.map((s, i) => (
          <li key={s.key}>
            <button
              type="button"
              onClick={() => go(s.key)}
              aria-current={s.key === step ? "step" : undefined}
              aria-label={`Step ${i + 1}: ${s.title}`}
              className="group flex w-full flex-col gap-2 text-left"
            >
              <span className="h-1.5 w-full overflow-hidden rounded-full bg-border">
                <span className={`block h-full origin-left rounded-full bg-accent transition-transform duration-500 ease-out-soft ${i <= index ? "scale-x-100" : "scale-x-0"}`} />
              </span>
              <span className={`flex items-center gap-1.5 text-xs ${i === index ? "font-medium text-foreground" : "text-muted group-hover:text-foreground"}`}>
                {i < index && <CheckIcon className="size-3.5 text-accent" />}
                {s.name}
              </span>
            </button>
          </li>
        ))}
      </ol>

      {step === "look" && <LookStep settings={settings.data} nav={nav} />}
      {step === "price" && <PriceStep settings={settings.data} nav={nav} />}
      {step === "payments" && <PaymentsStep settings={settings.data} nav={nav} />}
      {step === "try" && <TryStep settings={settings.data} nav={nav} />}
    </AdminPage>
  );
}

/** Back, the step's main button, which is Next unless the step has its own, and a way out of the guide. */
function Footer({ nav, primary }: { nav: Nav; primary?: React.ReactNode }) {
  const last = nav.index === guideSteps.length - 1;
  return (
    <div className="flex flex-wrap items-center gap-3">
      {nav.index > 0 && (
        <button type="button" onClick={() => nav.go(stepAfter(nav.step, -1))} className={secondaryButtonClass}>
          Back
        </button>
      )}
      {primary ??
        (last ? (
          <Link href="/admin" className={secondaryButtonClass}>
            Go to your overview
          </Link>
        ) : (
          <button type="button" onClick={() => nav.go(stepAfter(nav.step, 1))} className={buttonClass}>
            Next
          </button>
        ))}
      {!last && (
        <Link href="/admin" className="ml-auto text-sm text-muted hover:text-foreground">
          Skip the guide
        </Link>
      )}
    </div>
  );
}

function LookStep({ settings, nav }: { settings: FirmSettings; nav: Nav }) {
  const router = useRouter();
  const save = useSaveColors();
  const edges = useLogoEdges(settings.logoUrl);
  const accent = settings.colors.accent ?? defaultColors.accent;

  // A new brand color gets the button text that is easy to read on it, as on the design page.
  const choose = (color: string) => {
    const colors: Record<string, string> = { ...settings.colors, accent: color };
    delete colors["accent-foreground"];
    save.mutate(colors, { onSuccess: () => router.refresh() });
  };

  return (
    <>
      <Logo settings={settings} colorsWait={false} hardToSee={edges !== null && logoHardToSee(edges, settings.colors.panel ?? defaultColors.panel)} />
      <Panel title="Brand color">
        <BrandSwatches value={accent} disabled={save.isPending} onChoose={choose} />
        <p className="text-sm text-muted">
          Saved as soon as you choose. More colors, themes and a preview are under{" "}
          <Link href="/admin/design" className="text-accent hover:underline">
            Portal design
          </Link>
          .
        </p>
        <ErrorText error={save.error} />
      </Panel>
      <Footer nav={nav} />
    </>
  );
}

const priceForm = "guide-price";

// Next saves the price when it was changed, so the firm does not have to save first.
function PriceStep({ settings, nav }: { settings: FirmSettings; nav: Nav }) {
  const challenges = useChallenges(settings.status !== "Provisioning");
  const prices = usePrices();
  const first = challenges.data?.[0];

  if (settings.status === "Provisioning") {
    return (
      <>
        <Message text="Your trading server is being set up, with your first challenge. This takes a few seconds." />
        <Footer nav={nav} />
      </>
    );
  }

  if (!challenges.data || !prices.data) {
    return <ErrorText error={challenges.error ?? prices.error} />;
  }

  return (
    <>
      <Panel title={first?.name ?? "Your challenge"}>
        {first ? (
          <>
            <p className="text-sm text-muted">
              {first.evaluation.length > 0
                ? `Profit targets of ${first.evaluation.map((s) => `${s.profitTargetPercent}%`).join(" and ")}, ${first.evaluation[0].dailyLoss.percent}% daily loss`
                : `Funded from the start, with ${first.funded.dailyLoss.percent}% daily loss`}{" "}
              and {first.funded.profitSplitPercent}% of the profit to the funded trader. Change the rules, or make more sizes, under{" "}
              <Link href="/admin/challenges" className="text-accent hover:underline">
                Challenges
              </Link>
              .
            </p>
            <PriceControl challenge={first} price={prices.data.find((p) => p.challengeId === first.id)} formId={priceForm} onDone={() => nav.go("payments")} />
          </>
        ) : (
          <p className="text-sm text-muted">
            You have no challenge yet.{" "}
            <Link href="/admin/challenges/new" className="text-accent hover:underline">
              Make one from a template
            </Link>
            .
          </p>
        )}
      </Panel>
      <Footer
        nav={nav}
        primary={
          first && (
            <button type="submit" form={priceForm} className={buttonClass}>
              Next
            </button>
          )
        }
      />
    </>
  );
}

function PaymentsStep({ settings, nav }: { settings: FirmSettings; nav: Nav }) {
  const save = useSavePayments();
  const payments = settings.payments;
  const offerTest = !payments.provider && payments.testPaymentsAllowed;

  const startTestPayments = () =>
    save.mutate(
      { provider: "Test", stripeSecretKey: "", stripeWebhookSecret: "", checkoutUrl: payments.checkoutUrl ?? "", termsUrl: payments.termsUrl ?? "" },
      { onSuccess: () => nav.go("try") },
    );

  return (
    <>
      <Panel title="How traders pay">
        {payments.provider ? (
          <p className="flex items-center gap-2 text-sm">
            <CheckIcon className="size-4 text-profit" />
            {payments.provider === "Test"
              ? "Test payments are on: buyers pay on a test page, and no money is taken."
              : payments.provider === "Stripe"
                ? "Buyers pay with Stripe."
                : "Buyers pay on your own checkout page."}
          </p>
        ) : (
          <p className="text-sm">With test payments, buyers pay on a test page and no money is taken.</p>
        )}
        <p className="text-sm text-muted">
          Stripe and your own checkout page are under{" "}
          <Link href="/admin/checkout" className="text-accent hover:underline">
            Checkout
          </Link>
          .
        </p>
        <ErrorText error={save.error} />
      </Panel>
      <Footer
        nav={nav}
        primary={
          offerTest && (
            <button type="button" disabled={save.isPending} onClick={startTestPayments} className={buttonClass}>
              {save.isPending ? "Saving..." : "Use test payments and go on"}
            </button>
          )
        }
      />
    </>
  );
}

// The checklist ticks itself off as the firm buys and trades in its own shop, so the page is asked for again often.
function TryStep({ settings, nav }: { settings: FirmSettings; nav: Nav }) {
  const accounts = useNewestAccounts(10, 5_000);
  const items = tryChecklist(accounts.data?.accounts ?? []);
  const done = items.every((i) => i.done);
  return (
    <>
      <Panel title="Your shop">
        <p className="text-sm text-muted">Use an email of your own, and choose a password from the email you get. Then you see everything your traders will.</p>
        <ol aria-label="Trying it as a trader" className="flex flex-col gap-2.5">
          {items.map((item) => (
            <li key={item.key} className={`flex items-center gap-3 text-sm ${item.done ? "" : "text-muted"}`}>
              <span
                aria-hidden="true"
                className={`grid size-6 shrink-0 place-items-center rounded-full transition-colors duration-500 ${item.done ? "bg-profit/15 text-profit" : "border border-border"}`}
              >
                {item.done && <CheckIcon className="size-3.5 animate-pop" />}
              </span>
              {item.label}
              <span className="sr-only">{item.done ? ", done" : ", not yet"}</span>
            </li>
          ))}
        </ol>
        <a href={new URL("buy", settings.portalUrl).toString()} target="_blank" rel="noreferrer" className={`${buttonClass} flex items-center gap-2 self-start`}>
          Open your shop
          <ExternalIcon className="size-4" />
        </a>
        <p className="text-sm text-muted">
          {done ? "Everything works. " : ""}The overview has the rest of the way to live: our review of your company, and choosing your slots.
        </p>
      </Panel>
      <Footer nav={nav} />
    </>
  );
}

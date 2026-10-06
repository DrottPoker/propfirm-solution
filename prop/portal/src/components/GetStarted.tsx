"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import type { FirmSettings } from "@/lib/api/types";
import { guideSteps, stepAfter, type GuideStep } from "@/lib/getStarted";
import { useChallenges, useFirmSettings, usePrices, useSaveColors, useSavePayments } from "@/lib/queries";
import { logoHardToSee } from "@/lib/logoContrast";
import { defaultColors } from "@/lib/theme";

import { PriceControl } from "./AdminChallenges";
import { CheckIcon, ExternalIcon } from "./icons";
import { brandSwatches, Logo, useLogoEdges } from "./PortalDesign";
import { AdminPage, buttonClass, ErrorText, Message, Panel, secondaryButtonClass } from "./ui";

/**
 * Right after signing up: the look, the first challenge's price and how traders pay, one short step at a time, and then
 * trying it as a trader. Every step can be skipped, and the overview has the rest of the way to live. The step is in the
 * address, so the browser's back button and a reload keep it.
 */
export function GetStarted({ step }: { step: GuideStep }) {
  const router = useRouter();
  const settings = useFirmSettings();
  const setStep = (next: GuideStep) => router.push(`/admin/get-started?step=${next}`, { scroll: false });

  if (settings.isError) {
    return <Message text={settings.error.message} />;
  }

  if (!settings.data) {
    return <Message text="Loading..." />;
  }

  const index = guideSteps.findIndex((s) => s.key === step);
  const current = guideSteps[index];
  return (
    <AdminPage narrow>
      <div className="flex flex-col gap-2">
        <span className="text-sm text-muted">
          Welcome to {settings.data.name}. Step {index + 1} of {guideSteps.length}
        </span>
        <h1 className="text-2xl font-semibold tracking-tight">{current.title}</h1>
        <p className="text-muted">{current.text}</p>
      </div>
      <ol aria-label="Steps" className="flex gap-2">
        {guideSteps.map((s, i) => (
          <li key={s.key} className="flex-1">
            <button
              type="button"
              onClick={() => setStep(s.key)}
              aria-current={s.key === step ? "step" : undefined}
              aria-label={`Step ${i + 1}: ${s.title}`}
              className={`h-1.5 w-full rounded-full ${i <= index ? "bg-accent" : "bg-border"}`}
            />
          </li>
        ))}
      </ol>

      {step === "look" && <LookStep settings={settings.data} />}
      {step === "price" && <PriceStep settings={settings.data} />}
      {step === "payments" && <PaymentsStep settings={settings.data} />}
      {step === "try" && <TryStep settings={settings.data} />}

      <div className="flex flex-wrap items-center gap-3">
        {index > 0 && (
          <button type="button" onClick={() => setStep(stepAfter(step, -1))} className={secondaryButtonClass}>
            Back
          </button>
        )}
        {index < guideSteps.length - 1 ? (
          <button type="button" onClick={() => setStep(stepAfter(step, 1))} className={buttonClass}>
            Next
          </button>
        ) : (
          <Link href="/admin" className={buttonClass}>
            Go to your overview
          </Link>
        )}
        <Link href="/admin" className="ml-auto text-sm text-muted hover:text-foreground">
          Skip the guide
        </Link>
      </div>
    </AdminPage>
  );
}

function LookStep({ settings }: { settings: FirmSettings }) {
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
        <div role="group" aria-label="Brand colors" className="flex flex-wrap items-center gap-2.5">
          {brandSwatches.map((swatch) => (
            <button
              key={swatch}
              type="button"
              aria-label={`Brand color ${swatch}`}
              aria-pressed={accent.toLowerCase() === swatch}
              disabled={save.isPending}
              onClick={() => choose(swatch)}
              className={`size-9 rounded-full border-2 p-0.5 ${accent.toLowerCase() === swatch ? "border-foreground" : "border-transparent"}`}
            >
              <span className="block size-full rounded-full" style={{ background: swatch }} />
            </button>
          ))}
        </div>
        <p className="text-sm text-muted">
          Saved as soon as you choose. More colors, themes and a preview are under{" "}
          <Link href="/admin/design" className="text-accent hover:underline">
            Portal design
          </Link>
          .
        </p>
        <ErrorText error={save.error} />
      </Panel>
    </>
  );
}

function PriceStep({ settings }: { settings: FirmSettings }) {
  const challenges = useChallenges(settings.status !== "Provisioning");
  const prices = usePrices();
  const first = challenges.data?.[0];

  if (settings.status === "Provisioning") {
    return <Message text="Your trading server is being set up, with your first challenge. This takes a few seconds." />;
  }

  if (!challenges.data || !prices.data) {
    return <ErrorText error={challenges.error ?? prices.error} />;
  }

  return (
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
          <PriceControl challenge={first} price={prices.data.find((p) => p.challengeId === first.id)} />
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
  );
}

function PaymentsStep({ settings }: { settings: FirmSettings }) {
  const save = useSavePayments();
  const payments = settings.payments;

  const startTestPayments = () =>
    save.mutate({ provider: "Test", stripeSecretKey: "", stripeWebhookSecret: "", checkoutUrl: payments.checkoutUrl ?? "", termsUrl: payments.termsUrl ?? "" });

  return (
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
        payments.testPaymentsAllowed && (
          <button type="button" disabled={save.isPending} onClick={startTestPayments} className={`${buttonClass} self-start`}>
            {save.isPending ? "Saving..." : "Use test payments for now"}
          </button>
        )
      )}
      <p className="text-sm text-muted">
        Test payments are enough to try your shop. Before you go live, connect Stripe with your secret key, or your own checkout page, under{" "}
        <Link href="/admin/checkout" className="text-accent hover:underline">
          Checkout
        </Link>
        .
      </p>
      <ErrorText error={save.error} />
    </Panel>
  );
}

function TryStep({ settings }: { settings: FirmSettings }) {
  return (
    <Panel title="Your shop">
      <p className="text-sm text-muted">
        Buy your challenge with a test payment and an email of your own, choose a password from the email, and open the terminal from your account. Then
        you see everything your traders will.
      </p>
      <a href={new URL("buy", settings.portalUrl).toString()} target="_blank" rel="noreferrer" className={`${buttonClass} flex items-center gap-2 self-start`}>
        Open your shop
        <ExternalIcon className="size-4" />
      </a>
      <p className="text-sm text-muted">
        The overview has the rest of the way to live: our review of your company, and choosing your slots.
      </p>
    </Panel>
  );
}

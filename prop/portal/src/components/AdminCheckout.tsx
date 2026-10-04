"use client";

import { useState } from "react";

import type { FirmSettings, PaymentProvider } from "@/lib/api/types";
import { providerLabels } from "@/lib/orders";
import { useFirmSettings, useSavePayments } from "@/lib/queries";

import { AdminPage, buttonClass, ErrorText, fieldClass, Message, PageHeader, Panel } from "./ui";

/** How the firm's portal takes payment for challenges, and the terms buyers accept. */
export function AdminCheckout() {
  const settings = useFirmSettings();
  if (settings.isError) {
    return <Message text={settings.error.message} />;
  }

  if (!settings.data) {
    return <Message text="Loading..." />;
  }

  return (
    <AdminPage narrow>
      <PageHeader
        title="Checkout"
        description={
          <>
            Traders buy challenges in your portal&apos;s shop at <span className="font-mono text-foreground">{new URL("buy", settings.data.portalUrl).toString()}</span>. The money goes
            straight to you, and a paid order starts its challenge.
          </>
        }
      />
      <Payments key={JSON.stringify(settings.data.payments)} settings={settings.data} />
    </AdminPage>
  );
}

/** The Stripe events that move orders along. The firm picks them when it adds the webhook in Stripe. */
const stripeEvents = [
  "checkout.session.completed",
  "checkout.session.async_payment_succeeded",
  "checkout.session.async_payment_failed",
  "checkout.session.expired",
  "charge.refunded",
  "charge.dispute.created",
];

/** How the portal takes payment for challenges: a test page in the sandbox, Stripe with the firm's own keys, or the firm's own checkout. */
function Payments({ settings }: { settings: FirmSettings }) {
  const save = useSavePayments();
  const payments = settings.payments;
  const [provider, setProvider] = useState<PaymentProvider | null>(payments.provider);
  const [stripeSecretKey, setStripeSecretKey] = useState("");
  const [stripeWebhookSecret, setStripeWebhookSecret] = useState("");
  const [checkoutUrl, setCheckoutUrl] = useState(payments.checkoutUrl ?? "");
  const [termsUrl, setTermsUrl] = useState(payments.termsUrl ?? "");

  const choices: { value: PaymentProvider | null; label: string; hint: string }[] = [
    { value: null, label: "No sales in the portal", hint: "You sell elsewhere and start challenges through the firm API or the admin panel." },
    ...(payments.testPaymentsAllowed || payments.provider === "Test"
      ? [{ value: "Test" as const, label: providerLabels.Test, hint: "A test page instead of a payment, so you can try a purchase. Only in the sandbox." }]
      : []),
    { value: "Stripe", label: providerLabels.Stripe, hint: "Buyers pay on Stripe's page, and the money goes to your own Stripe account." },
    { value: "External", label: providerLabels.External, hint: "Buyers pay on your own page, with any provider. Your systems tell us when an order is paid." },
  ];

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    save.mutate({ provider, stripeSecretKey, stripeWebhookSecret, checkoutUrl, termsUrl });
  };

  return (
    <Panel title="How traders pay">
      <form onSubmit={submit} className="flex flex-col gap-4 text-sm">
        <fieldset className="flex flex-col gap-2">
          <legend className="sr-only">How the portal takes payment</legend>
          {choices.map((choice) => (
            <label key={choice.label} className="flex items-start gap-2">
              <input type="radio" name="provider" checked={provider === choice.value} onChange={() => setProvider(choice.value)} className="mt-1" />
              <span>
                {choice.label}
                <span className="block text-xs text-muted">{choice.hint}</span>
              </span>
            </label>
          ))}
        </fieldset>

        {provider === "Stripe" && (
          <div className="flex flex-col gap-3 border-t border-border pt-4">
            <p className="text-muted">
              {payments.hasStripeKeys
                ? `Your Stripe keys are saved (${payments.stripeTestMode ? "test mode" : "live mode"}). Leave the fields empty to keep them.`
                : "Paste your Stripe keys. They are kept encrypted and never shown again."}
              {settings.status !== "Live" && " In the sandbox, only test keys (sk_test_) work."}
            </p>
            <div className="grid gap-3 sm:grid-cols-2">
              <label className="flex flex-col gap-1">
                <span className="text-muted">Secret key</span>
                <input
                  type="password"
                  autoComplete="off"
                  value={stripeSecretKey}
                  onChange={(e) => setStripeSecretKey(e.target.value)}
                  placeholder="sk_test_..."
                  className={`${fieldClass} font-mono`}
                />
              </label>
              <label className="flex flex-col gap-1">
                <span className="text-muted">Webhook signing secret</span>
                <input
                  type="password"
                  autoComplete="off"
                  value={stripeWebhookSecret}
                  onChange={(e) => setStripeWebhookSecret(e.target.value)}
                  placeholder="whsec_..."
                  className={`${fieldClass} font-mono`}
                />
              </label>
            </div>
            <p className="text-muted">
              In Stripe, add a webhook endpoint at <span className="break-all font-mono text-foreground">{payments.stripeWebhookUrl}</span> for these
              events: <span className="font-mono">{stripeEvents.join(", ")}</span>. Its signing secret goes in the field above.
            </p>
          </div>
        )}

        {provider === "External" && (
          <div className="flex flex-col gap-3 border-t border-border pt-4">
            <label className="flex flex-col gap-1">
              <span className="text-muted">Your checkout page (https)</span>
              <input type="url" value={checkoutUrl} onChange={(e) => setCheckoutUrl(e.target.value)} placeholder="https://" className={fieldClass} />
            </label>
            <p className="text-muted">
              We send the buyer to this page with <span className="font-mono">order</span> and <span className="font-mono">return</span> in the
              address. Your page reads the order with <span className="font-mono">GET /api/firm/v1/orders/&#123;order&#125;</span>, takes the payment
              and calls <span className="font-mono">POST /api/firm/v1/orders/&#123;order&#125;/mark-paid</span>. Then it sends the buyer to the
              return address.
            </p>
          </div>
        )}

        <label className="flex flex-col gap-1 border-t border-border pt-4">
          <span className="text-muted">Your terms for buyers (https), or empty for none</span>
          <input type="url" value={termsUrl} onChange={(e) => setTermsUrl(e.target.value)} placeholder="https://" className={fieldClass} />
        </label>

        {payments.provider !== null && !payments.active && (
          <p className="text-warning">
            {payments.provider === "Stripe"
              ? "Stripe's live keys work once the firm is live. Use test keys in the sandbox."
              : "Payments are not working with these settings."}
          </p>
        )}
        <ErrorText error={save.error} />
        {save.isSuccess && <p className="text-profit">Saved.</p>}
        <button type="submit" disabled={save.isPending} className={`${buttonClass} self-start`}>
          {save.isPending ? "Saving..." : "Save payments"}
        </button>
      </form>
    </Panel>
  );
}

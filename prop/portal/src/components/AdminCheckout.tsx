"use client";

import { useState } from "react";

import type { FirmSettings, PaymentProvider } from "@/lib/api/types";
import { providerLabels } from "@/lib/orders";
import { useFirmSettings, useSavePayments } from "@/lib/queries";

import { CopyButton } from "./CopyButton";
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

  const shop = new URL("buy", settings.data.portalUrl).toString();
  return (
    <AdminPage narrow>
      <PageHeader
        title="Checkout"
        description={
          <>
            Traders buy challenges in your portal&apos;s shop at{" "}
            <span className="inline-flex items-center gap-1.5">
              <a href={shop} target="_blank" rel="noreferrer" className="font-mono text-accent hover:underline">
                {shop}
              </a>
              <CopyButton value={shop} label="Shop address" />
            </span>
            . Link to it from your website. The money goes straight to you, and a paid order starts its challenge.
          </>
        }
      />
      <Payments settings={settings.data} />
    </AdminPage>
  );
}

/** How the portal takes payment for challenges: a test page in the sandbox, Stripe with the firm's own keys, or the firm's own checkout. */
function Payments({ settings }: { settings: FirmSettings }) {
  const save = useSavePayments();
  const payments = settings.payments;
  const [provider, setProvider] = useState<PaymentProvider | null>(payments.provider);
  const [stripeSecretKey, setStripeSecretKey] = useState("");
  const [stripeWebhookSecret, setStripeWebhookSecret] = useState("");
  const [ownWebhook, setOwnWebhook] = useState(false);
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

  // The form stays, so it can say it saved; the keys are cleared, since they are never shown again.
  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    save.mutate(
      { provider, stripeSecretKey, stripeWebhookSecret: ownWebhook ? stripeWebhookSecret : "", checkoutUrl, termsUrl },
      {
        onSuccess: () => {
          setStripeSecretKey("");
          setStripeWebhookSecret("");
        },
      },
    );
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
                ? `Your Stripe secret key is saved (${payments.stripeTestMode ? "test mode" : "live mode"}). Leave the field empty to keep it.`
                : "Paste the secret key from your Stripe dashboard, under Developers and API keys. We add the webhook that tells us about payments to your Stripe account for you. The key is kept encrypted and never shown again."}
            </p>
            {settings.status !== "Live" && (
              <p className="text-muted">
                In the sandbox, a test key (sk_test_) takes test payments. A live key (sk_live_) can be saved now, and takes payment from the moment you
                go live.
              </p>
            )}
            <label className="flex flex-col gap-1">
              <span className="text-muted">Secret key</span>
              <input
                type="password"
                autoComplete="off"
                value={stripeSecretKey}
                onChange={(e) => setStripeSecretKey(e.target.value)}
                placeholder={settings.status === "Live" ? "sk_live_..." : "sk_test_... or sk_live_..."}
                className={`${fieldClass} font-mono`}
              />
            </label>
            <label className="flex items-start gap-2">
              <input type="checkbox" checked={ownWebhook} onChange={(e) => setOwnWebhook(e.target.checked)} className="mt-1" />
              <span>
                I add the webhook in Stripe myself
                <span className="block text-xs text-muted">For example with a restricted key that cannot add webhooks.</span>
              </span>
            </label>
            {ownWebhook && (
              <div className="flex flex-col gap-3 rounded border border-border p-3">
                <p className="text-muted">
                  In Stripe, add a webhook endpoint at <span className="break-all font-mono text-foreground">{payments.stripeWebhookUrl}</span> for these
                  events: <span className="font-mono">{payments.stripeWebhookEvents.join(", ")}</span>. Then paste its signing secret here, with the
                  secret key.
                </p>
                <label className="flex flex-col gap-1">
                  <span className="text-muted">Webhook signing secret</span>
                  <input
                    type="password"
                    autoComplete="off"
                    required={stripeSecretKey.length > 0}
                    value={stripeWebhookSecret}
                    onChange={(e) => setStripeWebhookSecret(e.target.value)}
                    placeholder="whsec_..."
                    className={`${fieldClass} font-mono`}
                  />
                </label>
              </div>
            )}
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
          <span className="text-muted">Your terms for traders (https), or empty for none</span>
          <input type="url" value={termsUrl} onChange={(e) => setTermsUrl(e.target.value)} placeholder="https://" className={fieldClass} />
          <span className="text-xs text-muted">Buyers accept them in your shop, and we read them in your application. One address for both.</span>
        </label>

        {payments.provider !== null && !payments.active && (
          <p className="text-warning">
            {payments.provider === "Stripe"
              ? "Your live key takes payment from the moment you go live. Until then your shop takes no payment: use a test key (sk_test_) to try a purchase."
              : "Payments are not working with these settings."}
          </p>
        )}
        <ErrorText error={save.error} />
        {save.isSuccess && !save.isPending && (
          <p role="status" className="text-profit">
            Saved.
          </p>
        )}
        <button type="submit" disabled={save.isPending} className={`${buttonClass} self-start`}>
          {save.isPending ? "Saving..." : "Save payments"}
        </button>
      </form>
    </Panel>
  );
}

"use client";

import Link from "next/link";
import { useId, useState } from "react";

import { useBranding } from "@/app/providers";

import type { IdentityMode, IdentityRequirement, IdentitySettings } from "@/lib/api/types";
import { formatDateTime, formatMoney } from "@/lib/format";
import { identityModes, readinessText, requirementLabels } from "@/lib/identity";
import { FieldError, useFirmSettings, useIdentitySettings, useSaveIdentitySettings } from "@/lib/queries";

import { CopyButton } from "./CopyButton";
import { AlertIcon, CheckIcon } from "./icons";
import { AdminPage, buttonClass, ErrorText, fieldClass, Message, PageHeader, Panel, secondaryButtonClass } from "./ui";

/**
 * The firm's KYC, how it checks its traders' IDs (ADR 0042): with our built-in check or its own service, and what waits
 * until a trader is verified. Neither is chosen for the firm, and going live waits until one is, and until its own
 * service has worked through the whole flow. Our review does not wait for it.
 */
export function AdminIdentity() {
  const settings = useIdentitySettings();
  const branding = useBranding();
  if (settings.isError) {
    return <Message text={settings.error.message} />;
  }

  if (!settings.data) {
    return <Message text="Loading..." />;
  }

  return (
    <AdminPage narrow>
      <PageHeader
        title="KYC"
        description="Know your customer: check who your traders are before you pay them. Choose how it is done, and what waits until it is. The trader's card shows the outcome."
        actions={
          branding.status === "Sandbox" && (
            <Link href="/admin/go-live" className={secondaryButtonClass}>
              Go live
            </Link>
          )
        }
      />
      <WhyKyc />
      <SettingsForm settings={settings.data} />
    </AdminPage>
  );
}

/** Why a firm checks its traders, in a few lines. */
function WhyKyc() {
  return (
    <section aria-labelledby="why-kyc" className="flex flex-col gap-2 rounded-lg border border-border bg-panel px-5 py-4 text-sm">
      <h2 id="why-kyc" className="font-medium">
        Why KYC matters
      </h2>
      <ul className="flex list-disc flex-col gap-1 pl-5 text-muted">
        <li>You know who you pay. Payouts go to a checked person, not to a stolen identity.</li>
        <li>It stops one person from passing with many accounts, or from a country you do not serve.</li>
        <li>Banks and payment providers often ask how you check the people you pay.</li>
      </ul>
    </section>
  );
}

function SettingsForm({ settings }: { settings: IdentitySettings }) {
  const save = useSaveIdentitySettings();
  const firm = useFirmSettings();
  const branding = useBranding();
  const urlId = useId();
  const [mode, setMode] = useState<IdentityMode | null>(settings.mode);
  const [requiredBefore, setRequiredBefore] = useState(settings.requiredBefore);
  const [checkAddress, setCheckAddress] = useState(settings.checkAddress);
  const [checkSanctions, setCheckSanctions] = useState(settings.checkSanctions);
  const [externalUrl, setExternalUrl] = useState(settings.externalUrl ?? "");
  const { prices } = settings;
  const money = (amount: number) => `${formatMoney(amount)} ${prices.currency}`;
  const urlInvalid = save.error instanceof FieldError && save.error.field === "externalUrl";
  const live = branding.status === "Live";
  const notReady = readinessText(settings.readiness, live);

  // The test of the firm's own service is about the address that is saved.
  const savedService = mode === "External" && settings.mode === "External" && externalUrl.trim() === settings.externalUrl;

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        if (mode) {
          save.mutate({ mode, requiredBefore, checkAddress, checkSanctions, externalUrl: mode === "External" ? externalUrl.trim() : null });
        }
      }}
      className="flex flex-col gap-6"
    >
      {settings.readiness === "NotChosen" && notReady && (
        <p role="status" className="flex gap-2.5 rounded-lg border border-warning/40 bg-warning/10 px-4 py-3 text-sm">
          <AlertIcon className="mt-0.5 size-4 shrink-0 text-warning" />
          {notReady}
        </p>
      )}

      <Panel title="How traders are checked">
        <div role="radiogroup" aria-label="How traders are checked" className="flex flex-col gap-2.5">
          {identityModes.map((option) => (
            <label
              key={option.mode}
              className={`flex cursor-pointer items-start gap-3 rounded-lg border p-4 ${mode === option.mode ? "border-accent bg-accent/5" : "border-border hover:border-muted"}`}
            >
              <input type="radio" name="mode" checked={mode === option.mode} onChange={() => setMode(option.mode)} className="mt-1" />
              <span className="flex flex-col gap-1">
                <span className="font-medium">{option.label}</span>
                <span className="text-sm text-muted">{option.description}</span>
                {option.mode === "BuiltIn" && (
                  <span className="text-sm">
                    {money(prices.monthlyPrice)} a month with {prices.included} checks included, then {money(prices.perCheck)} for each check. Added to your monthly bill.
                  </span>
                )}
                {option.mode === "External" && (
                  <span className="text-sm">It must work through the whole flow once before you go live. We show you how below.</span>
                )}
              </span>
            </label>
          ))}
        </div>

        {mode === "BuiltIn" && (
          <div className="flex flex-col gap-3 border-t border-border pt-4 text-sm">
            <span className="font-medium">Also check</span>
            <label className="flex items-start gap-2.5">
              <input type="checkbox" checked={checkAddress} onChange={(e) => setCheckAddress(e.target.checked)} className="mt-0.5" />
              <span className="flex flex-col">
                <span>The trader&apos;s address, from a bank statement or a bill · {money(prices.address)} a check</span>
                <span className="text-xs text-muted">Approval ticks Address checked too.</span>
              </span>
            </label>
            <label className="flex items-start gap-2.5">
              <input type="checkbox" checked={checkSanctions} onChange={(e) => setCheckSanctions(e.target.checked)} className="mt-0.5" />
              <span className="flex flex-col">
                <span>Sanctions and politically exposed persons lists · {money(prices.sanctions)} a check</span>
                <span className="text-xs text-muted">A trader on a list is not approved.</span>
              </span>
            </label>
            {settings.testChecks ? (
              <p className="rounded-md bg-warning/10 px-3 py-2 text-warning">
                These are test checks: your traders choose the outcome on a test page, and nothing is charged.
                {branding.status === "Sandbox" && " Real checks start when you go live."}
              </p>
            ) : (
              settings.mode === "BuiltIn" && (
                <p className="text-muted">
                  {settings.checksSinceLastCharge === 1 ? "1 check" : `${settings.checksSinceLastCharge} checks`} since your last bill.
                </p>
              )
            )}
          </div>
        )}

        {mode === "External" && (
          <div className="flex flex-col gap-4 border-t border-border pt-4 text-sm">
            <div className="flex flex-col gap-1">
              <label htmlFor={urlId} className="text-muted">
                Your page where traders are checked
              </label>
              <input
                id={urlId}
                value={externalUrl}
                onChange={(e) => setExternalUrl(e.target.value)}
                placeholder="https://kyc.yourfirm.com/start?trader={traderId}&email={email}"
                aria-invalid={urlInvalid || undefined}
                className={`${fieldClass} font-mono text-xs`}
              />
              <span className="text-xs text-muted">
                We put the trader&apos;s id in place of {"{traderId}"} and the email in place of {"{email}"}.
              </span>
            </div>
            {firm.data && <ExternalApi firmApiUrl={firm.data.firmApiUrl} />}
            {savedService ? <ServiceTest settings={settings} live={live} /> : <p className="text-muted">Save the address, then try the whole flow once.</p>}
          </div>
        )}
      </Panel>

      {mode !== null && (
        <Panel title="What waits for the check">
          <div role="radiogroup" aria-label="What waits for the check" className="flex flex-col gap-2 text-sm">
            {(Object.keys(requirementLabels) as IdentityRequirement[]).map((requirement) => (
              <label key={requirement} className="flex items-start gap-2.5">
                <input type="radio" name="requiredBefore" checked={requiredBefore === requirement} onChange={() => setRequiredBefore(requirement)} className="mt-0.5" />
                <span className="flex flex-col">
                  <span>{requirementLabels[requirement]}</span>
                  <span className="text-xs text-muted">
                    {requirement === "FirstPayout"
                      ? "The trader cannot ask for a payout until verified. Most firms choose this."
                      : "You cannot approve the funded account until the trader is verified."}
                  </span>
                </span>
              </label>
            ))}
          </div>
          <p className="text-xs text-muted">As an exception, you can tick ID checked on a trader&apos;s card by hand, which lets the trader through.</p>
        </Panel>
      )}

      <div className="flex flex-wrap items-center justify-end gap-3">
        {save.isSuccess && <span className="text-sm text-profit">Saved.</span>}
        {mode === null && <span className="text-sm text-muted">Choose one of the two first.</span>}
        <ErrorText error={save.error} />
        <button type="submit" disabled={mode === null || save.isPending} className={buttonClass}>
          {save.isPending ? "Saving..." : "Save"}
        </button>
      </div>
    </form>
  );
}

/**
 * Whether the firm's own service has worked through the whole flow at its saved address: a trader started the check in
 * the portal and the service reported the outcome. Until it has, how to try it.
 */
function ServiceTest({ settings, live }: { settings: IdentitySettings; live: boolean }) {
  const branding = useBranding();
  if (settings.readiness === "Ready") {
    return (
      <p role="status" className="flex gap-2.5 rounded-md border border-profit/40 bg-profit/10 px-3 py-2">
        <CheckIcon className="mt-0.5 size-4 shrink-0 text-profit" />
        <span>
          The whole flow works: on {settings.externalTestedAt ? formatDateTime(settings.externalTestedAt) : "a test"} a trader was sent to your page, and your service
          reported the outcome.
        </span>
      </p>
    );
  }

  return (
    <div role="status" className="flex flex-col gap-2 rounded-md border border-warning/40 bg-warning/10 px-3 py-3">
      <span className="flex gap-2.5 font-medium">
        <AlertIcon className="mt-0.5 size-4 shrink-0 text-warning" />
        {readinessText(settings.readiness, live)}
      </span>
      <ol className="flex list-decimal flex-col gap-1 pl-9">
        <li>Start a challenge for yourself under Accounts, and open the invitation as the trader.</li>
        <li>Under Payouts, choose Verify with {branding.name}. You land on your page.</li>
        <li>Let your service report Approved or Declined for that trader, as in the example above.</li>
      </ol>
      <span className="pl-6.5 text-xs text-muted">This page shows it here when it has worked.</span>
    </div>
  );
}

/** How the firm's systems tell us the outcome of their own check. */
function ExternalApi({ firmApiUrl }: { firmApiUrl: string }) {
  const example = `curl -X PUT ${firmApiUrl}traders/identity \\
  -H "X-Api-Key: YOUR_KEY" -H "Content-Type: application/json" \\
  -d '{"email":"trader@example.com","status":"Approved","fullName":"Anna Andersson","dateOfBirth":"1990-04-01","country":"Sweden"}'`;
  return (
    <div className="flex flex-col gap-2">
      <span className="text-muted">
        When your service has checked a trader, tell us with your firm API key: the status is Approved, Declined with a reason, or InReview. Approval ticks ID checked.
      </span>
      <div className="relative">
        <pre className="overflow-x-auto rounded-md border border-border bg-background p-3 pr-12 font-mono text-xs">{example}</pre>
        <span className="absolute right-2 top-2">
          <CopyButton value={example} label="the example" />
        </span>
      </div>
    </div>
  );
}

"use client";

import { useState } from "react";

import type { FirmSettings } from "@/lib/api/types";
import { formatDateTime } from "@/lib/format";
import { deliveryNote, firmApiCalls, startChallengeExample } from "@/lib/integrations";
import { useChallenges, useFirmSettings, useNewApiKey, useNewWebhookSecret, useSaveWebhook, useSendTestWebhook, useWebhookOverview } from "@/lib/queries";
import { useSavedNote } from "@/lib/useSavedNote";

import { CopyButton } from "./CopyButton";
import { ConfirmDialog } from "./Dialog";
import { AdminPage, Badge, buttonClass, ErrorText, fieldClass, Loading, Message, PageHeader, Panel, secondaryButtonClass } from "./ui";

/** How the firm's own systems work with the platform: the firm API, webhooks, and the addresses they need. */
export function AdminIntegrations() {
  const settings = useFirmSettings();
  if (settings.isError) {
    return <Message text={settings.error.message} />;
  }

  if (!settings.data) {
    return <Loading />;
  }

  const data = settings.data;
  return (
    <AdminPage narrow>
      <PageHeader title="Integrations" description="Your website, checkout or CRM can start challenges, follow accounts and hear about what happens to them." />
      <Panel title="Your firm">
        <dl className="grid gap-x-6 gap-y-3 text-sm sm:grid-cols-2">
          <Row label="Short name" value={data.id} mono />
          <Row label="Portal" value={data.portalUrl} mono />
          <Row label="Trading server" value={data.tradingServer ?? "Being set up"} mono />
          <Row label="Account currency" value={data.currency ?? "-"} />
        </dl>
      </Panel>
      <Integration settings={data} />
    </AdminPage>
  );
}

function Row({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-muted">{label}</dt>
      <dd className={`break-all ${mono ? "font-mono" : ""}`}>{value}</dd>
    </div>
  );
}

/** The firm's own systems: a key for the firm API, and webhooks signed with a secret. */
function Integration({ settings }: { settings: FirmSettings }) {
  const newKey = useNewApiKey();
  const saveWebhook = useSaveWebhook();
  useSavedNote(saveWebhook);
  const newSecret = useNewWebhookSecret();
  const [webhookUrl, setWebhookUrl] = useState(settings.webhookUrl ?? "");

  const [asking, setAsking] = useState<"key" | "secret" | null>(null);

  const createKey = () => (settings.hasApiKey ? setAsking("key") : newKey.mutate());
  const rotateSecret = () => setAsking("secret");

  const submitWebhook = (event: React.FormEvent) => {
    event.preventDefault();
    saveWebhook.mutate(webhookUrl);
  };

  const secret = newSecret.data ?? saveWebhook.data?.secret ?? null;
  const challenges = useChallenges(settings.status !== "Provisioning");
  const example = startChallengeExample(settings.firmApiUrl, challenges.data?.[0]?.id ?? "two-step-100k");

  return (
    <Panel title="Firm API">
      <div className="flex flex-col gap-3 text-sm">
        <p className="text-muted">
          Your website, checkout or CRM starts challenges and follows accounts through the firm API, with your key in the{" "}
          <span className="font-mono">X-Api-Key</span> header.
        </p>
        <p className="flex flex-wrap items-center gap-2">
          <span className="text-muted">Address</span>
          <span className="break-all font-mono">{settings.firmApiUrl}</span>
          <CopyButton value={settings.firmApiUrl} label="API address" />
        </p>
        <table className="w-full text-xs">
          <tbody>
            {firmApiCalls.map((call) => (
              <tr key={call.path} className="border-t border-border align-top">
                <td className="whitespace-nowrap py-1.5 pr-3 font-mono">
                  {call.method} {call.path}
                </td>
                <td className="py-1.5 text-muted">{call.text}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <div className="flex flex-col gap-1.5">
          <span className="flex items-center gap-2 text-muted">
            Start a challenge from a terminal
            <CopyButton value={example} label="Example" />
          </span>
          <pre className="overflow-x-auto rounded border border-border bg-background p-3 font-mono text-xs">{example}</pre>
        </div>
        <p className="text-muted">
          Every call and field is in the{" "}
          <a href={settings.openApiUrl} target="_blank" rel="noreferrer" className="text-accent hover:underline">
            OpenAPI description
          </a>
          , which tools such as Postman read, and code generators make a client from.
        </p>
        <div className="flex flex-wrap items-center gap-3">
          <span>{settings.hasApiKey ? "The firm has a key." : "No key yet."}</span>
          <button type="button" onClick={createKey} disabled={newKey.isPending} className={secondaryButtonClass}>
            {settings.hasApiKey ? "Replace key" : "Create key"}
          </button>
        </div>
        <ErrorText error={newKey.error} />
        {newKey.data && <ShownOnce label="Your API key" value={newKey.data} />}
      </div>

      <form onSubmit={submitWebhook} className="flex flex-col gap-3 border-t border-border pt-4 text-sm">
        <h3 className="font-medium">Webhooks</h3>
        <p className="text-muted">
          We send events, such as a passed stage or a payout request, to this address. Each one is signed in the{" "}
          <span className="font-mono">Prop-Signature</span> header with HMAC-SHA256 and your secret.
        </p>
        <div className="flex flex-wrap items-end gap-3">
          <label className="flex min-w-72 flex-1 flex-col gap-1">
            <span className="text-muted">Address (https), or empty for no webhooks</span>
            <input type="url" value={webhookUrl} onChange={(e) => setWebhookUrl(e.target.value)} placeholder="https://" className={fieldClass} />
          </label>
          <button type="submit" disabled={saveWebhook.isPending} className={secondaryButtonClass}>
            Save address
          </button>
          {settings.webhookUrl && (
            <button type="button" onClick={rotateSecret} disabled={newSecret.isPending} className={secondaryButtonClass}>
              New secret
            </button>
          )}
        </div>
        <ErrorText error={saveWebhook.error ?? newSecret.error} />
        {secret && <ShownOnce label="Your webhook secret" value={secret} />}
      </form>
      <Deliveries hasWebhook={settings.webhookUrl !== null} />
      <ConfirmDialog
        open={asking === "key"}
        onClose={() => setAsking(null)}
        onConfirm={() => newKey.mutate(undefined, { onSuccess: () => setAsking(null) })}
        title="Replace the API key?"
        description="The old key stops working at once, so your systems need the new one before they call us again."
        confirmLabel="Replace the key"
        pendingLabel="Replacing..."
        pending={newKey.isPending}
        danger
      >
        <ErrorText error={newKey.error} />
      </ConfirmDialog>
      <ConfirmDialog
        open={asking === "secret"}
        onClose={() => setAsking(null)}
        onConfirm={() => newSecret.mutate(undefined, { onSuccess: () => setAsking(null) })}
        title="Make a new webhook secret?"
        description="Webhooks are signed with the new secret from now on, so your system must check them with it."
        confirmLabel="Make a new secret"
        pendingLabel="Making..."
        pending={newSecret.isPending}
        danger
      >
        <ErrorText error={newSecret.error} />
      </ConfirmDialog>
    </Panel>
  );
}

/** The events webhooks tell about, a test event to send, and how the latest webhooks went. */
function Deliveries({ hasWebhook }: { hasWebhook: boolean }) {
  const overview = useWebhookOverview();
  const test = useSendTestWebhook();
  const deliveries = overview.data?.deliveries ?? [];

  return (
    <div className="flex flex-col gap-3 border-t border-border pt-4 text-sm">
      <details>
        <summary className="cursor-pointer font-medium">Events we send</summary>
        <table className="mt-2 w-full text-xs">
          <tbody>
            {(overview.data?.events ?? []).map((event) => (
              <tr key={event.type} className="border-t border-border align-top">
                <td className="whitespace-nowrap py-1.5 pr-3 font-mono">{event.type}</td>
                <td className="py-1.5 text-muted">{event.description}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>
      <div className="flex flex-wrap items-center gap-3">
        <h3 className="font-medium">Latest webhooks</h3>
        <button type="button" disabled={!hasWebhook || test.isPending} onClick={() => test.mutate()} className={`${secondaryButtonClass} ml-auto text-sm`}>
          {test.isPending ? "Sending..." : "Send a test event"}
        </button>
      </div>
      {!hasWebhook && <p className="text-muted">Save an address to send a test event.</p>}
      {test.isSuccess && <p className="text-muted">The test event webhook.test is on its way, and shows below in a few seconds.</p>}
      <ErrorText error={test.error ?? overview.error} />
      {deliveries.length === 0 ? (
        <p className="text-muted">No webhooks yet.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[32rem] text-xs">
            <thead className="text-left text-muted">
              <tr>
                <th scope="col" className="py-1.5 pr-3 font-normal">
                  Created
                </th>
                <th scope="col" className="py-1.5 pr-3 font-normal">
                  Event
                </th>
                <th scope="col" className="py-1.5 pr-3 font-normal">
                  Status
                </th>
                <th scope="col" className="py-1.5 font-normal">
                  <span className="sr-only">How it went</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {deliveries.map((delivery) => (
                <tr key={delivery.id} className="border-t border-border align-top">
                  <td className="whitespace-nowrap py-1.5 pr-3 text-muted">{formatDateTime(delivery.createdAt)}</td>
                  <td className="py-1.5 pr-3 font-mono">{delivery.eventType}</td>
                  <td className="py-1.5 pr-3">
                    <Badge tone={delivery.status === "Delivered" ? "profit" : delivery.status === "Failed" ? "loss" : "warning"}>{delivery.status}</Badge>
                  </td>
                  <td className="py-1.5 text-muted">{deliveryNote(delivery)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

/** A key or secret that is shown only now. */
function ShownOnce({ label, value }: { label: string; value: string }) {
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    await navigator.clipboard.writeText(value);
    setCopied(true);
  };

  return (
    <div className="flex flex-col gap-2 rounded border border-warning/40 bg-warning/10 p-3">
      <span className="text-warning">{label}. Copy it now: it is not shown again.</span>
      <div className="flex flex-wrap items-center gap-3">
        <code className="break-all font-mono">{value}</code>
        <button type="button" onClick={copy} className={`${buttonClass} text-xs`}>
          {copied ? "Copied" : "Copy"}
        </button>
      </div>
    </div>
  );
}

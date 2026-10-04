"use client";

import { useState } from "react";

import type { FirmSettings } from "@/lib/api/types";
import { useFirmSettings, useNewApiKey, useNewWebhookSecret, useSaveWebhook } from "@/lib/queries";

import { AdminPage, buttonClass, ErrorText, fieldClass, Message, PageHeader, Panel, secondaryButtonClass } from "./ui";

/** How the firm's own systems work with the platform: the firm API, webhooks, and the addresses they need. */
export function AdminIntegrations() {
  const settings = useFirmSettings();
  if (settings.isError) {
    return <Message text={settings.error.message} />;
  }

  if (!settings.data) {
    return <Message text="Loading..." />;
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
  const newSecret = useNewWebhookSecret();
  const [webhookUrl, setWebhookUrl] = useState(settings.webhookUrl ?? "");

  const createKey = () => {
    if (!settings.hasApiKey || window.confirm("Replace the key? The old key stops working at once.")) {
      newKey.mutate();
    }
  };

  const rotateSecret = () => {
    if (window.confirm("Make a new secret? Webhooks are signed with it from now on.")) {
      newSecret.mutate();
    }
  };

  const submitWebhook = (event: React.FormEvent) => {
    event.preventDefault();
    saveWebhook.mutate(webhookUrl);
  };

  const secret = newSecret.data ?? saveWebhook.data?.secret ?? null;

  return (
    <Panel title="Integration">
      <div className="flex flex-col gap-3 text-sm">
        <h3 className="font-medium">Firm API</h3>
        <p className="text-muted">
          Your website, checkout or CRM starts challenges and follows accounts through the firm API at <span className="font-mono">/api/firm/v1</span>,
          with the key in the <span className="font-mono">X-Api-Key</span> header.
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
        {saveWebhook.isSuccess && !secret && <p className="text-profit">Saved.</p>}
        {secret && <ShownOnce label="Your webhook secret" value={secret} />}
      </form>
    </Panel>
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

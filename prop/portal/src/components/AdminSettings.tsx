"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

import type { FirmSettings } from "@/lib/api/types";
import { useFirmSettings, useNewApiKey, useNewWebhookSecret, useSaveBranding, useSaveWebhook } from "@/lib/queries";
import { defaultColors, themeColors, themeStyle, type ThemeColor } from "@/lib/theme";

import { buttonClass, ErrorText, fieldClass, Panel, secondaryButtonClass } from "./ui";

const statusLabels: Record<FirmSettings["status"], string> = {
  Provisioning: "Setting up the trading server",
  Sandbox: "Sandbox",
  Live: "Live",
};

/** The firm's settings: what it is, how its portal looks, and how its own systems connect. */
export function AdminSettings() {
  const settings = useFirmSettings();

  return (
    <main className="mx-auto flex w-full max-w-4xl flex-col gap-6 p-6">
      <ErrorText error={settings.error} />
      {settings.data && (
        <>
          <Firm settings={settings.data} />
          <Look key={JSON.stringify([settings.data.logoUrl, settings.data.colors])} settings={settings.data} />
          <Integration settings={settings.data} />
        </>
      )}
    </main>
  );
}

function Firm({ settings }: { settings: FirmSettings }) {
  return (
    <Panel title="Firm">
      <dl className="grid gap-x-6 gap-y-3 text-sm sm:grid-cols-2">
        <Row label="Name" value={settings.name} />
        <Row label="Status" value={statusLabels[settings.status]} />
        <Row label="Portal" value={settings.portalUrl} mono />
        <Row label="Trading server" value={settings.tradingServer ?? "Being set up"} mono />
        {settings.sandboxMaxOpenAccounts !== null && (
          <Row label="Sandbox limit" value={`At most ${settings.sandboxMaxOpenAccounts} open challenge accounts`} />
        )}
      </dl>
      {settings.status !== "Live" && (
        <p className="text-xs text-muted">
          The firm is in the sandbox. Everything works, but with a few test traders only. Going live comes later, after a check of the company and its
          owners.
        </p>
      )}
    </Panel>
  );
}

function Row({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-muted">{label}</dt>
      <dd className={mono ? "font-mono" : ""}>{value}</dd>
    </div>
  );
}

/** The logo and colors, with a preview. Saving shows the new look at once. */
function Look({ settings }: { settings: FirmSettings }) {
  const router = useRouter();
  const save = useSaveBranding();
  const [logoUrl, setLogoUrl] = useState(settings.logoUrl ?? "");
  const [colors, setColors] = useState<Partial<Record<ThemeColor, string>>>(settings.colors);

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    save.mutate(
      { logoUrl: logoUrl.trim() || null, colors: colors as Record<string, string> },
      // The portal's look is set on the server before a page renders, so the page is rendered again.
      { onSuccess: () => router.refresh() },
    );
  };

  const setColor = (name: ThemeColor, value: string | null) =>
    setColors((current) => {
      const next = { ...current };
      if (value === null) {
        delete next[name];
      } else {
        next[name] = value;
      }

      return next;
    });

  return (
    <Panel title="Look">
      <form onSubmit={submit} className="flex flex-col gap-4">
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted">Logo address (https), or empty to show the firm&apos;s name</span>
          <input type="url" value={logoUrl} onChange={(e) => setLogoUrl(e.target.value)} placeholder="https://" className={fieldClass} />
        </label>

        <div className="grid gap-3 sm:grid-cols-3">
          {themeColors.map((name) => (
            <div key={name} className="flex items-center gap-2 text-sm">
              <input
                type="color"
                aria-label={`${name} color`}
                value={colors[name] ?? defaultColors[name]}
                onChange={(e) => setColor(name, e.target.value)}
                className="h-8 w-10 cursor-pointer rounded border border-border bg-transparent"
              />
              <span className="capitalize">{name}</span>
              {colors[name] !== undefined && (
                <button type="button" onClick={() => setColor(name, null)} className="ml-auto text-xs text-muted hover:text-foreground">
                  Default
                </button>
              )}
            </div>
          ))}
        </div>

        <Preview colors={colors} />

        <ErrorText error={save.error} />
        <button type="submit" disabled={save.isPending} className={`${buttonClass} self-start`}>
          {save.isPending ? "Saving..." : "Save look"}
        </button>
      </form>
    </Panel>
  );
}

function Preview({ colors }: { colors: Partial<Record<ThemeColor, string>> }) {
  return (
    <div style={themeStyle({ colors: colors as Record<string, string> }) as React.CSSProperties} className="rounded-lg border border-border bg-background p-4">
      <div className="flex flex-col gap-3 rounded border border-border bg-panel p-4 text-sm text-foreground">
        <span className="font-semibold">Preview</span>
        <span className="text-muted">Balance 102,500.00 · daily loss limit 97,000.00</span>
        <span className="flex gap-4">
          <span className="text-profit">+2,500.00</span>
          <span className="text-loss">Failed</span>
          <span className="text-warning">Waiting</span>
        </span>
        <span className="self-start rounded bg-accent px-3 py-1 font-medium text-white">Open terminal</span>
      </div>
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

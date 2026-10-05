"use client";

import Link from "next/link";
import { useState } from "react";

import type { FirmSettings } from "@/lib/api/types";
import { isOn, notificationKinds, type NotificationAudience } from "@/lib/notifications";
import { useFirmSettings, useSaveEmailSettings, useSaveSupportEmail } from "@/lib/queries";

import { TeamOnlyNote } from "./BeforeApproval";
import { AdminPage, ErrorText, fieldClass, Message, PageHeader, Panel, secondaryButtonClass } from "./ui";

/** Which emails we send for the firm: to its team when something waits for it, and to its traders as their challenges move on. */
export function AdminNotifications() {
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
        title="Notifications"
        description="The emails we send for you. Turn off those your own systems send, for example from webhooks. Invitations and password links are always sent."
      />
      <TeamOnlyNote />
      <SupportEmail settings={settings.data} />
      <Emails settings={settings.data} />
    </AdminPage>
  );
}

/** Where traders' replies go. The emails to traders come in the firm's name, logo and color. */
function SupportEmail({ settings }: { settings: FirmSettings }) {
  const save = useSaveSupportEmail();
  const [email, setEmail] = useState(settings.supportEmail ?? "");
  return (
    <Panel title="Replies from traders">
      <p className="text-sm text-muted">
        Emails to your traders come in your firm&apos;s name, with your logo and color. When a trader replies, the reply goes to this address. Without
        one, replies go nowhere.
      </p>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          save.mutate(email.trim());
        }}
        className="flex flex-wrap items-end gap-3"
      >
        <label className="flex min-w-64 flex-1 flex-col gap-1 text-sm">
          <span className="text-muted">Support email</span>
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="support@yourfirm.com" className={fieldClass} />
        </label>
        <button type="submit" disabled={save.isPending} className={secondaryButtonClass}>
          {save.isPending ? "Saving..." : "Save"}
        </button>
      </form>
      {save.isSuccess && <p className="text-sm text-profit">Saved.</p>}
      <ErrorText error={save.error} />
    </Panel>
  );
}

const audiences: { audience: NotificationAudience; title: string; note: React.ReactNode }[] = [
  {
    audience: "team",
    title: "To your team",
    note: (
      <>
        To every administrator on the{" "}
        <Link href="/admin/team" className="text-accent hover:underline">
          Team
        </Link>{" "}
        page.
      </>
    ),
  },
  { audience: "trader", title: "To your traders", note: "From your firm's name, in your look, with a button to the account in your portal." },
];

function Emails({ settings }: { settings: FirmSettings }) {
  const save = useSaveEmailSettings();

  return (
    <>
      {audiences.map(({ audience, title, note }) => (
        <Panel key={audience} title={title}>
          <p className="text-sm text-muted">{note}</p>
          <ul className="flex flex-col divide-y divide-border">
            {notificationKinds
              .filter((n) => n.audience === audience)
              .map((n) => {
                const on = isOn(settings.emailSettings, n.kind);
                return (
                  <li key={n.kind}>
                    <label className="flex cursor-pointer items-start justify-between gap-4 py-3 text-sm">
                      <span className="flex flex-col gap-0.5">
                        <span className="font-medium">{n.label}</span>
                        <span className="text-muted">{n.description}</span>
                      </span>
                      <input
                        type="checkbox"
                        role="switch"
                        checked={on}
                        disabled={save.isPending}
                        onChange={(e) => save.mutate({ [n.kind]: e.target.checked })}
                        className="mt-1 h-4 w-4 shrink-0 accent-accent"
                        aria-label={`Send "${n.label}"`}
                      />
                    </label>
                  </li>
                );
              })}
          </ul>
        </Panel>
      ))}
      <ErrorText error={save.error} />
    </>
  );
}

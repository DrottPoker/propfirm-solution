"use client";

import Link from "next/link";
import { useState } from "react";

import type { FirmSettings } from "@/lib/api/types";
import { isOn, notificationKinds, type NotificationAudience, type NotificationKind } from "@/lib/notifications";
import { useFirmSettings, useSaveEmailSettings, useSaveSupportEmail } from "@/lib/queries";
import { useSavedNote } from "@/lib/useSavedNote";

import { TeamOnlyNote } from "./BeforeApproval";
import { EmailPreview } from "./EmailPreview";
import { EyeIcon } from "./icons";
import { AdminPage, ErrorText, fieldClass, Loading, Message, PageHeader, Panel, secondaryButtonClass, Switch } from "./ui";

/** Which emails we send for the firm: to its team when something waits for it, and to its traders as their challenges move on. */
export function AdminNotifications() {
  const settings = useFirmSettings();
  if (settings.isError) {
    return <Message text={settings.error.message} />;
  }

  if (!settings.data) {
    return <Loading />;
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
  useSavedNote(save);
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
  useSavedNote(save);
  // The email whose preview is open.
  const [showing, setShowing] = useState<NotificationKind | null>(null);

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
                  <li key={n.kind} className="flex items-start justify-between gap-4 py-3 text-sm">
                    <span className="flex flex-col items-start gap-0.5">
                      <span className="font-medium">{n.label}</span>
                      <span className="text-muted">{n.description}</span>
                      <button
                        type="button"
                        onClick={() => setShowing(n)}
                        aria-label={`Show the email: ${n.label}`}
                        className="mt-1 flex items-center gap-1.5 rounded text-accent hover:underline"
                      >
                        <EyeIcon className="size-3.5" />
                        Show the email
                      </button>
                    </span>
                    <Switch checked={on} disabled={save.isPending} onChange={(checked) => save.mutate({ [n.kind]: checked })} label={`Send "${n.label}"`} />
                  </li>
                );
              })}
          </ul>
        </Panel>
      ))}
      <ErrorText error={save.error} />
      <EmailPreview email={showing} on={showing === null || isOn(settings.emailSettings, showing.kind)} onClose={() => setShowing(null)} />
    </>
  );
}
